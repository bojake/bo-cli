using BO.Core.Ids;
using TreeSitter;

namespace BO.Core.Indexing;

public sealed class DependencyExtractor
{
    private readonly BoIdGenerator _idGenerator;

    public DependencyExtractor(BoIdGenerator idGenerator)
    {
        _idGenerator = idGenerator;
    }

    public IReadOnlyList<FileDependencyRecord> Extract(IReadOnlyList<FileRecord> files)
    {
        var byNormalizedPath = files.ToDictionary(file => file.NormalizedPath, StringComparer.Ordinal);
        var dependencies = new List<FileDependencyRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Build a namespace → file lookup for C# using resolution
        var namespaceToFiles = BuildNamespaceToFileLookup(files);

        foreach (var file in files)
        {
            if (file.IsGenerated)
            {
                continue;
            }

            string sourceText;
            try
            {
                sourceText = File.ReadAllText(file.Path);
            }
            catch
            {
                continue;
            }

            try
            {
                var langInfo = LanguageRegistry.GetLanguageInfo(file);
                if (langInfo is null)
                {
                    continue;
                }

                using var language = new Language(langInfo.LibraryName, langInfo.FunctionName);
                using var parser = new Parser(language);
                using var tree = parser.Parse(sourceText);
                if (tree is null)
                {
                    continue;
                }

                if (file.Language == "csharp")
                {
                    ExtractCSharpDependencies(file, tree.RootNode, namespaceToFiles, seen, dependencies);
                }
                else
                {
                    ExtractJsTsDependencies(file, tree.RootNode, sourceText, byNormalizedPath, seen, dependencies);
                }
            }
            catch
            {
                // Leave dependency extraction best-effort for now.
            }
        }

        return dependencies;
    }

    // ── C# using directive extraction ────────────────────────────────────────

    private void ExtractCSharpDependencies(
        FileRecord file,
        Node rootNode,
        IReadOnlyDictionary<string, List<FileRecord>> namespaceToFiles,
        HashSet<string> seen,
        List<FileDependencyRecord> dependencies)
    {
        foreach (var usingText in ExtractCSharpUsingDirectives(rootNode))
        {
            if (!namespaceToFiles.TryGetValue(usingText, out var targetFiles))
            {
                // Try prefix match: "using BO.Core.Indexing" should match files in "BO.Core.Indexing.*"
                foreach (var kvp in namespaceToFiles)
                {
                    if (kvp.Key.StartsWith(usingText + ".", StringComparison.Ordinal)
                        || kvp.Key.StartsWith(usingText, StringComparison.Ordinal))
                    {
                        foreach (var targetFile in kvp.Value)
                        {
                            if (targetFile.Id == file.Id)
                            {
                                continue;
                            }

                            var edgeId = CreateDependencyId(file.Id, targetFile.Id, usingText);
                            if (seen.Add(edgeId))
                            {
                                dependencies.Add(new FileDependencyRecord(
                                    edgeId, file.Id, targetFile.Id, usingText,
                                    IsRuntime: true, IsCompileTime: true));
                            }
                        }
                    }
                }
                continue;
            }

            foreach (var targetFile in targetFiles)
            {
                if (targetFile.Id == file.Id)
                {
                    continue;
                }

                var edgeId = CreateDependencyId(file.Id, targetFile.Id, usingText);
                if (seen.Add(edgeId))
                {
                    dependencies.Add(new FileDependencyRecord(
                        edgeId, file.Id, targetFile.Id, usingText,
                        IsRuntime: true, IsCompileTime: true));
                }
            }
        }
    }

    private static IEnumerable<string> ExtractCSharpUsingDirectives(Node rootNode)
    {
        foreach (var child in rootNode.NamedChildren)
        {
            if (child.Type == "using_directive")
            {
                var nameNode = child.GetChildForField("name")
                    ?? child.NamedChildren.FirstOrDefault(
                        c => c.Type is "qualified_name" or "identifier" or "name");
                if (IsValidNode(nameNode))
                {
                    yield return nameNode!.Text;
                }
            }

            // Also search inside file-scoped namespaces
            if (child.Type == "file_scoped_namespace_declaration" || child.Type == "namespace_declaration")
            {
                foreach (var usingText in ExtractCSharpUsingDirectives(child))
                {
                    yield return usingText;
                }
            }
        }
    }

    private static Dictionary<string, List<FileRecord>> BuildNamespaceToFileLookup(IReadOnlyList<FileRecord> files)
    {
        var lookup = new Dictionary<string, List<FileRecord>>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (file.Language != "csharp" || file.IsGenerated)
            {
                continue;
            }

            // Derive namespace from directory path: BO.Core/Indexing/Foo.cs → "BO.Core.Indexing"
            var dir = Path.GetDirectoryName(file.NormalizedPath)?.Replace('/', '.').Replace('\\', '.') ?? "";
            if (!string.IsNullOrEmpty(dir))
            {
                if (!lookup.TryGetValue(dir, out var list))
                {
                    list = [];
                    lookup[dir] = list;
                }
                list.Add(file);
            }

            // Also try to extract actual namespace from file content (best effort)
            try
            {
                var content = File.ReadAllText(file.Path);
                foreach (var ns in ExtractNamespacesFromContent(content))
                {
                    if (!lookup.TryGetValue(ns, out var nsList))
                    {
                        nsList = [];
                        lookup[ns] = nsList;
                    }
                    if (!nsList.Any(f => f.Id == file.Id))
                    {
                        nsList.Add(file);
                    }
                }
            }
            catch
            {
                // Best effort
            }
        }

