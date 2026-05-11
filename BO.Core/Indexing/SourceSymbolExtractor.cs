using System.Text;
using System.Text.RegularExpressions;
using BO.Core.Ids;
using TreeSitter;

namespace BO.Core.Indexing;

public sealed class SourceSymbolExtractor
{
    private static readonly Regex FunctionPattern = new(
        @"^\s*(?<export>export\s+)?(?<default>default\s+)?(?<async>async\s+)?function\s+(?<name>[$A-Za-z_][\w$]*)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex AnonymousDefaultFunctionPattern = new(
        @"^\s*export\s+default\s+(?<async>async\s+)?function\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex ClassPattern = new(
        @"^\s*(?<export>export\s+)?(?<default>default\s+)?(?<abstract>abstract\s+)?class\s+(?<name>[$A-Za-z_][\w$]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex AnonymousDefaultClassPattern = new(
        @"^\s*export\s+default\s+class\b",
        RegexOptions.Compiled);

    private static readonly Regex InterfacePattern = new(
        @"^\s*(?<export>export\s+)?interface\s+(?<name>[$A-Za-z_][\w$]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex TypeAliasPattern = new(
        @"^\s*(?<export>export\s+)?type\s+(?<name>[$A-Za-z_][\w$]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex EnumPattern = new(
        @"^\s*(?<export>export\s+)?enum\s+(?<name>[$A-Za-z_][\w$]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex VariablePattern = new(
        @"^\s*(?<export>export\s+)?(?<keyword>const|let|var)\s+(?<name>[$A-Za-z_][\w$]*)\s*(?::[^=]+)?=",
        RegexOptions.Compiled);

    private static readonly Regex CommonJsExportPattern = new(
        @"^\s*(?:module\.exports|exports)\.(?<name>[$A-Za-z_][\w$]*)\s*=",
        RegexOptions.Compiled);

    private static readonly Regex CommonJsDefaultPattern = new(
        @"^\s*module\.exports\s*=\s*(?<name>[$A-Za-z_][\w$]*)\s*;?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex NamedExportListPattern = new(
        @"^\s*export\s*\{(?<exports>[^}]*)\}",
        RegexOptions.Compiled);

    private static readonly Regex CommonJsObjectExportPattern = new(
        @"^\s*module\.exports\s*=\s*\{(?<exports>[^}]*)\}",
        RegexOptions.Compiled);

    private static readonly Regex DefaultIdentifierExportPattern = new(
        @"^\s*export\s+default\s+(?<name>[$A-Za-z_][\w$]*)\s*;?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex MethodPattern = new(
        @"^\s*(?:(?:public|protected|private|static|async|get|set|readonly|override)\s+)*(?<name>[$A-Za-z_][\w$]*)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex ConstructorPattern = new(
        @"^\s*(?:(?:public|protected|private)\s+)?constructor\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex ClassFieldFunctionPattern = new(
        @"^\s*(?:(?:public|protected|private|static|readonly|override)\s+)*(?<name>[$A-Za-z_][\w$]*)\s*(?::[^=]+)?=\s*(?:async\s+)?(?:\([^)]*\)|[$A-Za-z_][\w$]*)\s*=>",
        RegexOptions.Compiled);

    private readonly BoIdGenerator _idGenerator;

    public SourceSymbolExtractor(BoIdGenerator idGenerator)
    {
        _idGenerator = idGenerator;
    }

    public SymbolExtractionResult Extract(IReadOnlyList<FileRecord> files)
    {
        var symbols = new List<SymbolRecord>();
        var warnings = new List<string>();
        var filesParsed = 0;

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
            catch (Exception ex)
            {
                warnings.Add($"Failed to read {file.NormalizedPath}: {ex.Message}");
                continue;
            }

            filesParsed++;
            symbols.AddRange(ExtractFromFile(file, sourceText));
        }

