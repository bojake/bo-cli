using TreeSitter;

namespace BO.Core.Indexing;

public sealed class ComplexityProfileDeriver
{
    // ── Branch node types ────────────────────────────────────────────────────
    // These contribute +1 to cyclomatic complexity and +1+depth to cognitive.

    // Shared across TS/JS and C#
    private static readonly HashSet<string> BranchNodeTypes = new(StringComparer.Ordinal)
    {
        // TS/JS
        "if_statement",
        "switch_statement",
        "switch_case",           // TS/JS
        "for_statement",
        "for_in_statement",      // TS/JS
        "while_statement",
        "do_statement",
        "catch_clause",
        "conditional_expression",

        // C#
        "switch_section",        // C# equivalent of switch_case
        "foreach_statement",     // C# foreach (TS uses for_in_statement)
        "case_switch_label",     // C# case label in older switch syntax
    };

    // ── Function/method node types (for parameter counting + symbol isolation) ──

    private static readonly HashSet<string> FunctionNodeTypes = new(StringComparer.Ordinal)
    {
        // TS/JS
        "function_declaration",
        "method_definition",
        "arrow_function",
        "function_expression",

        // C#
        "method_declaration",
        "constructor_declaration",
        "local_function_statement",
    };

    // ── Nesting node types (increase depth for cognitive complexity) ──────────

    private static readonly HashSet<string> NestingNodeTypes = new(StringComparer.Ordinal)
    {
        // TS/JS
        "statement_block",
        "class_body",
        "switch_body",

        // C#
        "block",                 // C# method/loop bodies
        "declaration_list",      // C# class/struct/interface body
    };

    // ── Logical operator node types (contribute to cognitive complexity) ──────

    private static readonly HashSet<string> LogicalOperatorNodeTypes = new(StringComparer.Ordinal)
    {
        "binary_expression",     // both TS and C# — checked for && || operators
    };

    public IReadOnlyList<ComplexityProfileRecord> Derive(
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyList<FileDependencyRecord> dependencies,
        IReadOnlyList<EffectProfileRecord> effectProfiles)
    {
        var fanOutByFile = dependencies
            .GroupBy(dependency => dependency.FromFileId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.ToFileId).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);

