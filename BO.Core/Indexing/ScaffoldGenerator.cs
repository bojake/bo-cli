using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BO.Core.Indexing;

/// <summary>
/// Generates new executor class files and god-class edits from
/// <see cref="ExtractionRecipe"/>s by reading the source file and
/// extracting method bodies.
/// </summary>
public sealed class ScaffoldGenerator
{
    private readonly Dictionary<string, SourceAnalysis> _sourceAnalysisCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _sourceLineCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ArchitecturePlacementRules _architectureRules;

    public ScaffoldGenerator(ArchitecturePlacementRules? architectureRules = null)
    {
        _architectureRules = architectureRules ?? ArchitecturePlacementRules.Default;
    }

    /// <summary>
    /// Result of a scaffold generation: the new file to write and the
    /// list of line ranges to delete from the god class.
    /// </summary>
    public sealed record ScaffoldResult(
        string NewFilePath,
        string NewFileContent,
        string GodClassPath,
        IReadOnlyList<(int StartLine, int EndLine)> MethodRangesToDelete,
        IReadOnlyList<(int LineNumber, string OldVisibility, string NewVisibility)> VisibilityFixes,
        string DiRegistrationLine,
        IReadOnlyList<string> AdditionalDiRegistrationLines,
        IReadOnlyList<(string Path, string Content)> SupportFiles);

    private sealed record SourceAnalysis(
        string Path,
        string[] Lines,
        int[] LineDepths,
        string? GodClassName,
        IReadOnlyDictionary<string, ExtractedMethod> TopLevelMethods,
        IReadOnlyList<string> PublicTopLevelMethodNames,
        IReadOnlyDictionary<string, IReadOnlyList<string>> TopLevelCallGraph,
        IReadOnlyList<ExtractedNestedType> NestedTypes,
        IReadOnlyList<SharedHelperRef> PrivateHelperCandidates);

    /// <summary>
    /// Ensures the extraction interface exists in the workspace. If not, generates it
    /// using the provided recipe (should be one with step types for a rich contract).
    /// Call this ONCE before the extraction loop with the best recipe.
    /// </summary>
    public void EnsureInterfaceGenerated(ExtractionRecipe recipe, string workspaceRoot)
    {
        var interfaceName = recipe.CreateFile.InterfaceName;
        if (string.IsNullOrEmpty(interfaceName)) return;

        var plannedInterface = recipe.InterfaceFile;
        if (!string.IsNullOrEmpty(plannedInterface?.ExistingPath))
        {
            return;
        }

        // Read god class source for signature extraction
        var godClassPath = Path.Combine(workspaceRoot, recipe.SourceFile);
        if (!File.Exists(godClassPath))
        {
            // Try finding the biggest source file referenced by any recipe
            godClassPath = Directory.EnumerateFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(".bo") && !f.Contains("bin") && !f.Contains("obj"))
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault() ?? "";
        }

        var sourceLines = GetSourceLines(godClassPath) ?? Array.Empty<string>();

        var interfaceRelativePath = plannedInterface?.Path
            ?? $"{interfaceName}.cs";
        var interfacePath = Path.Combine(workspaceRoot, interfaceRelativePath);
        if (File.Exists(interfacePath))
        {
            return;
        }

        var interfaceNs = plannedInterface?.Namespace ?? recipe.CreateFile.Namespace;

        var content = GenerateInterfaceFile(
            interfaceNs,
            interfaceName,
            sourceLines,
            recipe,
            _architectureRules.InterfacePlacement);

