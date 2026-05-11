using BO.Core.Configuration;
using System.Text.RegularExpressions;
using TreeSitter;

namespace BO.Core.Indexing;

public sealed class BoundaryExtractor
{
    private static readonly Regex ImportClausePattern = new(
        @"^\s*import\s+(?<clause>.+?)\s+from\s+['""][^'""]+['""]",
        RegexOptions.Compiled);

    private static readonly Regex RequireAssignmentPattern = new(
        @"^\s*(?:const|let|var)\s+(?<name>[$A-Za-z_][\w$]*)\s*=\s*require\((?<quote>['""])(?<pkg>[^'""]+)\k<quote>\)",
        RegexOptions.Compiled);

    public IReadOnlyList<BoundaryInteractionRecord> Extract(
        IReadOnlyList<FileRecord> files,
        PackageClassificationRules rules,
        BoConfiguration? boConfiguration = null)
    {
        var effectiveConfig = boConfiguration ?? BoConfiguration.Empty;
        var interactions = new List<BoundaryInteractionRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            foreach (var interaction in ExtractConfiguredPathBoundaryInteractions(file, effectiveConfig))
            {
                if (seen.Add(interaction.Id))
                {
                    interactions.Add(interaction);
                }
            }

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
                    // C# boundary detection via using directives
                    var usingDirectives = ExtractCSharpUsingNamespaces(tree.RootNode)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();

                    foreach (var usingNs in usingDirectives)
                    {
                        foreach (var rule in rules.Boundaries)
                        {
                            if (!MatchesPackageRule(usingNs, rule))
                            {
                                continue;
                            }

                            var interaction = new BoundaryInteractionRecord(
                                CreateInteractionId(file.Id, rule.BoundaryType, "use", usingNs),
                                file.Id,
                                rule.BoundaryType,
                                "use",
                                usingNs,
                                DetermineEffectMode(rule.BoundaryType),
                                0.8);

                            if (seen.Add(interaction.Id))
                            {
                                interactions.Add(interaction);
                            }
                        }
                    }
                }
                else
                {
                    // TS/JS boundary detection via import/require
                    var bindings = ExtractPackageBindings(tree.RootNode);
                    var instanceBindings = ExtractInstanceBindings(tree.RootNode, bindings);
                    var imports = ExtractExternalImports(tree.RootNode)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    var typedInteractions = ExtractTypedInteractions(file, tree.RootNode, bindings, instanceBindings, rules)
                        .ToArray();
                    var typedPackagesByBoundary = typedInteractions
                        .Select(interaction => new
                        {
                            interaction.BoundaryType,
                            PackageName = ResolvePackageFromTarget(interaction.TargetName, bindings, instanceBindings)
                        })
                        .Where(item => !string.IsNullOrWhiteSpace(item.PackageName))
                        .Select(item => $"{item.BoundaryType}:{item.PackageName}")
                        .ToHashSet(StringComparer.Ordinal);

                    foreach (var interaction in typedInteractions)
                    {
                        if (seen.Add(interaction.Id))
                        {
                            interactions.Add(interaction);
                        }
                    }

                    foreach (var importText in imports)
                    {
                        foreach (var rule in rules.Boundaries)
                        {
                            if (!MatchesPackageRule(importText, rule))
                            {
                                continue;
                            }

                            if (typedPackagesByBoundary.Contains($"{rule.BoundaryType}:{importText}"))
                            {
                                continue;
                            }

                            var interaction = new BoundaryInteractionRecord(
                                CreateInteractionId(file.Id, rule.BoundaryType, "use", importText),
                                file.Id,
                                rule.BoundaryType,
                                "use",
                                importText,
                                DetermineEffectMode(rule.BoundaryType),
                                0.8);

                            if (seen.Add(interaction.Id))
                            {
                                interactions.Add(interaction);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Boundary extraction stays best-effort during indexing.
            }
        }

        return interactions;
    }

    private static IEnumerable<BoundaryInteractionRecord> ExtractConfiguredPathBoundaryInteractions(
        FileRecord file,
        BoConfiguration config)
    {
        foreach (var boundary in config.Boundaries)
        {
            if (string.IsNullOrWhiteSpace(boundary.Name) ||
                !PathPatternMatcher.MatchesAny(file.NormalizedPath, boundary.PathPatterns))
            {
                continue;
            }

            yield return new BoundaryInteractionRecord(
                CreateInteractionId(file.Id, boundary.Name, "own", file.NormalizedPath),
                file.Id,
                boundary.Name,
                "own",
                file.NormalizedPath,
                boundary.Generated ? "generated" : "internal",
                1.0);
        }
    }

    private static IReadOnlyDictionary<string, string> ExtractPackageBindings(Node rootNode)
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var child in rootNode.NamedChildren)
        {
            switch (child.Type)
            {
                case "import_statement":
                    {
                        var sourceNode = child.GetChildForField("source");
                        if (!IsValidNode(sourceNode))
                        {
                            break;
                        }

                        var packageName = NormalizeStringLiteral(sourceNode!.Text);
                        if (string.IsNullOrWhiteSpace(packageName) || packageName.StartsWith(".", StringComparison.Ordinal))
                        {
                            break;
                        }

                        RegisterImportBindings(bindings, child.Text, packageName);
                        break;
                    }
                case "lexical_declaration":
                case "variable_declaration":
                    foreach (var declarator in EnumerateNodesByType(child, "variable_declarator"))
                    {
                        var declaratorText = declarator.Text ?? string.Empty;
                        var match = RequireAssignmentPattern.Match(declaratorText);
                        if (!match.Success)
                        {
                            continue;
                        }

                        var packageName = match.Groups["pkg"].Value;
                        if (!packageName.StartsWith(".", StringComparison.Ordinal))
                        {
                            bindings[match.Groups["name"].Value] = packageName;
                        }
                    }
                    break;
            }
        }