        var fanInByFile = dependencies
            .GroupBy(dependency => dependency.ToFileId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.FromFileId).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);

        var effectProfileByTarget = effectProfiles.ToDictionary(profile => profile.TargetId, StringComparer.Ordinal);
        var symbolsByFile = symbols
            .Where(s => s.Kind is "function" or "method" or "constructor")
            .GroupBy(s => s.FileId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        var profiles = new List<ComplexityProfileRecord>();

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

            int loc = sourceText
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Count(line => !string.IsNullOrWhiteSpace(line));

            int cognitiveComplexity = 0;
            int cyclomaticComplexity = 1;
            int branchCount = 0;
            int parameterCount = 0;
            int nestingDepth = 0;

            Tree? tree = null;
            try
            {
                var langInfo = LanguageRegistry.GetLanguageInfo(file);
                if (langInfo is not null)
                {
                    using var language = new Language(langInfo.LibraryName, langInfo.FunctionName);
                    using var parser = new Parser(language);
                    tree = parser.Parse(sourceText);
                    if (tree is not null)
                    {
                        AnalyzeNode(tree.RootNode, depth: 0,
                            ref cognitiveComplexity, ref cyclomaticComplexity,
                            ref branchCount, ref parameterCount, ref nestingDepth);
                    }
                }
            }
            catch
            {
                // Leave metrics at conservative defaults if parsing fails.
            }

            effectProfileByTarget.TryGetValue(file.Id, out var effectProfile);
            var sideEffectCount = effectProfile?.SideEffectClasses.Count ?? 0;

            // ── File-level profile ───────────────────────────────────────────
            profiles.Add(new ComplexityProfileRecord(
                $"complexity:{file.Id}",
                file.Id,
                "file",
                loc,
                cognitiveComplexity,
                cyclomaticComplexity,
                nestingDepth,
                parameterCount,
                branchCount,
                sideEffectCount,
                fanInByFile.GetValueOrDefault(file.Id, 0),
                fanOutByFile.GetValueOrDefault(file.Id, 0),
                effectProfile is null ? 0.75 : Math.Max(0.8, effectProfile.Confidence)));

            // ── Symbol-level profiles ────────────────────────────────────────
            if (tree is not null && symbolsByFile.TryGetValue(file.Id, out var fileSymbols))
            {
                var functionNodes = CollectFunctionNodes(tree.RootNode);

                foreach (var symbol in fileSymbols)
                {
                    var matchedNode = MatchSymbolToNode(symbol, functionNodes);
                    if (matchedNode is null)
                    {
                        continue;
                    }

                    int symCognitive = 0;
                    int symCyclomatic = 1;
                    int symBranch = 0;
                    int symParams = 0;
                    int symNesting = 0;

                    AnalyzeNode(matchedNode, depth: 0,
                        ref symCognitive, ref symCyclomatic,
                        ref symBranch, ref symParams, ref symNesting);

                    int symLoc = CountNodeLines(matchedNode, sourceText);

                    profiles.Add(new ComplexityProfileRecord(
                        $"complexity:{symbol.Id}",
                        symbol.Id,
                        "symbol",
                        symLoc,
                        symCognitive,
                        symCyclomatic,
                        symNesting,
                        symParams,
                        symBranch,
                        0, // side-effect count not available at symbol level yet
                        0, // fan-in not available at symbol level yet
                        0, // fan-out not available at symbol level yet
                        0.80));
                }
            }

            tree?.Dispose();
        }

        return profiles;
    }

    /// <summary>
    /// Collects all function/method AST nodes from the tree, recording their
    /// start line for matching against SymbolRecords.
    /// </summary>
    private static List<Node> CollectFunctionNodes(Node root)
    {
        var nodes = new List<Node>();
        CollectFunctionNodesRecursive(root, nodes);
        return nodes;
    }

    private static void CollectFunctionNodesRecursive(Node node, List<Node> results)
    {
        if (node.IsNamed && FunctionNodeTypes.Contains(node.Type))
        {
            results.Add(node);
        }

        foreach (var child in node.NamedChildren)
        {
            CollectFunctionNodesRecursive(child, results);
        }
    }

    /// <summary>
    /// Matches a SymbolRecord to the closest function AST node by declaration line.
    /// Tree-sitter uses 0-indexed rows; SymbolRecord.DeclarationLine is 1-indexed.
    /// </summary>
    private static Node? MatchSymbolToNode(SymbolRecord symbol, List<Node> functionNodes)
    {
        Node? bestMatch = null;
        int bestDistance = int.MaxValue;
        int targetLine = symbol.DeclarationLine - 1; // convert to 0-indexed

        foreach (var node in functionNodes)
        {
            int distance = Math.Abs((int)node.StartPosition.Row - targetLine);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestMatch = node;
            }
        }

        // Only accept matches within 2 lines (account for decorators/attributes)
        return bestDistance <= 2 ? bestMatch : null;
    }

    /// <summary>
    /// Counts non-blank source lines spanned by an AST node.
    /// </summary>
    private static int CountNodeLines(Node node, string sourceText)
    {
        int startLine = (int)node.StartPosition.Row;
        int endLine = (int)node.EndPosition.Row;
        var lines = NormalizeLineEndings(sourceText).Split('\n');
        int count = 0;

        for (int i = startLine; i <= endLine && i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                count++;
            }
        }

        return count;
    }

    private static string NormalizeLineEndings(string sourceText)
    {
        return sourceText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static void AnalyzeNode(
        Node node,
        int depth,
        ref int cognitiveComplexity,
        ref int cyclomaticComplexity,
        ref int branchCount,
        ref int parameterCount,
        ref int nestingDepth)
    {
        if (!node.IsNamed)
        {
            return;
        }

        // ── Branch complexity ────────────────────────────────────────────────
        if (BranchNodeTypes.Contains(node.Type))
        {
            branchCount++;
            cyclomaticComplexity++;
            cognitiveComplexity += 1 + depth;
        }

        // ── Logical operators (&& ||) add cognitive complexity ───────────────
        if (LogicalOperatorNodeTypes.Contains(node.Type))
        {
            var text = node.Text ?? "";
            if (text.Contains("&&", StringComparison.Ordinal) ||
                text.Contains("||", StringComparison.Ordinal))
            {
                cognitiveComplexity++;
            }
        }

        // ── Parameter counting ───────────────────────────────────────────────
        if (FunctionNodeTypes.Contains(node.Type))
        {
            // Both TS/JS and C# use a child named "parameters" or "parameter_list".
            var parametersNode = node.GetChildForField("parameters");
            if (parametersNode is null || parametersNode.Id == IntPtr.Zero)
            {
                parametersNode = node.NamedChildren
                    .FirstOrDefault(c => c.Type == "parameter_list");
            }

            if (parametersNode is not null && parametersNode.Id != IntPtr.Zero)
            {
                parameterCount += parametersNode.NamedChildren.Count;
            }
        }

        // ── Nesting depth tracking ───────────────────────────────────────────
        var nextDepth = depth;
        if (NestingNodeTypes.Contains(node.Type))
        {
            nextDepth = depth + 1;
            nestingDepth = Math.Max(nestingDepth, nextDepth);
        }

        // ── Recurse into children ────────────────────────────────────────────
        foreach (var child in node.NamedChildren)
        {
            AnalyzeNode(child, nextDepth,
                ref cognitiveComplexity, ref cyclomaticComplexity,
                ref branchCount, ref parameterCount, ref nestingDepth);
        }
    }
}