        return new SymbolExtractionResult(symbols, filesParsed, warnings);
    }

    private IReadOnlyList<SymbolRecord> ExtractFromFile(FileRecord file, string sourceText)
    {
        IReadOnlyList<SymbolRecord> parsedSymbols;
        try
        {
            parsedSymbols = ExtractWithTreeSitter(file, sourceText);
        }
        catch
        {
            // Fall back to the deterministic text pass if parsing is unavailable.
            var fallbackSymbols = ExtractWithRegexFallback(file, sourceText);
            return IsCSharp(file)
                ? AddCSharpTopLevelLocalFunctions(file, sourceText, fallbackSymbols)
                : fallbackSymbols;
        }

        if (parsedSymbols.Count > 0)
        {
            return IsCSharp(file)
                ? AddCSharpTopLevelLocalFunctions(file, sourceText, parsedSymbols)
                : parsedSymbols;
        }

        var regexSymbols = ExtractWithRegexFallback(file, sourceText);
        return IsCSharp(file)
            ? AddCSharpTopLevelLocalFunctions(file, sourceText, regexSymbols)
            : regexSymbols;
    }

    private IReadOnlyList<SymbolRecord> AddCSharpTopLevelLocalFunctions(
        FileRecord file,
        string sourceText,
        IReadOnlyList<SymbolRecord> parsedSymbols)
    {
        var symbols = parsedSymbols.ToList();
        var existingKeys = symbols
            .Select(symbol => $"{symbol.Kind}:{symbol.QualifiedName}")
            .ToHashSet(StringComparer.Ordinal);

        var lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var braceDepth = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var displayName = braceDepth == 0
                ? TryGetCSharpTopLevelLocalFunctionName(line)
                : null;
            if (displayName is not null)
            {
                var qualifiedName = $"Program.{displayName}";
                var key = $"method:{qualifiedName}";
                if (existingKeys.Add(key))
                {
                    symbols.Add(CreateSymbolRecord(
                        file,
                        qualifiedName,
                        displayName,
                        "method",
                        line.Trim(),
                        index + 1,
                        isExported: false));
                }
            }

            braceDepth = Math.Max(0, braceDepth + CountChar(line, '{') - CountChar(line, '}'));
        }

        return symbols
            .OrderBy(symbol => symbol.DeclarationLine)
            .ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? TryGetCSharpTopLevelLocalFunctionName(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("static ", StringComparison.Ordinal))
        {
            return null;
        }

        var openParen = trimmed.IndexOf('(', StringComparison.Ordinal);
        if (openParen < 0)
        {
            return null;
        }

        var declarationPrefix = trimmed[..openParen].TrimEnd();
        var tokens = declarationPrefix.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3)
        {
            return null;
        }

        var name = tokens[^1];
        if (IsMethodKeyword(name) || !IsCSharpIdentifier(name))
        {
            return null;
        }

        return name;
    }

    private static bool IsCSharpIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.Skip(1).All(ch => char.IsLetterOrDigit(ch) || ch == '_');
    }

    private static int CountChar(string value, char target)
    {
        var count = 0;
        foreach (var ch in value)
        {
            if (ch == target)
            {
                count++;
            }
        }

        return count;
    }

    private IReadOnlyList<SymbolRecord> ExtractWithTreeSitter(FileRecord file, string sourceText)
    {
        using var language = CreateTreeSitterLanguage(file);
        using var parser = new Parser(language);
        using var tree = parser.Parse(sourceText);

        if (tree is null)
        {
            return [];
        }

        var drafts = new List<SymbolDraft>();
        var namedSymbols = new Dictionary<string, int>(StringComparer.Ordinal);

        if (IsCSharp(file))
        {
            ProcessCSharpRootNode(drafts, namedSymbols, file, tree.RootNode, namespaceParts: []);
        }
        else
        {
            foreach (var child in tree.RootNode.NamedChildren)
            {
                ProcessTopLevelNode(drafts, namedSymbols, file, child, isExported: false);
            }
        }

        return FinalizeDrafts(drafts);
    }

    // ── C# AST processing ──────────────────────────────────────────────────

    private static bool IsCSharp(FileRecord file) => file.Language == "csharp";

    private void ProcessCSharpRootNode(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        Node node,
        IReadOnlyList<string> namespaceParts)
    {
        // In tree-sitter c-sharp, file-scoped namespace declarations are siblings
        // to the declarations they apply to, not parents. So we need to extract
        // the namespace first and apply it to all subsequent declarations.
        var activeParts = namespaceParts;

        foreach (var child in node.NamedChildren)
        {
            if (child.Type == "file_scoped_namespace_declaration")
            {
                var nsName = GetCSharpNamespaceIdentifier(child);
                if (nsName.Length > 0)
                {
                    activeParts = [.. namespaceParts, .. nsName.Split('.')];
                }
                // Also process any declarations INSIDE the file_scoped_namespace_declaration
                foreach (var innerChild in child.NamedChildren)
                {
                    ProcessCSharpNode(drafts, namedSymbols, file, innerChild, activeParts);
                }
                continue;
            }

            ProcessCSharpNode(drafts, namedSymbols, file, child, activeParts);
        }
    }

    private void ProcessCSharpNode(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        Node node,
        IReadOnlyList<string> namespaceParts)
    {
        switch (node.Type)
        {
            case "namespace_declaration":
                ProcessCSharpNamespace(drafts, namedSymbols, file, node, namespaceParts);
                return;
            case "file_scoped_namespace_declaration":
                ProcessCSharpFileScopedNamespace(drafts, namedSymbols, file, node, namespaceParts);
                return;
            case "class_declaration":
            case "record_declaration":
            case "record_struct_declaration":
                ProcessCSharpClassLikeDeclaration(drafts, namedSymbols, file, node, namespaceParts, "class");
                return;
            case "struct_declaration":
                ProcessCSharpClassLikeDeclaration(drafts, namedSymbols, file, node, namespaceParts, "class");
                return;
            case "interface_declaration":
                ProcessCSharpClassLikeDeclaration(drafts, namedSymbols, file, node, namespaceParts, "interface");
                return;
            case "enum_declaration":
                AddCSharpNamedDeclaration(drafts, namedSymbols, file, node, "enum", namespaceParts);
                return;
            case "delegate_declaration":
                AddCSharpNamedDeclaration(drafts, namedSymbols, file, node, "type_alias", namespaceParts);
                return;
            case "global_statement":
                // Top-level statements in C# — skip for symbol extraction
                return;
        }
    }

    private void ProcessCSharpNamespace(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        Node node,
        IReadOnlyList<string> parentParts)
    {
        var nsName = GetCSharpNamespaceIdentifier(node);
        var parts = nsName.Length > 0
            ? [.. parentParts, .. nsName.Split('.')]
            : parentParts;

        var body = node.GetChildForField("body");
        if (IsValidNode(body))
        {
            ProcessCSharpRootNode(drafts, namedSymbols, file, body!, parts);
        }
        else
        {
            foreach (var child in node.NamedChildren)
            {
                ProcessCSharpNode(drafts, namedSymbols, file, child, parts);
            }
        }
    }

    private void ProcessCSharpFileScopedNamespace(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        Node node,
        IReadOnlyList<string> parentParts)
    {
        var nsName = GetCSharpNamespaceIdentifier(node);
        var parts = nsName.Length > 0
            ? [.. parentParts, .. nsName.Split('.')]
            : parentParts;

        // File-scoped namespace: all sibling declarations after the namespace are in scope
        foreach (var child in node.NamedChildren)
        {
            ProcessCSharpNode(drafts, namedSymbols, file, child, parts);
        }
    }

    private void ProcessCSharpClassLikeDeclaration(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        Node node,
        IReadOnlyList<string> namespaceParts,
        string kind)
    {
        var className = GetCSharpIdentifier(node);
        if (string.IsNullOrWhiteSpace(className))
        {
            return;
        }

        var qualifiedName = BuildCSharpQualifiedName(namespaceParts, className);
        var isExported = HasCSharpPublicModifier(node);

        AddDraft(
            drafts,
            namedSymbols,
            file,
            qualifiedName,
            className,
            kind,
            GetNodeSignature(node),
            GetDeclarationLine(node),
            isExported);

        // Process members inside the declaration body
        var bodyNode = FindCSharpBody(node);
        if (!IsValidNode(bodyNode))
        {
            return;
        }

        foreach (var memberNode in bodyNode!.NamedChildren)
        {
            ProcessCSharpClassMember(drafts, namedSymbols, file, memberNode, namespaceParts, className);
        }
    }

    private void ProcessCSharpClassMember(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        Node memberNode,
        IReadOnlyList<string> namespaceParts,
        string className)
    {
        switch (memberNode.Type)
        {
            case "method_declaration":
            {
                var methodName = GetCSharpIdentifier(memberNode);
                if (string.IsNullOrWhiteSpace(methodName))
                {
                    return;
                }

                var qualifiedName = BuildCSharpQualifiedName(namespaceParts, $"{className}.{methodName}");
                AddDraft(
                    drafts,
                    namedSymbols,
                    file,
                    qualifiedName,
                    methodName,
                    "method",
                    GetNodeSignature(memberNode),
                    GetDeclarationLine(memberNode),
                    isExported: false);
                return;
            }
            case "constructor_declaration":
            {
                var qualifiedName = BuildCSharpQualifiedName(namespaceParts, $"{className}.{className}");
                AddDraft(
                    drafts,
                    namedSymbols,
                    file,
                    qualifiedName,
                    className,
                    "constructor",
                    GetNodeSignature(memberNode),
                    GetDeclarationLine(memberNode),
                    isExported: false);
                return;
            }
            case "property_declaration":
            {
                var propName = GetCSharpIdentifier(memberNode);
                if (string.IsNullOrWhiteSpace(propName))
                {
                    return;
                }

                var qualifiedName = BuildCSharpQualifiedName(namespaceParts, $"{className}.{propName}");
                AddDraft(
                    drafts,
                    namedSymbols,
                    file,
                    qualifiedName,
                    propName,
                    "variable",
                    GetNodeSignature(memberNode),
                    GetDeclarationLine(memberNode),
                    isExported: false);
                return;
            }
            case "field_declaration":
            {
                foreach (var declarator in EnumerateNodesByType(memberNode, "variable_declarator"))
                {
                    var fieldName = GetCSharpIdentifier(declarator);
                    if (string.IsNullOrWhiteSpace(fieldName))
                    {
                        continue;
                    }

                    var qualifiedName = BuildCSharpQualifiedName(namespaceParts, $"{className}.{fieldName}");
                    AddDraft(
                        drafts,
                        namedSymbols,
                        file,
                        qualifiedName,
                        fieldName,
                        "variable",
                        GetNodeSignature(memberNode),
                        GetDeclarationLine(memberNode),
                        isExported: false);
                }
                return;
            }
            case "class_declaration":
            case "record_declaration":
            case "record_struct_declaration":
            case "struct_declaration":
                ProcessCSharpClassLikeDeclaration(drafts, namedSymbols, file, memberNode, namespaceParts, "class");
                return;
            case "interface_declaration":
                ProcessCSharpClassLikeDeclaration(drafts, namedSymbols, file, memberNode, namespaceParts, "interface");
                return;
            case "enum_declaration":
                AddCSharpNamedDeclaration(drafts, namedSymbols, file, memberNode, "enum", namespaceParts);
                return;
            case "delegate_declaration":
                AddCSharpNamedDeclaration(drafts, namedSymbols, file, memberNode, "type_alias", namespaceParts);
                return;
        }
    }

    private void AddCSharpNamedDeclaration(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        Node node,
        string kind,
        IReadOnlyList<string> namespaceParts)
    {
        var name = GetCSharpIdentifier(node);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var qualifiedName = BuildCSharpQualifiedName(namespaceParts, name);
        AddDraft(
            drafts,
            namedSymbols,
            file,
            qualifiedName,
            name,
            kind,
            GetNodeSignature(node),
            GetDeclarationLine(node),
            HasCSharpPublicModifier(node));
    }

    private static string GetCSharpIdentifier(Node node)
    {
        // Try 'name' field first (used by most C# declarations)
        var nameNode = node.GetChildForField("name");
        if (IsValidNode(nameNode))
        {
            return nameNode!.Text;
        }

        // Fallback: look for first identifier child
        var identNode = node.NamedChildren.FirstOrDefault(
            child => child.Type is "identifier" or "type_identifier");
        return IsValidNode(identNode) ? identNode!.Text : string.Empty;
    }

    private static string GetCSharpNamespaceIdentifier(Node node)
    {
        var nameNode = node.GetChildForField("name");
        if (IsValidNode(nameNode))
        {
            return nameNode!.Text;
        }

        // Look for qualified_name or identifier
        var qualified = node.NamedChildren.FirstOrDefault(
            child => child.Type is "qualified_name" or "identifier");
        return IsValidNode(qualified) ? qualified!.Text : string.Empty;
    }

    private static bool HasCSharpPublicModifier(Node node)
    {
        // Check named children for modifier lists
        foreach (var child in node.NamedChildren)
        {
            if (child.Type is "modifier_list" or "modifiers" or "modifier")
            {
                if (child.Text.Contains("public", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        // Fallback: check if the node's text starts with "public" (handles raw modifier tokens
        // in tree-sitter grammars that expose them as anonymous children)
        var text = node.Text?.TrimStart() ?? string.Empty;
        return text.StartsWith("public ", StringComparison.Ordinal)
            || text.StartsWith("public\n", StringComparison.Ordinal)
            || text.StartsWith("public\r", StringComparison.Ordinal);
    }

    private static Node? FindCSharpBody(Node node)
    {
        var body = node.GetChildForField("body");
        if (IsValidNode(body))
        {
            return body;
        }

        // Fallback: look for declaration_list or class_body
        return node.NamedChildren.FirstOrDefault(
            child => child.Type is "declaration_list" or "class_body" or "enum_member_declaration_list");
    }

    private static string BuildCSharpQualifiedName(IReadOnlyList<string> namespaceParts, string name)
    {
        if (namespaceParts.Count == 0)
        {
            return name;
        }

        return $"{string.Join('.', namespaceParts)}.{name}";
    }

    private void ProcessTopLevelNode(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        Node node,
        bool isExported)
    {
        switch (node.Type)
        {
            case "export_statement":
                ProcessExportStatement(drafts, namedSymbols, file, node);
                return;
            case "function_declaration":
            case "generator_function_declaration":
                AddNamedDeclaration(drafts, namedSymbols, file, node, "function", isExported, defaultQualifiedName: "default");
                return;
            case "class_declaration":
            case "abstract_class_declaration":
                ProcessClassDeclaration(drafts, namedSymbols, file, node, isExported, "default");
                return;
            case "interface_declaration":
                AddNamedDeclaration(drafts, namedSymbols, file, node, "interface", isExported);
                return;
            case "type_alias_declaration":
                AddNamedDeclaration(drafts, namedSymbols, file, node, "type_alias", isExported);
                return;
            case "enum_declaration":
                AddNamedDeclaration(drafts, namedSymbols, file, node, "enum", isExported);
                return;
            case "lexical_declaration":
            case "variable_declaration":
                AddVariableDeclarations(drafts, namedSymbols, file, node, isExported);
                return;
            case "expression_statement":
                ProcessExpressionStatement(drafts, namedSymbols, file, node);
                return;
        }
    }

    private void ProcessExportStatement(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        Node exportNode)
    {
        foreach (var child in exportNode.NamedChildren)
        {
            switch (child.Type)
            {
                case "export_clause":
                    MarkExportClause(child, namedSymbols, drafts);
                    break;
                case "identifier":
                    MarkSymbolExported(namedSymbols, drafts, child.Text);
                    break;
                default:
                    ProcessTopLevelNode(drafts, namedSymbols, file, child, isExported: true);
                    break;
            }
        }
    }

    private void ProcessClassDeclaration(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        Node classNode,
        bool isExported,
        string? defaultQualifiedName = null)
    {
        var className = GetNameNode(classNode)?.Text;
        if (string.IsNullOrWhiteSpace(className))
        {
            className = defaultQualifiedName ?? "default";
        }

        AddDraft(
            drafts,
            namedSymbols,
            file,
            className,
            className,
            "class",
            GetNodeSignature(classNode),
            GetDeclarationLine(classNode),
            isExported);

        var bodyNode = classNode.GetChildForField("body");
        if (!IsValidNode(bodyNode))
        {
            bodyNode = classNode.NamedChildren.FirstOrDefault(child => child.Type is "class_body" or "object_type");
        }

        if (!IsValidNode(bodyNode))
        {
            return;
        }

        foreach (var memberNode in bodyNode!.NamedChildren)
        {
            ProcessClassMember(drafts, namedSymbols, file, className, memberNode);
        }
    }

    private void AddNamedDeclaration(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        Node node,
        string kind,
        bool isExported,
        string? defaultQualifiedName = null)
    {
        var nameNode = GetNameNode(node);
        var qualifiedName = IsValidNode(nameNode)
            ? nameNode!.Text
            : defaultQualifiedName ?? kind;

        AddDraft(
            drafts,
            namedSymbols,
            file,
            qualifiedName,
            qualifiedName,
            kind,
            GetNodeSignature(node),
            GetDeclarationLine(node),
            isExported);
    }

    private void ProcessClassMember(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        string className,
        Node memberNode)
    {
        switch (memberNode.Type)
        {
            case "method_definition":
            case "method_signature":
            case "abstract_method_signature":
                {
                    var nameNode = GetNameNode(memberNode);
                    if (!IsValidNode(nameNode))
                    {
                        return;
                    }

                    var memberName = nameNode!.Text;
                    var kind = string.Equals(memberName, "constructor", StringComparison.Ordinal) ? "constructor" : "method";
                    AddDraft(
                        drafts,
                        namedSymbols,
                        file,
                        $"{className}.{memberName}",
                        memberName,
                        kind,
                        GetNodeSignature(memberNode),
                        GetDeclarationLine(memberNode),
                        isExported: false);
                    return;
                }
            case "public_field_definition":
            case "field_definition":
            case "property_signature":
                {
                    var nameNode = GetNameNode(memberNode);
                    if (!IsValidNode(nameNode))
                    {
                        return;
                    }

                    var valueNode = memberNode.GetChildForField("value");
                    var isMethodLikeField = IsValidNode(valueNode) && valueNode!.Type is "arrow_function" or "function_expression";
                    if (!isMethodLikeField && !memberNode.Text.Contains("=>", StringComparison.Ordinal))
                    {
                        return;
                    }

                    var memberName = nameNode!.Text;
                    AddDraft(
                        drafts,
                        namedSymbols,
                        file,
                        $"{className}.{memberName}",
                        memberName,
                        "method",
                        GetNodeSignature(memberNode),
                        GetDeclarationLine(memberNode),
                        isExported: false);
                    return;
                }
        }
    }

    private void AddVariableDeclarations(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        Node declarationNode,
        bool isExported)
    {
        foreach (var declarator in EnumerateNodesByType(declarationNode, "variable_declarator"))
        {
            var nameNode = declarator.GetChildForField("name");
            if (!IsValidNode(nameNode))
            {
                continue;
            }

            AddDraft(
                drafts,
                namedSymbols,
                file,
                nameNode!.Text,
                nameNode.Text,
                "variable",
                GetNodeSignature(declarator),
                GetDeclarationLine(declarator),
                isExported);
        }
    }

    private void ProcessExpressionStatement(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        Node expressionNode)
    {
        foreach (var assignment in EnumerateNodesByType(expressionNode, "assignment_expression"))
        {
            var leftNode = assignment.GetChildForField("left");
            var rightNode = assignment.GetChildForField("right");

            if (!IsValidNode(leftNode))
            {
                continue;
            }

            var leftText = leftNode!.Text;
            if (string.Equals(leftText, "module.exports", StringComparison.Ordinal))
            {
                if (IsValidNode(rightNode) && rightNode!.Type == "identifier")
                {
                    MarkSymbolExported(namedSymbols, drafts, rightNode.Text);
                    continue;
                }

                if (IsValidNode(rightNode) && rightNode!.Type == "object")
                {
                    MarkObjectExports(rightNode, namedSymbols, drafts);
                }

                continue;
            }

            if (leftText.StartsWith("exports.", StringComparison.Ordinal) || leftText.StartsWith("module.exports.", StringComparison.Ordinal))
            {
                var exportedName = leftText[(leftText.LastIndexOf('.') + 1)..];
                if (IsValidNode(rightNode) && rightNode!.Type == "identifier")
                {
                    MarkSymbolExported(namedSymbols, drafts, rightNode.Text);
                    continue;
                }

                AddDraft(
                    drafts,
                    namedSymbols,
                    file,
                    exportedName,
                    exportedName,
                    "variable",
                    GetNodeSignature(assignment),
                    GetDeclarationLine(assignment),
                    isExported: true);
            }
        }
    }

    private static void MarkExportClause(
        Node exportClause,
        IReadOnlyDictionary<string, int> namedSymbols,
        List<SymbolDraft> drafts)
    {
        foreach (var exportNode in exportClause.NamedChildren)
        {
            if (exportNode.Type == "export_specifier")
            {
                var nameNode = exportNode.GetChildForField("name");
                if (IsValidNode(nameNode))
                {
                    MarkSymbolExported(namedSymbols, drafts, nameNode!.Text);
                }

                continue;
            }

            if (exportNode.Type == "identifier")
            {
                MarkSymbolExported(namedSymbols, drafts, exportNode.Text);
            }
        }
    }

    private static void MarkObjectExports(
        Node objectNode,
        IReadOnlyDictionary<string, int> namedSymbols,
        List<SymbolDraft> drafts)
    {
        foreach (var child in objectNode.NamedChildren)
        {
            switch (child.Type)
            {
                case "pair":
                    {
                        var valueNode = child.GetChildForField("value");
                        if (IsValidNode(valueNode) && valueNode!.Type == "identifier")
                        {
                            MarkSymbolExported(namedSymbols, drafts, valueNode.Text);
                        }
                        break;
                    }
                case "shorthand_property_identifier":
                case "identifier":
                    MarkSymbolExported(namedSymbols, drafts, child.Text);
                    break;
            }
        }
    }

    private static Node? GetNameNode(Node node)
    {
        var nameNode = node.GetChildForField("name");
        if (IsValidNode(nameNode))
        {
            return nameNode;
        }

        return node.NamedChildren.FirstOrDefault(child => child.Type is "identifier" or "property_identifier" or "type_identifier");
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

    private static int GetDeclarationLine(Node node) => node.StartPosition.Row + 1;

    private static string GetNodeSignature(Node node)
    {
        var text = node.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return node.Type;
        }

        var signature = ExtractDeclarationSignature(text);
        return string.IsNullOrWhiteSpace(signature) ? node.Type : signature;
    }

    private static string ExtractDeclarationSignature(string text)
    {
        var builder = new StringBuilder(text.Length);
        var parenDepth = 0;
        var angleDepth = 0;
        var lastWasWhitespace = false;

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';

            switch (ch)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0)
                    {
                        parenDepth--;
                    }
                    break;
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    if (angleDepth > 0)
                    {
                        angleDepth--;
                    }
                    break;
            }

            if (parenDepth == 0 && angleDepth == 0)
            {
                if (ch == '{')
                {
                    break;
                }

                if (ch == '=' && next == '>')
                {
                    builder.Append("=>");
                    break;
                }

                if (ch == ';')
                {
                    break;
                }
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasWhitespace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasWhitespace = true;
                }

                continue;
            }

            builder.Append(ch);
            lastWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static Language CreateTreeSitterLanguage(FileRecord file)
    {
        var info = LanguageRegistry.GetLanguageInfo(file);
        if (info is not null)
        {
            return new Language(info.LibraryName, info.FunctionName);
        }

        // Fallback for TS/JS
        return file.Language == "typescript"
            ? new Language("TypeScript")
            : new Language("JavaScript");
    }

    private IReadOnlyList<SymbolRecord> ExtractWithRegexFallback(FileRecord file, string sourceText)
    {
        var drafts = new List<SymbolDraft>();
        var namedSymbols = new Dictionary<string, int>(StringComparer.Ordinal);
        var sanitizedLines = SanitizeSource(sourceText).Split('\n');
        var braceDepth = 0;
        var classScopes = new Stack<ClassScope>();
        string? pendingClassName = null;

        for (var index = 0; index < sanitizedLines.Length; index++)
        {
            var line = sanitizedLines[index];
            var trimmedLine = line.Trim();
            var lineBraceDelta = CountBraces(line);

            if (pendingClassName is not null && braceDepth == 0 && line.Contains('{', StringComparison.Ordinal))
            {
                classScopes.Push(new ClassScope(pendingClassName, braceDepth + Math.Max(1, lineBraceDelta)));
                pendingClassName = null;
            }

            if (braceDepth == 0 && trimmedLine.Length > 0)
            {
                TryAddTopLevelSymbol(drafts, namedSymbols, file, trimmedLine, index + 1, classScopes, ref pendingClassName, braceDepth, lineBraceDelta);
            }
            else if (classScopes.Count > 0 && trimmedLine.Length > 0)
            {
                TryAddClassMember(drafts, namedSymbols, file, trimmedLine, index + 1, classScopes.Peek(), braceDepth);
            }

            braceDepth += lineBraceDelta;
            if (braceDepth <= 0)
            {
                braceDepth = 0;
            }

            while (classScopes.Count > 0 && braceDepth < classScopes.Peek().BodyDepth)
            {
                classScopes.Pop();
            }
        }

        return FinalizeDrafts(drafts);
    }

    private void TryAddTopLevelSymbol(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        string line,
        int declarationLine,
        Stack<ClassScope> classScopes,
        ref string? pendingClassName,
        int braceDepth,
        int lineBraceDelta)
    {
        if (TryMatchAndAdd(drafts, namedSymbols, file, line, declarationLine, FunctionPattern, "function")) return;
        if (AnonymousDefaultFunctionPattern.IsMatch(line))
        {
            AddDraft(drafts, namedSymbols, file, "default", "default", "function", line, declarationLine, isExported: true);
            return;
        }

        if (TryMatchAndAdd(drafts, namedSymbols, file, line, declarationLine, ClassPattern, "class", out var className, out _))
        {
            if (className is not null)
            {
                if (lineBraceDelta > 0)
                {
                    classScopes.Push(new ClassScope(className, braceDepth + Math.Max(1, lineBraceDelta)));
                }
                else
                {
                    pendingClassName = className;
                }
            }
            return;
        }

        if (AnonymousDefaultClassPattern.IsMatch(line))
        {
            AddDraft(drafts, namedSymbols, file, "default", "default", "class", line, declarationLine, isExported: true);
            if (lineBraceDelta > 0)
            {
                classScopes.Push(new ClassScope("default", braceDepth + Math.Max(1, lineBraceDelta)));
            }
            else
            {
                pendingClassName = "default";
            }
            return;
        }

        if (file.Language == "typescript" && TryMatchAndAdd(drafts, namedSymbols, file, line, declarationLine, InterfacePattern, "interface")) return;
        if (file.Language == "typescript" && TryMatchAndAdd(drafts, namedSymbols, file, line, declarationLine, TypeAliasPattern, "type_alias")) return;
        if (file.Language == "typescript" && TryMatchAndAdd(drafts, namedSymbols, file, line, declarationLine, EnumPattern, "enum")) return;
        if (TryMatchAndAdd(drafts, namedSymbols, file, line, declarationLine, VariablePattern, "variable")) return;

        var commonJsMatch = CommonJsExportPattern.Match(line);
        if (commonJsMatch.Success)
        {
            var displayName = commonJsMatch.Groups["name"].Value;
            AddDraft(drafts, namedSymbols, file, displayName, displayName, "variable", line, declarationLine, isExported: true);
            return;
        }

        var commonJsDefaultMatch = CommonJsDefaultPattern.Match(line);
        if (commonJsDefaultMatch.Success)
        {
            MarkSymbolExported(namedSymbols, drafts, commonJsDefaultMatch.Groups["name"].Value);
            return;
        }

        var namedExportMatch = NamedExportListPattern.Match(line);
        if (namedExportMatch.Success)
        {
            MarkExports(namedExportMatch.Groups["exports"].Value, namedSymbols, drafts);
            return;
        }

        var commonJsObjectExportMatch = CommonJsObjectExportPattern.Match(line);
        if (commonJsObjectExportMatch.Success)
        {
            MarkExports(commonJsObjectExportMatch.Groups["exports"].Value, namedSymbols, drafts);
            return;
        }

        var defaultIdentifierMatch = DefaultIdentifierExportPattern.Match(line);
        if (defaultIdentifierMatch.Success)
        {
            MarkSymbolExported(namedSymbols, drafts, defaultIdentifierMatch.Groups["name"].Value);
        }
    }

    private void TryAddClassMember(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        string line,
        int declarationLine,
        ClassScope classScope,
        int braceDepth)
    {
        if (braceDepth != classScope.BodyDepth)
        {
            return;
        }

        if (ConstructorPattern.IsMatch(line))
        {
            AddDraft(
                drafts,
                namedSymbols,
                file,
                $"{classScope.Name}.constructor",
                "constructor",
                "constructor",
                line,
                declarationLine,
                isExported: false);
            return;
        }

        var methodMatch = MethodPattern.Match(line);
        if (methodMatch.Success)
        {
            var name = methodMatch.Groups["name"].Value;
            if (!IsMethodKeyword(name))
            {
                AddDraft(
                    drafts,
                    namedSymbols,
                    file,
                    $"{classScope.Name}.{name}",
                    name,
                    "method",
                    line,
                    declarationLine,
                    isExported: false);
                return;
            }
        }

        var fieldFunctionMatch = ClassFieldFunctionPattern.Match(line);
        if (!fieldFunctionMatch.Success)
        {
            return;
        }

        var displayName = fieldFunctionMatch.Groups["name"].Value;
        AddDraft(
            drafts,
            namedSymbols,
            file,
            $"{classScope.Name}.{displayName}",
            displayName,
            "method",
            line,
            declarationLine,
            isExported: false);
    }

    private bool TryMatchAndAdd(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        string line,
        int declarationLine,
        Regex pattern,
        string kind)
    {
        return TryMatchAndAdd(drafts, namedSymbols, file, line, declarationLine, pattern, kind, out _, out _);
    }

    private bool TryMatchAndAdd(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        string line,
        int declarationLine,
        Regex pattern,
        string kind,
        out string? displayName,
        out bool isExported)
    {
        var match = pattern.Match(line);
        if (!match.Success)
        {
            displayName = null;
            isExported = false;
            return false;
        }

        displayName = match.Groups["name"].Value;
        isExported = match.Groups["export"].Success || match.Groups["default"].Success;
        AddDraft(drafts, namedSymbols, file, displayName, displayName, kind, line, declarationLine, isExported);
        return true;
    }

    private void AddDraft(
        List<SymbolDraft> drafts,
        Dictionary<string, int> namedSymbols,
        FileRecord file,
        string qualifiedName,
        string displayName,
        string kind,
        string signature,
        int declarationLine,
        bool isExported)
    {
        var key = $"{kind}:{qualifiedName}";
        if (drafts.Any(draft => draft.Key == key))
        {
            return;
        }

        drafts.Add(new SymbolDraft(
            key,
            file,
            qualifiedName,
            displayName,
            kind,
            signature,
            declarationLine,
            isExported));

        if (!qualifiedName.Contains('.', StringComparison.Ordinal))
        {
            namedSymbols[displayName] = drafts.Count - 1;
        }
    }

    private IReadOnlyList<SymbolRecord> FinalizeDrafts(IReadOnlyList<SymbolDraft> drafts)
    {
        var symbols = new List<SymbolRecord>(drafts.Count);
        foreach (var draft in drafts)
        {
            symbols.Add(CreateSymbolRecord(
                draft.File,
                draft.QualifiedName,
                draft.DisplayName,
                draft.Kind,
                draft.Signature,
                draft.DeclarationLine,
                draft.IsExported));
        }

        return symbols;
    }

    private static void MarkExports(
        string exportList,
        IReadOnlyDictionary<string, int> namedSymbols,
        List<SymbolDraft> drafts)
    {
        foreach (var rawEntry in exportList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var entry = rawEntry.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            var parts = entry.Split(" as ", StringSplitOptions.TrimEntries);
            var sourceName = parts[0].Trim();
            if (string.Equals(sourceName, "default", StringComparison.Ordinal))
            {
                continue;
            }

            MarkSymbolExported(namedSymbols, drafts, sourceName);
        }
    }

    private static void MarkSymbolExported(
        IReadOnlyDictionary<string, int> namedSymbols,
        List<SymbolDraft> drafts,
        string symbolName)
    {
        if (!namedSymbols.TryGetValue(symbolName, out var index))
        {
            return;
        }

        drafts[index] = drafts[index] with { IsExported = true };
    }

    private static bool IsMethodKeyword(string name)
    {
        return name is "if" or "for" or "while" or "switch" or "catch" or "return" or "new";
    }

    private SymbolRecord CreateSymbolRecord(
        FileRecord file,
        string qualifiedName,
        string displayName,
        string kind,
        string signature,
        int declarationLine,
        bool isExported)
    {
        var symbolId = _idGenerator.CreateSymbolId(
            file.RepoId,
            qualifiedName,
            file.Id,
            kind,
            signature,
            declarationLine);

        return new SymbolRecord(
            symbolId,
            file.RepoId,
            file.Id,
            file.ModuleId,
            qualifiedName,
            displayName,
            kind,
            file.Language,
            signature,
            declarationLine,
            isExported);
    }

    private static string SanitizeSource(string sourceText)
    {
        var builder = new StringBuilder(sourceText.Length);
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var stringDelimiter = '\0';
        var escapeNext = false;

        for (var index = 0; index < sourceText.Length; index++)
        {
            var current = sourceText[index];
            var next = index + 1 < sourceText.Length ? sourceText[index + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                    builder.Append(current);
                }
                else if (current == '\r')
                {
                    builder.Append(current);
                }
                else
                {
                    builder.Append(' ');
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    builder.Append("  ");
                    index++;
                    inBlockComment = false;
                }
                else if (current == '\n' || current == '\r')
                {
                    builder.Append(current);
                }
                else
                {
                    builder.Append(' ');
                }

                continue;
            }

            if (inString)
            {
                builder.Append(current);

                if (escapeNext)
                {
                    escapeNext = false;
                    continue;
                }

                if (current == '\\')
                {
                    escapeNext = true;
                    continue;
                }

                if (current == stringDelimiter)
                {
                    inString = false;
                }

                continue;
            }

            if (current == '/' && next == '/')
            {
                builder.Append("  ");
                index++;
                inLineComment = true;
                continue;
            }

            if (current == '/' && next == '*')
            {
                builder.Append("  ");
                index++;
                inBlockComment = true;
                continue;
            }

            if (current is '\'' or '"' or '`')
            {
                inString = true;
                stringDelimiter = current;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static int CountBraces(string line)
    {
        var delta = 0;
        foreach (var character in line)
        {
            if (character == '{') delta++;
            if (character == '}') delta--;
        }

        return delta;
    }

    private sealed record ClassScope(string Name, int BodyDepth);

    private sealed record SymbolDraft(
        string Key,
        FileRecord File,
        string QualifiedName,
        string DisplayName,
        string Kind,
        string Signature,
        int DeclarationLine,
        bool IsExported);
}

public sealed record SymbolExtractionResult(
    IReadOnlyList<SymbolRecord> Symbols,
    int FilesParsed,
    IReadOnlyList<string> Warnings);