        return lookup;
    }

    private static IEnumerable<string> ExtractNamespacesFromContent(string content)
    {
        // Quick regex-based extraction: namespace X.Y.Z { or namespace X.Y.Z;
        foreach (System.Text.RegularExpressions.Match match in
            System.Text.RegularExpressions.Regex.Matches(
                content,
                @"namespace\s+([\w.]+)\s*[{;]",
                System.Text.RegularExpressions.RegexOptions.Compiled))
        {
            yield return match.Groups[1].Value;
        }
    }

    // ── TS/JS import extraction (existing logic) ─────────────────────────────

    private void ExtractJsTsDependencies(
        FileRecord file,
        Node rootNode,
        string sourceText,
        IReadOnlyDictionary<string, FileRecord> byNormalizedPath,
        HashSet<string> seen,
        List<FileDependencyRecord> dependencies)
    {
        foreach (var importText in ExtractImportTargets(rootNode))
        {
            var targetFile = ResolveImportedFile(file, importText, byNormalizedPath);
            if (targetFile is null)
            {
                continue;
            }

            var edgeId = CreateDependencyId(file.Id, targetFile.Id, importText);
            if (!seen.Add(edgeId))
            {
                continue;
            }

            dependencies.Add(new FileDependencyRecord(
                edgeId,
                file.Id,
                targetFile.Id,
                importText,
                IsRuntimeImport(importText, sourceText),
                true));
        }
    }

    private static IEnumerable<string> ExtractImportTargets(Node rootNode)
    {
        foreach (var child in rootNode.NamedChildren)
        {
            switch (child.Type)
            {
                case "import_statement":
                case "export_statement":
                    {
                        var sourceNode = child.GetChildForField("source");
                        if (IsValidNode(sourceNode))
                        {
                            var importText = NormalizeStringLiteral(sourceNode!.Text);
                            if (!string.IsNullOrWhiteSpace(importText))
                            {
                                yield return importText;
                            }
                        }
                        break;
                    }
                case "lexical_declaration":
                case "variable_declaration":
                case "expression_statement":
                    foreach (var importText in ExtractRequireTargets(child))
                    {
                        yield return importText;
                    }
                    break;
            }
        }
    }

    private static IEnumerable<string> ExtractRequireTargets(Node node)
    {
        foreach (var callNode in EnumerateNodesByType(node, "call_expression"))
        {
            var functionNode = callNode.GetChildForField("function");
            if (!IsValidNode(functionNode) || !string.Equals(functionNode!.Text, "require", StringComparison.Ordinal))
            {
                continue;
            }

            var argumentsNode = callNode.GetChildForField("arguments");
            if (!IsValidNode(argumentsNode))
            {
                continue;
            }

            var firstArgument = argumentsNode!.NamedChildren.FirstOrDefault();
            if (!IsValidNode(firstArgument))
            {
                continue;
            }

            var importText = NormalizeStringLiteral(firstArgument!.Text);
            if (!string.IsNullOrWhiteSpace(importText))
            {
                yield return importText;
            }
        }
    }

    private static FileRecord? ResolveImportedFile(
        FileRecord fromFile,
        string importText,
        IReadOnlyDictionary<string, FileRecord> byNormalizedPath)
    {
        if (!importText.StartsWith(".", StringComparison.Ordinal))
        {
            return null;
        }

        var fromDirectory = Path.GetDirectoryName(fromFile.NormalizedPath)?.Replace('\\', '/') ?? string.Empty;
        var combined = string.IsNullOrEmpty(fromDirectory)
            ? importText
            : $"{fromDirectory}/{importText}";

        var normalized = NormalizeRelativePath(combined);
        foreach (var candidate in EnumerateCandidatePaths(normalized))
        {
            if (byNormalizedPath.TryGetValue(candidate, out var targetFile))
            {
                return targetFile;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string normalizedPath)
    {
        if (Path.HasExtension(normalizedPath))
        {
            yield return normalizedPath;
            yield break;
        }

        foreach (var extension in new[] { ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs" })
        {
            yield return normalizedPath + extension;
        }

        foreach (var extension in new[] { "/index.ts", "/index.tsx", "/index.js", "/index.jsx", "/index.mjs", "/index.cjs" })
        {
            yield return normalizedPath + extension;
        }
    }

    // ── Shared Helpers ────────────────────────────────────────────────────────

    private string CreateDependencyId(string fromFileId, string toFileId, string importText)
    {
        var normalized = importText.Trim();
        var shape = $"{fromFileId}|imports|{toFileId}|{normalized}";
        return $"edge:{fromFileId}:imports:{toFileId}:{ShortHash(shape)}";
    }

    private static string NormalizeStringLiteral(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'') ||
             (trimmed[0] == '`' && trimmed[^1] == '`')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static string NormalizeRelativePath(string path)
    {
        var segments = new Stack<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.Pop();
                }
                continue;
            }

            segments.Push(segment);
        }

        return string.Join("/", segments.Reverse());
    }

    private static IEnumerable<Node> EnumerateNodesByType(Node node, string type)
    {
        foreach (var child in node.NamedChildren)
        {
            if (child.Type == type)
            {
                yield return child;
            }

            foreach (var descendant in EnumerateNodesByType(child, type))
            {
                yield return descendant;
            }
        }
    }

    private static bool IsValidNode(Node? node) => node is not null && node.Id != IntPtr.Zero;

    private static bool IsRuntimeImport(string importText, string sourceText)
    {
        return sourceText.Contains($"require(\"{importText}\")", StringComparison.Ordinal)
            || sourceText.Contains($"require('{importText}')", StringComparison.Ordinal);
    }

    private static string ShortHash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes[..6]);
    }
}