        return bindings;
    }

    private static IReadOnlyDictionary<string, string> ExtractInstanceBindings(
        Node rootNode,
        IReadOnlyDictionary<string, string> packageBindings)
    {
        var instances = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var declarator in EnumerateNodesByType(rootNode, "variable_declarator"))
        {
            var nameNode = declarator.GetChildForField("name");
            var valueNode = declarator.GetChildForField("value");
            if (!IsValidNode(nameNode) || !IsValidNode(valueNode))
            {
                continue;
            }

            var packageName = ResolvePackageFromValue(valueNode!, packageBindings);
            if (packageName is null)
            {
                continue;
            }

            instances[nameNode!.Text] = packageName;
        }

        return instances;
    }

    private static IEnumerable<BoundaryInteractionRecord> ExtractTypedInteractions(
        FileRecord file,
        Node rootNode,
        IReadOnlyDictionary<string, string> packageBindings,
        IReadOnlyDictionary<string, string> instanceBindings,
        PackageClassificationRules rules)
    {
        var interactions = new List<BoundaryInteractionRecord>();

        foreach (var callNode in EnumerateNodesByType(rootNode, "call_expression"))
        {
            var functionNode = callNode.GetChildForField("function");
            if (!IsValidNode(functionNode))
            {
                continue;
            }

            var callPath = functionNode!.Text;
            var rootIdentifier = GetRootIdentifier(callPath);
            if (rootIdentifier is null)
            {
                continue;
            }

            string? packageName = null;
            if (!packageBindings.TryGetValue(rootIdentifier, out packageName) &&
                !instanceBindings.TryGetValue(rootIdentifier, out packageName))
            {
                continue;
            }

            var rule = rules.Boundaries.FirstOrDefault(candidate => MatchesPackageRule(packageName, candidate));
            if (rule is null)
            {
                continue;
            }

            var operation = ResolveOperationType(rule, callPath);
            if (operation is null)
            {
                continue;
            }

            interactions.Add(new BoundaryInteractionRecord(
                CreateInteractionId(file.Id, rule.BoundaryType, operation, callPath),
                file.Id,
                rule.BoundaryType,
                operation,
                callPath,
                DetermineEffectMode(rule.BoundaryType),
                0.9));
        }

        return interactions;
    }

    private static IEnumerable<string> ExtractExternalImports(Node rootNode)
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
                            if (!string.IsNullOrWhiteSpace(importText) && !importText.StartsWith(".", StringComparison.Ordinal))
                            {
                                yield return importText;
                            }
                        }
                        break;
                    }
                case "lexical_declaration":
                case "variable_declaration":
                case "expression_statement":
                    foreach (var importText in ExtractExternalRequireTargets(child))
                    {
                        yield return importText;
                    }
                    break;
            }
        }
    }

    private static IEnumerable<string> ExtractExternalRequireTargets(Node node)
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
            if (!string.IsNullOrWhiteSpace(importText) && !importText.StartsWith(".", StringComparison.Ordinal))
            {
                yield return importText;
            }
        }
    }

    private static bool MatchesPackageRule(string importText, BoundaryRule rule)
    {
        return rule.Packages.Any(package =>
            string.Equals(importText, package, StringComparison.Ordinal)
            || importText.StartsWith(package + "/", StringComparison.Ordinal));
    }

    private static string CreateInteractionId(string fileId, string boundaryType, string operationType, string targetName)
    {
        var shape = $"{fileId}|{boundaryType}|{operationType}|{targetName}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(shape));
        return $"boundary:{fileId}:{boundaryType}:{Convert.ToHexStringLower(bytes[..6])}";
    }

    private static string DetermineEffectMode(string boundaryType) =>
        boundaryType is "logging" or "metrics" ? "observable" : "external";

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

    private static void RegisterImportBindings(
        IDictionary<string, string> bindings,
        string importText,
        string packageName)
    {
        var match = ImportClausePattern.Match(importText);
        if (!match.Success)
        {
            return;
        }

        var clause = match.Groups["clause"].Value.Trim();
        if (clause.StartsWith("{", StringComparison.Ordinal))
        {
            RegisterNamedBindings(bindings, clause, packageName);
            return;
        }

        if (clause.StartsWith("* as ", StringComparison.Ordinal))
        {
            bindings[clause["* as ".Length..].Trim()] = packageName;
            return;
        }

        if (clause.Contains('{', StringComparison.Ordinal))
        {
            var parts = clause.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && !parts[0].StartsWith("{", StringComparison.Ordinal))
            {
                bindings[parts[0].Trim()] = packageName;
            }

            if (parts.Length == 2)
            {
                RegisterNamedBindings(bindings, parts[1], packageName);
            }
            return;
        }

        bindings[clause] = packageName;
    }

    private static void RegisterNamedBindings(
        IDictionary<string, string> bindings,
        string clause,
        string packageName)
    {
        var trimmed = clause.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            return;
        }

        var content = trimmed[1..^1];
        foreach (var entry in content.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(" as ", StringSplitOptions.TrimEntries);
            var localName = parts.Length == 2 ? parts[1] : parts[0];
            if (!string.IsNullOrWhiteSpace(localName))
            {
                bindings[localName] = packageName;
            }
        }
    }

    private static string? ResolvePackageFromValue(Node valueNode, IReadOnlyDictionary<string, string> packageBindings)
    {
        if (valueNode.Type == "new_expression")
        {
            var constructorNode = valueNode.GetChildForField("constructor");
            if (IsValidNode(constructorNode) && packageBindings.TryGetValue(constructorNode!.Text, out var packageName))
            {
                return packageName;
            }
        }

        if (valueNode.Type == "call_expression")
        {
            var functionNode = valueNode.GetChildForField("function");
            if (IsValidNode(functionNode) && packageBindings.TryGetValue(functionNode!.Text, out var packageName))
            {
                return packageName;
            }
        }

        return null;
    }

    private static string? ResolveOperationType(BoundaryRule rule, string callPath)
    {
        foreach (var overrideRule in rule.OperationOverrides)
        {
            if (WildcardMatches(overrideRule.Key, callPath))
            {
                return overrideRule.Value;
            }
        }

        if (rule.SymbolPatterns.Any(pattern => WildcardMatches(pattern, callPath)))
        {
            return "use";
        }

        var terminal = callPath.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (terminal is not null)
        {
            foreach (var overrideRule in rule.OperationOverrides)
            {
                if (WildcardMatches(overrideRule.Key, terminal))
                {
                    return overrideRule.Value;
                }
            }

            if (rule.SymbolPatterns.Any(pattern => WildcardMatches(pattern, terminal)))
            {
                return "use";
            }
        }

        return null;
    }

    private static bool WildcardMatches(string pattern, string value)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string? GetRootIdentifier(string callPath)
    {
        var segments = callPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        return segments[0];
    }

    private static string? ResolvePackageFromTarget(
        string targetName,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlyDictionary<string, string> instanceBindings)
    {
        var root = GetRootIdentifier(targetName);
        if (root is null)
        {
            return null;
        }

        if (bindings.TryGetValue(root, out var bindingPackage))
        {
            return bindingPackage;
        }

        if (instanceBindings.TryGetValue(root, out var instancePackage))
        {
            return instancePackage;
        }

        return root;
    }

    private static bool IsValidNode(Node? node) => node is not null && node.Id != IntPtr.Zero;

    // ── C# using namespace extraction ──────────────────────────────────────

    private static IEnumerable<string> ExtractCSharpUsingNamespaces(Node rootNode)
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

            if (child.Type is "file_scoped_namespace_declaration" or "namespace_declaration")
            {
                foreach (var ns in ExtractCSharpUsingNamespaces(child))
                {
                    yield return ns;
                }
            }
        }
    }
}