        var dir = Path.GetDirectoryName(interfacePath);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(interfacePath, content);
        Console.Error.WriteLine($"    ✓ Generated: {Path.GetRelativePath(workspaceRoot, interfacePath)}");
    }

    /// <summary>
    /// Scans generated interface content for type references that live in a disallowed
    /// layer and promotes them to the configured abstraction layer if dependencies are safe.
    /// </summary>
    private static void PromoteDependentTypes(
        string interfaceContent,
        string targetNs,
        string abstractionsDir,
        string workspaceRoot,
        InterfacePlacementRules rules)
    {
        // Extract all type names from method signatures
        var typePattern = new Regex(@"\b([A-Z][A-Za-z0-9]+(?:Definition|Context|Options|Request|Response|Result|Config|Settings|Info|Dto|Model|Record))\b");
        var referencedTypes = typePattern.Matches(interfaceContent)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var typeName in referencedTypes)
        {
            // Check if type already exists in an allowed contract layer.
            var existingInApp = Directory.EnumerateFiles(workspaceRoot, $"{typeName}.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("bin") && !f.Contains("obj") && !f.Contains(".bo"))
                .Where(f => IsAllowedContractPath(f, rules))
                .Any();

            if (existingInApp) continue;

            // Find the type in a disallowed implementation layer.
            var infraFile = Directory.EnumerateFiles(workspaceRoot, $"{typeName}.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("bin") && !f.Contains("obj") && !f.Contains(".bo"))
                .FirstOrDefault(f => IsDisallowedContractPath(f, rules));

            if (infraFile is null) continue;

            // Read the file and check if it's safe to move
            var fileContent = File.ReadAllText(infraFile);
            var fileLines = File.ReadAllLines(infraFile);

            // Check usings: all must be BCL, configured allowed layers, or framework infrastructure.
            var usings = fileLines
                .Where(l => l.TrimStart().StartsWith("using ", StringComparison.Ordinal) &&
                            l.TrimEnd().EndsWith(";", StringComparison.Ordinal) &&
                            !l.Contains('(') && !l.Contains('=') &&
                            !l.TrimStart().StartsWith("using var", StringComparison.Ordinal))
                .Select(l => l.Trim())
                .ToList();

            var hasUnsafeDeps = usings.Any(u =>
                IsDisallowedContractNamespace(ExtractUsingNamespace(u), rules) &&
                !u.Contains("Microsoft.Extensions", StringComparison.OrdinalIgnoreCase));

            if (hasUnsafeDeps)
            {
                Console.Error.WriteLine($"    ⚠ Cannot auto-promote {typeName} — has disallowed layer dependencies");
                continue;
            }

            // Safe to move: rewrite namespace and copy to abstractions dir
            var oldNs = fileLines
                .FirstOrDefault(l => l.TrimStart().StartsWith("namespace ", StringComparison.Ordinal))
                ?.Trim().TrimEnd(';').Replace("namespace ", "") ?? "";

            var newContent = fileContent.Replace(
                $"namespace {oldNs}",
                $"namespace {targetNs}",
                StringComparison.Ordinal);

            var newPath = Path.Combine(abstractionsDir, $"{typeName}.cs");
            File.WriteAllText(newPath, newContent);

            // Delete the old file
            File.Delete(infraFile);

            // Update all references in the workspace: add using if needed
            UpdateNamespaceReferences(workspaceRoot, oldNs, targetNs, typeName);

            Console.Error.WriteLine($"    ✓ Promoted: {typeName} → {Path.GetRelativePath(workspaceRoot, newPath)}");
        }
    }

    /// <summary>
    /// Updates files that referenced the old namespace to use the new one.
    /// </summary>
    private static void UpdateNamespaceReferences(
        string workspaceRoot, string oldNs, string newNs, string typeName)
    {
        var csFiles = Directory.EnumerateFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("bin") && !f.Contains("obj") && !f.Contains(".bo"));

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);

            // Skip files that don't reference the type
            if (!content.Contains(typeName, StringComparison.Ordinal)) continue;

            var hasOldUsing = content.Contains($"using {oldNs};", StringComparison.Ordinal);
            var hasNewUsing = content.Contains($"using {newNs};", StringComparison.Ordinal);

            // If file uses the old namespace and doesn't have the new one, add it
            if (hasOldUsing && !hasNewUsing)
            {
                content = content.Replace(
                    $"using {oldNs};",
                    $"using {oldNs};\nusing {newNs};",
                    StringComparison.Ordinal);
                File.WriteAllText(file, content);
            }
            else if (!hasOldUsing && !hasNewUsing)
            {
                // File references the type but has neither using — it's probably in the same namespace
                // Check if it's in the old namespace
                if (content.Contains($"namespace {oldNs}", StringComparison.Ordinal))
                {
                    // Same namespace, now different — add the new using
                    var insertPoint = content.LastIndexOf("using ", StringComparison.Ordinal);
                    if (insertPoint >= 0)
                    {
                        var lineEnd = content.IndexOf('\n', insertPoint);
                        if (lineEnd > 0)
                        {
                            content = content.Insert(lineEnd + 1, $"using {newNs};\n");
                            File.WriteAllText(file, content);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Generates a scaffold from a recipe and the workspace root.
    /// </summary>
    public ScaffoldResult? Generate(ExtractionRecipe recipe, string workspaceRoot)
    {
        var godClassPath = Path.Combine(workspaceRoot, recipe.SourceFile);
        var sourceLines = GetSourceLines(godClassPath);
        if (sourceLines is null)
        {
            return null;
        }

        var sourceAnalysis = GetSourceAnalysis(godClassPath);
        if (sourceAnalysis is null)
        {
            return null;
        }
        var lineDepths = sourceAnalysis.LineDepths;
        var methodMetadata = recipe.CreateFile.MethodsToCopy
            .Concat(recipe.CreateFile.HelpersThatMove)
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        var preserveUtilityMethodNames = recipe.CreateFile.MethodsToCopy
            .Where(m => m.StepType is null)
            .Select(m => m.Name)
            .Concat(recipe.CreateFile.HelpersThatMove.Select(h => h.Name))
            .ToHashSet(StringComparer.Ordinal);
        var allMethodNames = recipe.CreateFile.MethodsToCopy
            .Select(m => m.Name)
            .Concat(recipe.CreateFile.HelpersThatMove.Select(h => h.Name))
            .ToHashSet(StringComparer.Ordinal);
        var publicClosureProtectedMethodNames = DetermineProtectedMethodNames(
            sourceAnalysis.TopLevelCallGraph,
            sourceAnalysis.PublicTopLevelMethodNames,
            allMethodNames);
        var preservedMethodNames = new HashSet<string>(publicClosureProtectedMethodNames, StringComparer.Ordinal);
        preservedMethodNames.UnionWith(preserveUtilityMethodNames);
        var protectedRanges = GetProtectedMethodRanges(sourceAnalysis.TopLevelMethods, preservedMethodNames);

        // ── Extract method bodies ────────────────────────────────────────────
        var extractedMethods = new List<ExtractedMethod>();
        var rangesToDelete = new List<(int StartLine, int EndLine)>();

        foreach (var methodName in allMethodNames)
        {
            if (sourceAnalysis.TopLevelMethods.TryGetValue(methodName, out var extracted))
            {
                if (methodMetadata.TryGetValue(methodName, out var metadata))
                {
                    extracted = extracted with
                    {
                        StepType = metadata.StepType
                    };
                }

                extractedMethods.Add(extracted);
                if (!preservedMethodNames.Contains(methodName))
                {
                    rangesToDelete.Add((extracted.StartLine, extracted.EndLine));
                }
            }
        }

        var forcedMoveMethodNames = recipe.CreateFile.MethodsToCopy
            .Where(m => m.StepType is not null)
            .Select(m => m.Name)
            .Concat(recipe.CreateFile.HelpersThatMove.Select(h => h.Name))
            .ToHashSet(StringComparer.Ordinal);
        var localOnlyMethodNames = publicClosureProtectedMethodNames
            .Where(name => !forcedMoveMethodNames.Contains(name))
            .ToHashSet(StringComparer.Ordinal);
        var emittedMethods = extractedMethods
            .Where(method => !localOnlyMethodNames.Contains(method.Name))
            .ToList();

        // ── Extract nested types referenced by extracted methods ─────────
        var extractedBodies = string.Join("\n", emittedMethods.Select(m => m.Body));
        // Don't delete nested types from god class — other methods may still
        // reference them. Instead, we'll promote their visibility to `internal`
        // so the new executor can access them.
        var visibilityFixes = new List<(int LineNumber, string OldVisibility, string NewVisibility)>();
        foreach (var preservedMethod in extractedMethods.Where(m => localOnlyMethodNames.Contains(m.Name)))
        {
            visibilityFixes.Add((preservedMethod.StartLine, "private ", "internal "));
        }

        var alreadyPromotedTypes = new HashSet<string>(StringComparer.Ordinal);
        var nestedTypes = new List<ExtractedNestedType>();
        var pendingBodies = extractedBodies;

        while (!string.IsNullOrWhiteSpace(pendingBodies))
        {
            var discoveredTypes = FilterReferencedNestedTypes(sourceAnalysis.NestedTypes, pendingBodies)
                .Where(tt => alreadyPromotedTypes.Add(tt.Name))
                .ToList();

            if (discoveredTypes.Count == 0)
            {
                break;
            }

            nestedTypes.AddRange(discoveredTypes);
            foreach (var discoveredType in discoveredTypes)
            {
                visibilityFixes.Add((discoveredType.StartLine, "private ", "internal "));
            }

            pendingBodies = string.Join("\n", discoveredTypes.Select(tt => tt.Body));
        }

        // ── Detect shared helpers referenced by extracted methods ────────
        var extractedNameSet = emittedMethods
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);
        var sharedHelpers = DetectSharedHelperReferences(sourceAnalysis.PrivateHelperCandidates, extractedBodies, extractedNameSet);
        var needsGodClassDI = false;
        foreach (var helper in sharedHelpers)
        {
            if (helper.IsStatic)
            {
                // Already static — just promote visibility
                visibilityFixes.Add((helper.LineNumber, "private ", "internal "));
            }
            else
            {
                // Instance method — just promote to internal (not static,
                // since it may access instance fields). The new class will
                // get the god class injected to call these.
                visibilityFixes.Add((helper.LineNumber, "private ", "internal "));
                needsGodClassDI = true;
            }
        }

        // ── Promote nested types referenced by promoted helpers ──────────
        // When a private method is promoted to internal, any private nested
        // types it uses in its signature must also be promoted.
        var promotedHelperNames = sharedHelpers.Select(h => h.Name).ToHashSet();
        var helperSignatures = new StringBuilder();
        foreach (var helper in sharedHelpers)
        {
            var idx = helper.LineNumber - 1;
            if (idx >= 0 && idx < sourceLines.Length)
            {
                helperSignatures.AppendLine(sourceLines[idx]);
            }
        }
        var transitiveTypes = FilterReferencedNestedTypes(sourceAnalysis.NestedTypes, helperSignatures.ToString());
        foreach (var tt in transitiveTypes)
        {
            if (!alreadyPromotedTypes.Contains(tt.Name))
            {
                visibilityFixes.Add((tt.StartLine, "private ", "internal "));
                alreadyPromotedTypes.Add(tt.Name);
            }
        }

        // ── Promote nested types referenced by preserved local-only methods ───
        var pendingPreservedBodies = string.Join(
            "\n",
            extractedMethods
                .Where(method => localOnlyMethodNames.Contains(method.Name))
                .Select(method => method.Body));

        while (!string.IsNullOrWhiteSpace(pendingPreservedBodies))
        {
            var preservedTypes = FilterReferencedNestedTypes(sourceAnalysis.NestedTypes, pendingPreservedBodies)
                .Where(tt => alreadyPromotedTypes.Add(tt.Name))
                .ToList();

            if (preservedTypes.Count == 0)
            {
                break;
            }

            foreach (var preservedType in preservedTypes)
            {
                visibilityFixes.Add((preservedType.StartLine, "private ", "internal "));
            }

            pendingPreservedBodies = string.Join("\n", preservedTypes.Select(tt => tt.Body));
        }

        // ── Detect and remove dispatch case statements ─────────────────
        // The god class switch/case dispatches to extracted methods.
        // Remove those case statements so the step dispatcher handles them.
        var stepTypes = recipe.CreateFile.SupportedStepTypes;
        for (var i = 0; i < sourceLines.Length; i++)
        {
            var trimmed = sourceLines[i].TrimStart();
            foreach (var stepType in stepTypes)
            {
                if (trimmed.StartsWith($"case WorkflowStepType.{stepType}:", StringComparison.Ordinal))
                {
                    // A case block is typically: case line + return/body line
                    var caseEnd = i + 1;
                    // Walk forward to find all lines in this case block
                    while (caseEnd < sourceLines.Length - 1)
                    {
                        var nextTrimmed = sourceLines[caseEnd + 1].TrimStart();
                        if (nextTrimmed.StartsWith("case ", StringComparison.Ordinal) ||
                            nextTrimmed.StartsWith("default:", StringComparison.Ordinal) ||
                            nextTrimmed.StartsWith("}", StringComparison.Ordinal))
                        {
                            break;
                        }
                        caseEnd++;
                    }
                    rangesToDelete.Add((i + 1, caseEnd + 1)); // 1-indexed
                    break;
                }
            }
        }

        rangesToDelete = SubtractProtectedRanges(rangesToDelete, protectedRanges);

        // Sort ranges in reverse for safe deletion
        rangesToDelete.Sort((a, b) => b.StartLine.CompareTo(a.StartLine));

        // ── Detect using statements from source ─────────────────────────────
        var usings = DetectRequiredUsings(sourceLines, extractedMethods);

        // ── Determine god class name for qualifying shared helper calls ──────
        var godClassName = sourceAnalysis.GodClassName;

        // ── Generate new class ───────────────────────────────────────────────
        var newFileContent = GenerateClassFile(recipe, emittedMethods, usings, nestedTypes, sharedHelpers, godClassName, needsGodClassDI);
        var newFilePath = Path.Combine(workspaceRoot, recipe.CreateFile.Path);

        // ── Validate interface exists, generate if missing ───────────────────
        var supportFiles = new List<(string Path, string Content)>();
        var interfaceName = recipe.CreateFile.InterfaceName;
        if (!string.IsNullOrEmpty(interfaceName))
        {
            var plannedInterfacePath = Path.Combine(
                workspaceRoot,
                recipe.InterfaceFile?.Path ?? $"{interfaceName}.cs");
            var interfaceExists = File.Exists(plannedInterfacePath) ||
                                  (!string.IsNullOrWhiteSpace(recipe.InterfaceFile?.ExistingPath) &&
                                   File.Exists(Path.Combine(workspaceRoot, recipe.InterfaceFile.ExistingPath)));

            if (!interfaceExists)
            {
                var interfacePath = plannedInterfacePath;
                var interfaceNs = recipe.InterfaceFile?.Namespace ?? recipe.CreateFile.Namespace;
                var interfaceContent = GenerateInterfaceFile(
                    interfaceNs,
                    interfaceName,
                    sourceLines,
                    recipe,
                    _architectureRules.InterfacePlacement);
                supportFiles.Add((interfacePath, interfaceContent));
            }
        }

        return new ScaffoldResult(
            newFilePath,
            newFileContent,
            godClassPath,
            rangesToDelete,
            visibilityFixes,
            recipe.RegisterDi.RegistrationLine,
            recipe.RegisterDi.AdditionalRegistrationLines,
            supportFiles);
    }

    private SourceAnalysis? GetSourceAnalysis(string godClassPath)
    {
        var lines = GetSourceLines(godClassPath);
        if (lines is null)
        {
            return null;
        }

        if (_sourceAnalysisCache.TryGetValue(godClassPath, out var cached))
        {
            return cached;
        }

        var lineDepths = ComputeBraceDepthsBeforeEachLine(lines);
        var topLevelMethods = BuildTopLevelMethodMap(lines, lineDepths);
        var analysis = new SourceAnalysis(
            godClassPath,
            lines,
            lineDepths,
            DetectGodClassName(lines),
            topLevelMethods,
            DetectPublicTopLevelMethodNames(lines, lineDepths),
            BuildTopLevelCallGraph(topLevelMethods),
            DetectAllNestedTypes(lines),
            DetectPrivateHelperCandidates(topLevelMethods));
        _sourceAnalysisCache[godClassPath] = analysis;
        return analysis;
    }

    private string[]? GetSourceLines(string godClassPath)
    {
        if (!File.Exists(godClassPath))
        {
            return null;
        }

        if (_sourceLineCache.TryGetValue(godClassPath, out var cached))
        {
            return cached;
        }

        var lines = File.ReadAllLines(godClassPath);
        _sourceLineCache[godClassPath] = lines;
        return lines;
    }

    // ── Interface Generation ─────────────────────────────────────────────────

    /// <summary>
    /// Generates a minimal step executor interface when one doesn't exist
    /// in the target codebase. Scans the god class for actual method signatures
    /// and using directives to generate a matching contract.
    /// </summary>
    private static string GenerateInterfaceFile(
        string ns,
        string interfaceName,
        string[] godClassLines,
        ExtractionRecipe firstRecipeWithStepTypes,
        InterfacePlacementRules rules)
    {
        var sb = new StringBuilder();

        // Collect only using directives relevant to the interface's project.
        // For configured abstraction-layer interfaces, keep same-root allowed-layer namespaces.
        var rootNs = ns.Split('.').FirstOrDefault() ?? "";
        var isAbstractionLayer = IsAbstractionLayerNamespace(ns, rules);
        var relevantUsings = godClassLines
            .Where(l =>
            {
                var trimmed = l.TrimStart();
                if (!trimmed.StartsWith("using ", StringComparison.Ordinal) ||
                    !trimmed.EndsWith(";", StringComparison.Ordinal) ||
                    trimmed.Contains('(') || trimmed.Contains('=') ||
                    trimmed.StartsWith("using var", StringComparison.Ordinal))
                    return false;

                if (isAbstractionLayer)
                {
                    return IsAllowedContractUsing(trimmed, rootNs, rules);
                }

                return trimmed.Contains(rootNs, StringComparison.OrdinalIgnoreCase);
            })
            .Select(l => l.Trim())
            .Where(l => !l.Contains(ns + ";", StringComparison.Ordinal)) // don't import own namespace
            .Distinct()
            .OrderBy(l => l)
            .ToList();

        foreach (var u in relevantUsings)
        {
            sb.AppendLine(u);
        }

        if (relevantUsings.Count > 0)
        {
            sb.AppendLine();
        }

        sb.AppendLine("namespace " + ns + ";");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Contract for step executor classes extracted from the god class.");
        sb.AppendLine("/// Each implementation handles a specific set of workflow step types.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public interface {interfaceName}");
        sb.AppendLine("{");

        // If we have step types, generate the dispatcher contract
        if (firstRecipeWithStepTypes.CreateFile.SupportedStepTypes.Count > 0)
        {
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// The step types this executor can handle.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    IReadOnlyCollection<WorkflowStepType> SupportedStepTypes { get; }");
            sb.AppendLine();

            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Execute a workflow step.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    Task<string> ExecuteAsync(");
            sb.AppendLine("        WorkflowStepDefinition step,");
            sb.AppendLine("        WorkflowExecutionContext context,");
            sb.AppendLine("        CancellationToken cancellationToken);");
        }
        else
        {
            // Find the primary execute method signature from the god class
            var executePattern = firstRecipeWithStepTypes.CreateFile.MethodsToCopy
                .FirstOrDefault(m => m.Name.StartsWith("Execute", StringComparison.Ordinal));

            if (executePattern is not null)
            {
                var sig = FindMethodSignature(godClassLines, executePattern.Name);
                if (sig is not null)
                {
                    sb.AppendLine("    /// <summary>");
                    sb.AppendLine("    /// Execute a workflow step.");
                    sb.AppendLine("    /// </summary>");
                    sb.AppendLine($"    {sig.TrimEnd().TrimEnd(';')};");
                }
            }
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Extracts a method signature from source lines, stripping the body.
    /// </summary>
    private static string? FindMethodSignature(string[] lines, string methodName)
    {
        var lineDepths = ComputeBraceDepthsBeforeEachLine(lines);
        for (int i = 0; i < lines.Length; i++)
        {
            if (!IsTopLevelMethodDeclaration(lineDepths, i))
            {
                continue;
            }

            var trimmed = lines[i].TrimStart();
            if (trimmed.Contains(methodName, StringComparison.Ordinal) &&
                (trimmed.Contains("async ", StringComparison.Ordinal) || trimmed.Contains("Task", StringComparison.Ordinal)))
            {
                // Build multi-line signature
                var sigLines = new List<string>();
                for (int j = i; j < lines.Length; j++)
                {
                    sigLines.Add(lines[j]);
                    if (lines[j].Contains(')', StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                var fullSig = string.Join(" ", sigLines.Select(l => l.Trim()));

                // Strip access modifiers + async, keep return type + name + params
                fullSig = fullSig
                    .Replace("private ", "", StringComparison.Ordinal)
                    .Replace("protected ", "", StringComparison.Ordinal)
                    .Replace("internal ", "", StringComparison.Ordinal)
                    .Replace("public ", "", StringComparison.Ordinal)
                    .Replace("async ", "", StringComparison.Ordinal)
                    .Trim();

                // Truncate at '{'
                var braceIdx = fullSig.IndexOf('{');
                if (braceIdx > 0) fullSig = fullSig[..braceIdx].Trim();

                return fullSig.TrimEnd().TrimEnd(';');
            }
        }

        return null;
    }

    /// <summary>
    /// Locates the abstractions/interfaces directory in the workspace.
    /// Searches for common clean architecture conventions.
    /// </summary>
    private static string? FindAbstractionsDirectory(string workspaceRoot)
    {
        return FindAbstractionsDirectory(workspaceRoot, ArchitecturePlacementRules.Default.InterfacePlacement);
    }

    private static string? FindAbstractionsDirectory(string workspaceRoot, InterfacePlacementRules rules)
    {
        var candidates = Directory.EnumerateDirectories(workspaceRoot, "*", SearchOption.AllDirectories)
            .Where(d => !d.Contains("bin", StringComparison.OrdinalIgnoreCase) &&
                        !d.Contains("obj", StringComparison.OrdinalIgnoreCase) &&
                        !d.Contains(".bo", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var match = candidates
            .Where(d =>
            {
                var rel = Path.GetRelativePath(workspaceRoot, d);
                return rules.AbstractionLayerNames.Any(layer => rel.Contains(layer, StringComparison.OrdinalIgnoreCase)) &&
                       rules.AbstractionDirectoryNames.Any(name => rel.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            })
            .OrderByDescending(d => rules.PreferredAbstractionDirectoryNames.Any(name => d.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault();

        return match;
    }

    /// <summary>
    /// Infers a C# namespace from an absolute directory path relative to the workspace root.
    /// </summary>
    private static string InferNamespaceFromPath(string absoluteDir, string workspaceRoot)
    {
        var relative = Path.GetRelativePath(workspaceRoot, absoluteDir);
        return InferNamespaceFromPath(relative, ArchitecturePlacementRules.Default.InterfacePlacement);
    }

    private static string InferNamespaceFromPath(string relativePath, InterfacePlacementRules rules)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(p => !string.IsNullOrWhiteSpace(p) &&
                        !rules.SourceRootDirectoryNames.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return parts.Count > 0 ? string.Join(".", parts) : rules.FallbackNamespace;
    }

    private static bool IsAllowedContractUsing(
        string usingLine,
        string rootNamespace,
        InterfacePlacementRules rules)
    {
        var usingNamespace = ExtractUsingNamespace(usingLine);
        return usingNamespace.Contains(rootNamespace, StringComparison.OrdinalIgnoreCase) &&
               !IsDisallowedContractNamespace(usingNamespace, rules) &&
               IsAllowedContractNamespace(usingNamespace, rules);
    }

    private static string ExtractUsingNamespace(string usingLine) =>
        usingLine.Trim()
            .TrimEnd(';')
            .Replace("using ", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static bool IsAbstractionLayerNamespace(string ns, InterfacePlacementRules rules) =>
        rules.AbstractionLayerNames.Any(layer =>
            ns.Contains($".{layer}", StringComparison.OrdinalIgnoreCase) ||
            ns.StartsWith(layer, StringComparison.OrdinalIgnoreCase));

    private static bool IsAllowedContractPath(string path, InterfacePlacementRules rules) =>
        IsAllowedContractNamespace(path.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.'), rules);

    private static bool IsDisallowedContractPath(string path, InterfacePlacementRules rules) =>
        IsDisallowedContractNamespace(path.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.'), rules);

    private static bool IsAllowedContractNamespace(string ns, InterfacePlacementRules rules)
    {
        var layer = DetectLayer(ns, rules);
        return layer is not null &&
               rules.AllowedContractLayers.Contains(layer, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDisallowedContractNamespace(string ns, InterfacePlacementRules rules)
    {
        var layer = DetectLayer(ns, rules);
        return layer is not null &&
               rules.DisallowedContractLayers.Contains(layer, StringComparer.OrdinalIgnoreCase);
    }

    private static string? DetectLayer(string ns, InterfacePlacementRules rules)
    {
        foreach (var layer in rules.Layers)
        {
            if (layer.NamespaceMarkers.Any(marker => ns.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
                layer.NamespacePrefixes.Any(prefix => ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return layer.Name;
            }
        }

        return null;
    }

    // ── Nested Type Extraction ────────────────────────────────────────────────

    private sealed record ExtractedNestedType(
        string Name,
        string Body,
        int StartLine,
        int EndLine);

    /// <summary>
    /// Scans extracted method bodies for references to types that are defined
    /// as nested records/classes inside the god class. Extracts those type
    /// definitions so they can be moved into the new executor class.
    /// </summary>
    private static IReadOnlyList<ExtractedNestedType> DetectAllNestedTypes(string[] sourceLines)
    {
        var nestedTypes = new List<ExtractedNestedType>();

        // Pattern: private/internal [readonly] [abstract|sealed] record/class/struct/interface TypeName(
        var typePattern = new Regex(
            @"^\s+(?:private|internal)\s+(?:readonly\s+)?(?:(?:abstract|sealed)\s+)?(?:record(?:\s+struct)?|class|struct|interface)\s+(\w+)",
            RegexOptions.Compiled);

        for (var i = 0; i < sourceLines.Length; i++)
        {
            var match = typePattern.Match(sourceLines[i]);
            if (!match.Success) continue;

            var typeName = match.Groups[1].Value;

            // Extract the full type definition using brace counting
            // (handles both record positional params and class bodies)
            var startLine = i;

            // Check for single-line record: record Foo(string A, int B);
            var isSingleLine = sourceLines[i].TrimEnd().EndsWith(";", StringComparison.Ordinal);
            if (isSingleLine)
            {
                nestedTypes.Add(new ExtractedNestedType(
                    typeName,
                    sourceLines[i],
                    startLine + 1,
                    startLine + 1));
                continue;
            }

            // Multi-line: find closing via braces or semicolon after params
            var braceCount = 0;
            var foundBrace = false;
            var end = i;

            for (var j = i; j < sourceLines.Length; j++)
            {
                foreach (var ch in sourceLines[j])
                {
                    if (ch == '(' || ch == '{') { braceCount++; foundBrace = true; }
                    else if (ch == ')' || ch == '}') braceCount--;
                }

                if (foundBrace && braceCount == 0)
                {
                    end = j;
                    // If it ends with ';' it's a positional record
                    if (sourceLines[j].TrimEnd().EndsWith(";", StringComparison.Ordinal))
                    {
                        break;
                    }
                    // If it's a class/record with body, check for closing brace
                    if (sourceLines[j].TrimEnd().EndsWith("}", StringComparison.Ordinal))
                    {
                        break;
                    }
                }
            }

            var bodyLines = new StringBuilder();
            for (var j = startLine; j <= end; j++)
            {
                bodyLines.AppendLine(sourceLines[j]);
            }

            nestedTypes.Add(new ExtractedNestedType(
                typeName,
                bodyLines.ToString().TrimEnd(),
                startLine + 1,  // 1-indexed
                end + 1));      // 1-indexed
        }

        return nestedTypes;
    }

    private static IReadOnlyList<ExtractedNestedType> FilterReferencedNestedTypes(
        IReadOnlyList<ExtractedNestedType> nestedTypes,
        string extractedBodies)
    {
        return nestedTypes
            .Where(type => extractedBodies.Contains(type.Name, StringComparison.Ordinal))
            .ToList();
    }

    // ── Shared Helper Detection ──────────────────────────────────────────────

    private sealed record SharedHelperRef(
        string Name,
        int LineNumber,
        bool IsStatic);

    /// <summary>
    /// Scans extracted method bodies for function-call references (patterns like
    /// "MethodName(") and cross-references them against private methods in the
    /// god class. Returns methods that need visibility promotion to `internal`.
    /// </summary>
    private static IReadOnlyList<SharedHelperRef> DetectPrivateHelperCandidates(
        IReadOnlyDictionary<string, ExtractedMethod> topLevelMethods)
    {
        return topLevelMethods.Values
            .Where(method => method.Signature.Contains("private", StringComparison.Ordinal))
            .Select(method => new SharedHelperRef(method.Name, method.StartLine, method.IsStatic))
            .OrderBy(method => method.LineNumber)
            .ToList();
    }

    private static IReadOnlyList<SharedHelperRef> DetectSharedHelperReferences(
        IReadOnlyList<SharedHelperRef> privateHelperCandidates,
        string extractedBodies,
        IReadOnlyCollection<string> extractedMethodNames)
    {
        return privateHelperCandidates
            .Where(helper => !extractedMethodNames.Contains(helper.Name) &&
                             extractedBodies.Contains($"{helper.Name}(", StringComparison.Ordinal))
            .ToList();
    }

    // ── Method Extraction ────────────────────────────────────────────────────

    /// <summary>
    /// Detects the class name of the god class from source lines.
    /// </summary>
    private static string? DetectGodClassName(string[] sourceLines)
    {
        var classPattern = new Regex(
            @"^\s*public\s+(?:sealed\s+)?class\s+(\w+)",
            RegexOptions.Compiled);

        foreach (var line in sourceLines)
        {
            var match = classPattern.Match(line);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private sealed record ExtractedMethod(
        string Name,
        string Signature,
        string Body,
        int StartLine,
        int EndLine,
        bool IsStatic,
        string? StepType);

    /// <summary>
    /// Finds a method by name in source lines and extracts its complete body
    /// using brace counting.
    /// </summary>
    private static ExtractedMethod? ExtractMethodBody(string[] lines, int[] lineDepths, string methodName)
    {
        // Match patterns like:
        //   private async Task<string> MethodName(
        //   internal static string MethodName(
        //   private static async Task<bool> MethodName(
        var pattern = new Regex(
            @"(?:private|internal|public|protected)\s+(?:static\s+)?(?:async\s+)?(?:[\w<>\[\],\s]+?)\s+" +
            Regex.Escape(methodName) + @"\s*[\(<]",
            RegexOptions.Compiled);

        var topLevelMatches = new List<int>();
        var allMatches = new List<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (!pattern.IsMatch(lines[i]))
            {
                continue;
            }

            allMatches.Add(i);
            if (IsTopLevelMethodDeclaration(lineDepths, i))
            {
                topLevelMatches.Add(i);
            }
        }

        var matchLine = SelectBestMethodMatch(lines, topLevelMatches, allMatches);
        if (matchLine < 0)
        {
            return null;
        }

        // Found the method declaration. Walk backward to pick up
        // any XML doc comments or attributes.
        var signatureStart = matchLine;
        while (signatureStart > 0 &&
               (lines[signatureStart - 1].TrimStart().StartsWith("///", StringComparison.Ordinal) ||
                lines[signatureStart - 1].TrimStart().StartsWith("[", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(lines[signatureStart - 1])))
        {
            signatureStart--;
            // Don't walk past an empty line that's not part of doc comments
            if (string.IsNullOrWhiteSpace(lines[signatureStart]) &&
                signatureStart > 0 &&
                !lines[signatureStart - 1].TrimStart().StartsWith("///", StringComparison.Ordinal))
            {
                signatureStart++;
                break;
            }
        }

        var declarationTerminator = FindDeclarationTerminator(lines, matchLine);
        if (declarationTerminator.Kind == DeclarationTerminatorKind.None)
        {
            return null;
        }

        var end = declarationTerminator.LineIndex;

        if (declarationTerminator.Kind == DeclarationTerminatorKind.ExpressionBodied)
        {
            while (end < lines.Length && !lines[end].TrimEnd().EndsWith(";", StringComparison.Ordinal))
            {
                end++;
            }
        }
        else
        {
            var braceStart = declarationTerminator.LineIndex;
            var braceCount = 0;
            for (var j = braceStart; j < lines.Length; j++)
            {
                foreach (var ch in lines[j])
                {
                    if (ch == '{') braceCount++;
                    else if (ch == '}') braceCount--;
                }

                if (braceCount == 0)
                {
                    end = j;
                    break;
                }
            }
        }

        var isStatic = lines[matchLine].Contains(" static ", StringComparison.Ordinal);

        // Extract the full method text
        var bodyLines = new StringBuilder();
        for (var j = signatureStart; j <= end; j++)
        {
            bodyLines.AppendLine(lines[j]);
        }

        // Infer step type from method name
        string? stepType = null;
        if (methodName.StartsWith("Execute", StringComparison.Ordinal) &&
            methodName.EndsWith("StepAsync", StringComparison.Ordinal))
        {
            stepType = methodName["Execute".Length..^"StepAsync".Length];
        }
        else if (methodName.StartsWith("Execute", StringComparison.Ordinal) &&
                 methodName.EndsWith("Step", StringComparison.Ordinal))
        {
            stepType = methodName["Execute".Length..^"Step".Length];
        }

        return new ExtractedMethod(
            methodName,
            lines[matchLine].Trim(),
            bodyLines.ToString().TrimEnd(),
            signatureStart + 1, // 1-indexed
            end + 1,            // 1-indexed
            isStatic,
            stepType);
    }

    private static int SelectBestMethodMatch(string[] lines, IReadOnlyList<int> topLevelMatches, IReadOnlyList<int> allMatches)
    {
        if (topLevelMatches.Count == 1)
        {
            return topLevelMatches[0];
        }

        if (topLevelMatches.Count > 1)
        {
            return topLevelMatches[0];
        }

        if (allMatches.Count == 1)
        {
            return LooksLikeClassLevelDeclaration(lines[allMatches[0]])
                ? allMatches[0]
                : -1;
        }

        if (allMatches.Count > 0)
        {
            return allMatches.FirstOrDefault(index => LooksLikeClassLevelDeclaration(lines[index]), -1);
        }

        return -1;
    }

    private static bool LooksLikeClassLevelDeclaration(string line)
    {
        var indentationWidth = 0;
        foreach (var ch in line)
        {
            if (ch == ' ')
            {
                indentationWidth++;
                continue;
            }

            if (ch == '\t')
            {
                indentationWidth += 4;
                continue;
            }

            break;
        }

        return indentationWidth <= 4;
    }

    // ── Using Detection ──────────────────────────────────────────────────────

    /// <summary>
    /// Scans the source usings and the extracted method bodies to determine
    /// which using statements are needed in the new file.
    /// </summary>
    private static IReadOnlyList<string> DetectRequiredUsings(
        string[] sourceLines,
        IReadOnlyList<ExtractedMethod> methods)
    {
        // Collect all source usings
        var sourceUsings = sourceLines
            .TakeWhile(l => l.TrimStart().StartsWith("using ", StringComparison.Ordinal) ||
                           string.IsNullOrWhiteSpace(l) ||
                           l.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(l => l.TrimStart().StartsWith("using ", StringComparison.Ordinal))
            .Select(l => l.Trim())
            .ToList();

        // For now, include all source usings. A more refined approach would
        // scan method bodies for type references, but including all is safe
        // and the compiler will warn about unused usings.
        return sourceUsings;
    }

    private enum DeclarationTerminatorKind
    {
        None,
        BlockBodied,
        ExpressionBodied
    }

    private static (DeclarationTerminatorKind Kind, int LineIndex) FindDeclarationTerminator(string[] lines, int startLine)
    {
        for (var i = startLine; i < lines.Length; i++)
        {
            var line = lines[i];
            var arrowIndex = line.IndexOf("=>", StringComparison.Ordinal);
            var braceIndex = line.IndexOf('{');

            if (arrowIndex >= 0 && (braceIndex < 0 || arrowIndex < braceIndex))
            {
                return (DeclarationTerminatorKind.ExpressionBodied, i);
            }

            if (braceIndex >= 0)
            {
                return (DeclarationTerminatorKind.BlockBodied, i);
            }

            if (line.TrimEnd().EndsWith(";", StringComparison.Ordinal))
            {
                break;
            }
        }

        return (DeclarationTerminatorKind.None, startLine);
    }

    private static int[] ComputeBraceDepthsBeforeEachLine(string[] lines)
    {
        var depths = new int[lines.Length];
        var depth = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            depths[i] = depth;
            foreach (var ch in lines[i])
            {
                if (ch == '{')
                {
                    depth++;
                }
                else if (ch == '}')
                {
                    depth--;
                }
            }
        }

        return depths;
    }

    private static bool IsTopLevelMethodDeclaration(int[] lineDepths, int lineIndex)
    {
        return lineDepths[lineIndex] == 1;
    }

    private static HashSet<string> DetermineProtectedMethodNames(
        IReadOnlyDictionary<string, IReadOnlyList<string>> topLevelCallGraph,
        IReadOnlyList<string> publicTopLevelMethodNames,
        IReadOnlySet<string> candidateMethodNames)
    {
        var protectedMethodNames = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var publicMethodName in publicTopLevelMethodNames)
        {
            queue.Enqueue(publicMethodName);
        }

        while (queue.Count > 0)
        {
            var methodName = queue.Dequeue();
            if (!visited.Add(methodName))
            {
                continue;
            }

            if (!topLevelCallGraph.TryGetValue(methodName, out var calledMethods))
            {
                continue;
            }

            foreach (var candidate in calledMethods)
            {
                queue.Enqueue(candidate);
                if (candidateMethodNames.Contains(candidate))
                {
                    protectedMethodNames.Add(candidate);
                }
            }
        }

        return protectedMethodNames;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildTopLevelCallGraph(
        IReadOnlyDictionary<string, ExtractedMethod> topLevelMethods)
    {
        var methodNames = topLevelMethods.Keys.ToArray();
        var callGraph = new Dictionary<string, IReadOnlyList<string>>(topLevelMethods.Count, StringComparer.Ordinal);

        foreach (var (methodName, method) in topLevelMethods)
        {
            var calledMethods = new List<string>();
            foreach (var candidate in methodNames)
            {
                if (string.Equals(candidate, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (CallsMethod(method.Body, candidate))
                {
                    calledMethods.Add(candidate);
                }
            }

            callGraph[methodName] = calledMethods;
        }

        return callGraph;
    }

    private static Dictionary<string, ExtractedMethod> BuildTopLevelMethodMap(string[] sourceLines, int[] lineDepths)
    {
        var methodNames = DetectAllTopLevelMethodNames(sourceLines, lineDepths);
        var methods = new Dictionary<string, ExtractedMethod>(StringComparer.Ordinal);

        foreach (var methodName in methodNames)
        {
            var extracted = ExtractMethodBody(sourceLines, lineDepths, methodName);
            if (extracted is not null)
            {
                methods[methodName] = extracted;
            }
        }

        return methods;
    }

    private static IReadOnlyList<string> DetectPublicTopLevelMethodNames(string[] sourceLines, int[] lineDepths)
    {
        var methodPattern = new Regex(
            @"^\s*public\s+(?:static\s+)?(?:async\s+)?(?:[\w<>\[\],\.\s\?]+?)\s+(\w+)\s*\(",
            RegexOptions.Compiled);
        var methodNames = new List<string>();

        for (var i = 0; i < sourceLines.Length; i++)
        {
            if (!IsTopLevelMethodDeclaration(lineDepths, i))
            {
                continue;
            }

            var match = methodPattern.Match(sourceLines[i]);
            if (match.Success)
            {
                methodNames.Add(match.Groups[1].Value);
            }
        }

        return methodNames;
    }

    private static IReadOnlyList<string> DetectAllTopLevelMethodNames(string[] sourceLines, int[] lineDepths)
    {
        var methodPattern = new Regex(
            @"^\s*(?:public|private|internal|protected)\s+(?:static\s+)?(?:async\s+)?(?:[\w<>\[\],\.\s\?]+?)\s+(\w+)\s*\(",
            RegexOptions.Compiled);
        var methodNames = new List<string>();

        for (var i = 0; i < sourceLines.Length; i++)
        {
            if (!IsTopLevelMethodDeclaration(lineDepths, i))
            {
                continue;
            }

            var match = methodPattern.Match(sourceLines[i]);
            if (match.Success)
            {
                methodNames.Add(match.Groups[1].Value);
            }
        }

        return methodNames;
    }

    private static IReadOnlyList<(int StartLine, int EndLine)> GetProtectedMethodRanges(
        IReadOnlyDictionary<string, ExtractedMethod> topLevelMethods,
        IReadOnlyCollection<string> protectedMethodNames)
    {
        return protectedMethodNames
            .Select(name => topLevelMethods.TryGetValue(name, out var method)
                ? (Found: true, Method: method)
                : (Found: false, Method: default(ExtractedMethod)!))
            .Where(result => result.Found)
            .Select(result => (result.Method.StartLine, result.Method.EndLine))
            .ToList();
    }

    private static bool CallsMethod(string bodyText, string methodName)
    {
        return Regex.IsMatch(
            bodyText,
            @"(?<![\w\.])" + Regex.Escape(methodName) + @"\s*\(",
            RegexOptions.CultureInvariant);
    }

    private static List<(int StartLine, int EndLine)> SubtractProtectedRanges(
        IReadOnlyList<(int StartLine, int EndLine)> rangesToDelete,
        IReadOnlyList<(int StartLine, int EndLine)> protectedRanges)
    {
        if (rangesToDelete.Count == 0 || protectedRanges.Count == 0)
        {
            return rangesToDelete.ToList();
        }

        var remaining = new List<(int StartLine, int EndLine)>();

        foreach (var range in rangesToDelete)
        {
            var fragments = new List<(int StartLine, int EndLine)> { range };

            foreach (var protectedRange in protectedRanges)
            {
                var nextFragments = new List<(int StartLine, int EndLine)>();
                foreach (var fragment in fragments)
                {
                    nextFragments.AddRange(SubtractRange(fragment, protectedRange));
                }

                fragments = nextFragments;
                if (fragments.Count == 0)
                {
                    break;
                }
            }

            remaining.AddRange(fragments);
        }

        return remaining;
    }

    private static IReadOnlyList<(int StartLine, int EndLine)> SubtractRange(
        (int StartLine, int EndLine) source,
        (int StartLine, int EndLine) protectedRange)
    {
        if (protectedRange.EndLine < source.StartLine || protectedRange.StartLine > source.EndLine)
        {
            return [source];
        }

        var fragments = new List<(int StartLine, int EndLine)>();

        if (protectedRange.StartLine > source.StartLine)
        {
            fragments.Add((source.StartLine, protectedRange.StartLine - 1));
        }

        if (protectedRange.EndLine < source.EndLine)
        {
            fragments.Add((protectedRange.EndLine + 1, source.EndLine));
        }

        return fragments;
    }

    // ── Class Generation ─────────────────────────────────────────────────────

    private static string GenerateClassFile(
        ExtractionRecipe recipe,
        IReadOnlyList<ExtractedMethod> methods,
        IReadOnlyList<string> usings,
        IReadOnlyList<ExtractedNestedType> nestedTypes,
        IReadOnlyList<SharedHelperRef> sharedHelpers,
        string? godClassName,
        bool needsGodClassDI)
    {
        var sb = new StringBuilder();
        var cf = recipe.CreateFile;

        // Using statements
        foreach (var u in usings)
        {
            sb.AppendLine(u);
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {cf.Namespace};");
        sb.AppendLine();

        // Class doc comment
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Handles {string.Join(", ", cf.SupportedStepTypes)} step type(s).");
        sb.AppendLine($"/// Extracted from {Path.GetFileNameWithoutExtension(recipe.SourceFile)} to reduce god-class complexity.");
        sb.AppendLine("/// </summary>");

        // Class declaration with primary constructor
        sb.Append($"public sealed class {cf.ClassName}(");
        if (cf.ConstructorParams.Count > 0)
        {
            sb.AppendLine();
            for (var i = 0; i < cf.ConstructorParams.Count; i++)
            {
                var param = cf.ConstructorParams[i];
                sb.Append($"    {param}");
                sb.AppendLine(i < cf.ConstructorParams.Count - 1 ? "," : "");
            }
            sb.Append(") ");
        }
        else
        {
            // Infer common injections from the method bodies
            var inferredParams = InferConstructorParams(methods);

            // If we need the god class for instance method calls, add it
            if (needsGodClassDI && godClassName is not null)
            {
                inferredParams.Insert(0, $"{godClassName} godClass");
            }

            if (inferredParams.Count > 0)
            {
                sb.AppendLine();
                for (var i = 0; i < inferredParams.Count; i++)
                {
                    sb.Append($"    {inferredParams[i]}");
                    sb.AppendLine(i < inferredParams.Count - 1 ? "," : "");
                }
                sb.Append(") ");
            }
            else
            {
                sb.Append(") ");
            }
        }

        sb.AppendLine($": {cf.InterfaceName}");
        sb.AppendLine("{");

        // SupportedStepTypes property
        if (cf.SupportedStepTypes.Count > 0)
        {
            sb.AppendLine("    public IReadOnlyCollection<WorkflowStepType> SupportedStepTypes { get; } =");
            sb.AppendLine("    [");
            foreach (var st in cf.SupportedStepTypes)
            {
                sb.AppendLine($"        WorkflowStepType.{st},");
            }
            sb.AppendLine("    ];");
            sb.AppendLine();
        }

        // ExecuteAsync dispatch method
        var stepMethods = methods.Where(m => m.StepType is not null).ToList();
        if (stepMethods.Count > 0)
        {
            sb.AppendLine("    public async Task<string> ExecuteAsync(");
            sb.AppendLine("        WorkflowStepDefinition step,");
            sb.AppendLine("        WorkflowExecutionContext context,");
            sb.AppendLine("        CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            sb.AppendLine("        return step.Type switch");
            sb.AppendLine("        {");
            foreach (var m in stepMethods)
            {
                sb.AppendLine($"            WorkflowStepType.{m.StepType} => await {m.Name}(step.Configuration, context, cancellationToken),");
            }
            sb.AppendLine($"            _ => throw new InvalidOperationException($\"{cf.ClassName} does not support step type '{{step.Type}}'.\")");
            sb.AppendLine("        };");
            sb.AppendLine("    }");
        }

        // Extract method bodies
        foreach (var method in methods)
        {
            sb.AppendLine();
            var bodyText = method.Body;
            var (declarationText, executableText) = SplitDeclarationAndExecutableText(bodyText);

            // Rewrite shared helper calls to qualify with god class name
            if (godClassName is not null)
            {
                foreach (var helper in sharedHelpers)
                {
                    string qualifier = helper.IsStatic
                        ? $"{godClassName}."
                        : "godClass.";

                    executableText = executableText.Replace(
                        $" {helper.Name}(",
                        $" {qualifier}{helper.Name}(",
                        StringComparison.Ordinal);
                    executableText = executableText.Replace(
                        $"({helper.Name}(",
                        $"({qualifier}{helper.Name}(",
                        StringComparison.Ordinal);
                    executableText = executableText.Replace(
                        $"await {helper.Name}(",
                        $"await {qualifier}{helper.Name}(",
                        StringComparison.Ordinal);
                    executableText = executableText.Replace(
                        $"= {helper.Name}(",
                        $"= {qualifier}{helper.Name}(",
                        StringComparison.Ordinal);
                }

            }

            bodyText = declarationText + executableText;
            if (godClassName is not null)
            {
                bodyText = RewriteNestedTypeReferences(bodyText, nestedTypes, godClassName);
            }

            var bodyLines = bodyText.Split('\n');
            foreach (var line in bodyLines)
            {
                var trimmed = line.TrimEnd('\r');
                sb.AppendLine(trimmed);
            }
        }

        sb.AppendLine();

        // NOTE: Nested types (records, classes) remain in the god class
        // with promoted visibility (internal). They are not duplicated here
        // to avoid type ambiguity errors.

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static (string DeclarationText, string ExecutableText) SplitDeclarationAndExecutableText(string bodyText)
    {
        var arrowIndex = bodyText.IndexOf("=>", StringComparison.Ordinal);
        var braceIndex = bodyText.IndexOf('{');

        if (arrowIndex >= 0 && (braceIndex < 0 || arrowIndex < braceIndex))
        {
            var splitIndex = arrowIndex + 2;
            return (bodyText[..splitIndex], bodyText[splitIndex..]);
        }

        if (braceIndex >= 0)
        {
            var splitIndex = braceIndex + 1;
            return (bodyText[..splitIndex], bodyText[splitIndex..]);
        }

        return (bodyText, string.Empty);
    }

    private static string RewriteNestedTypeReferences(
        string text,
        IReadOnlyList<ExtractedNestedType> nestedTypes,
        string godClassName)
    {
        foreach (var nt in nestedTypes)
        {
            text = Regex.Replace(
                text,
                @"(?<![\w\.])" + Regex.Escape(nt.Name) + @"(?!\w)",
                $"{godClassName}.{nt.Name}");
        }

        return text;
    }

    /// <summary>
    /// Scans method bodies for common service references (field access patterns
    /// like "dbContextFactory.", "logger.", etc.) and infers constructor params.
    /// </summary>
    private static List<string> InferConstructorParams(IReadOnlyList<ExtractedMethod> methods)
    {
        var allBodies = string.Join("\n", methods.Select(m => m.Body));
        var constructorParams = new List<string>();

        // Common service patterns in the god class
        (string fieldRef, string type, string name)[] knownServices =
        [
            ("dbContextFactory.", "IDbContextFactory<AppDbContext>", "dbContextFactory"),
            ("monitoringSupport.", "IRunExecutionMonitoringSupport", "monitoringSupport"),
            ("logger.", "ILogger", "logger"),
            ("executionPolicyOptions.", "IOptions<ExecutionPolicyOptions>", "executionPolicyOptions"),
            ("storageOptions.", "IOptions<StorageOptions>", "storageOptions"),
            ("httpClientFactory.", "IHttpClientFactory", "httpClientFactory"),
            ("featureEntitlementService.", "IFeatureEntitlementService", "featureEntitlementService"),
            ("auditService.", "IAuditService", "auditService"),
            ("agentExecutionService.", "IAgentExecutionService", "agentExecutionService"),
            ("contractValidator.", "IAgentContractValidator", "contractValidator"),
            ("inputMappingResolver.", "IInputMappingResolver", "inputMappingResolver"),
            ("globalVariableService.", "IGlobalVariableService", "globalVariableService"),
            ("notificationPipelineExecutor.", "NotificationPipelineExecutor", "notificationPipelineExecutor"),
            ("aiPromptExecutionSupport.", "IAiPromptExecutionSupport", "aiPromptExecutionSupport"),
            ("resourceSecretVaultService.", "IResourceSecretVaultService", "resourceSecretVaultService"),
            ("pythonScriptRepositoryService.", "IPythonScriptRepositoryService", "pythonScriptRepositoryService"),
            ("outboundRateLimiter.", "IOutboundRateLimiter", "outboundRateLimiter"),
            ("cipherPolicyOptions.", "IOptions<CipherPolicyOptions>", "cipherPolicyOptions"),
            ("workflowParser.", "IWorkflowDefinitionParser", "workflowParser"),
            ("runRequestService.", "IRunRequestService", "runRequestService"),
        ];

        foreach (var (fieldRef, type, name) in knownServices)
        {
            if (allBodies.Contains(fieldRef, StringComparison.Ordinal))
            {
                // Special case for ILogger — use the new class name
                if (type == "ILogger")
                {
                    continue;
                }

                constructorParams.Add($"{type} {name}");
            }
        }

        // Always add a logger as the last param
        if (allBodies.Contains("logger.", StringComparison.Ordinal))
        {
            // Placeholder — the caller will replace {ClassName}
            constructorParams.Add("ILogger<{ClassName}> logger");
        }

        return constructorParams;
    }

    /// <summary>
    /// Applies the god-class edits: removes extracted methods from the source file.
    /// Returns the modified source content.
    /// </summary>
    public static string ApplyGodClassEdits(
        string[] sourceLines,
        IReadOnlyList<(int StartLine, int EndLine)> rangesToDelete,
        IReadOnlyList<(int LineNumber, string OldVisibility, string NewVisibility)>? visibilityFixes = null)
    {
        var result = new List<string>(sourceLines);

        // Apply visibility promotions first (before deletions shift line numbers)
        if (visibilityFixes is not null)
        {
            foreach (var (lineNum, oldVis, newVis) in visibilityFixes)
            {
                var idx = lineNum - 1; // 1-indexed to 0-indexed
                if (idx >= 0 && idx < result.Count)
                {
                    // Replace the first occurrence of the old visibility keyword
                    // Handles: private static, private sealed record, private async, etc.
                    var line = result[idx];
                    var visIdx = line.IndexOf(oldVis, StringComparison.Ordinal);
                    if (visIdx >= 0)
                    {
                        result[idx] = string.Concat(
                            line.AsSpan(0, visIdx),
                            newVis,
                            line.AsSpan(visIdx + oldVis.Length));
                    }
                }
            }
        }

        // Sort ranges in reverse to delete from bottom up
        var sorted = rangesToDelete.OrderByDescending(r => r.StartLine).ToList();

        foreach (var (start, end) in sorted)
        {
            // Convert from 1-indexed to 0-indexed
            var s = start - 1;
            var e = end - 1;
            if (s >= 0 && e < result.Count)
            {
                // Also remove trailing blank line if present
                var count = e - s + 1;
                if (e + 1 < result.Count && string.IsNullOrWhiteSpace(result[e + 1]))
                {
                    count++;
                }

                result.RemoveRange(s, Math.Min(count, result.Count - s));
            }
        }

        return string.Join(Environment.NewLine, result);
    }
}
