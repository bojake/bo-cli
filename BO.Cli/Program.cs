using System.Text.Json;
using BO.Core;
using BO.Core.Indexing;
using BO.Core.Services.Bootstrap;
using BO.Core.Services.Index;
using BO.Cli;
using Microsoft.Extensions.DependencyInjection;

var workspaceRoot = Directory.GetCurrentDirectory();
var command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
var jsonMode = args.Any(arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase));
var fullMode = args.Any(arg => string.Equals(arg, "--full", StringComparison.OrdinalIgnoreCase));

await using var services = new ServiceCollection()
    .AddBoCore(workspaceRoot)
    .BuildServiceProvider();

return command switch
{
    "init" => RunInit(services.GetRequiredService<BootstrapService>(), workspaceRoot, jsonMode),
    "index" => await RunIndexAsync(services.GetRequiredService<IndexWorkspaceService>(), workspaceRoot, jsonMode, fullMode),
    "pivot" => RunPivot(services.GetRequiredService<IndexWorkspaceService>(), workspaceRoot, args, jsonMode),
    _ => WriteHelp()
};

static int RunInit(BootstrapService bootstrapService, string workspaceRoot, bool jsonMode)
{
    var report = bootstrapService.Initialize(workspaceRoot);

    if (jsonMode)
    {
        WriteJson(new
        {
            schema_version = "0.1.0",
            command = "init",
            generated_at = DateTimeOffset.UtcNow,
            status = "ok",
            data = new
            {
                repo_id = report.RepoId,
                root_path = report.WorkspaceRoot,
                package_rules_found = report.PackageRulesFound,
                package_rules_version = report.PackageRulesVersion
            },
            warnings = Array.Empty<string>()
        });

        return 0;
    }

    Console.WriteLine($"Initialized BO workspace metadata in {report.WorkspaceRoot}");
    return 0;
}

static async Task<int> RunIndexAsync(
    IndexWorkspaceService indexWorkspaceService,
    string workspaceRoot,
    bool jsonMode,
    bool fullMode)
{
    var result = await indexWorkspaceService.IndexAsync(workspaceRoot);

    if (jsonMode)
    {
        WriteJson(fullMode ? BuildFullIndexPayload(result) : BuildIndexSummaryPayload(result));
        return 0;
    }

    Console.WriteLine($"Indexed repo {result.Repo.Name}");
    Console.WriteLine($"Files scanned: {result.Files.Count}");
    Console.WriteLine($"Files parsed: {result.FilesParsed}");
    Console.WriteLine($"Symbols indexed: {result.Symbols.Count}");
    Console.WriteLine($"Contracts indexed: {result.Contracts.Count}");
    Console.WriteLine($"Dependencies indexed: {result.Dependencies.Count}");
    Console.WriteLine($"Symbol dependency edges indexed: {result.SymbolDependencies.Count}");
    Console.WriteLine($"Boundaries indexed: {result.BoundaryInteractions.Count}");
    Console.WriteLine($"Effect profiles indexed: {result.EffectProfiles.Count}");
    Console.WriteLine($"Complexity profiles indexed: {result.ComplexityProfiles.Count}");
    Console.WriteLine($"Responsibility profiles indexed: {result.ResponsibilityProfiles.Count}");
    Console.WriteLine($"Refactor pressure scores: {result.RefactorPressureScores.Count}");
    Console.WriteLine($"Refactor decisions: {result.RefactorDecisions.Count}");

    foreach (var warning in result.Warnings)
        Console.WriteLine($"Warning: {warning}");

    return 0;
}

static int RunPivot(
    IndexWorkspaceService indexWorkspaceService,
    string workspaceRoot,
    string[] args,
    bool jsonMode)
{
    var target = args
        .Skip(1)
        .FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));

    if (string.IsNullOrWhiteSpace(target))
    {
        return WriteError("Usage: bo pivot <file|path> [--json]", jsonMode, "pivot");
    }

    var result = indexWorkspaceService.IndexPivot(workspaceRoot, file => MatchesTarget(file, target));
    var matchedFiles = result.Files.Where(file => MatchesTarget(file, target)).ToArray();
    if (matchedFiles.Length == 0)
    {
        return WriteError($"No files matched target '{target}'.", jsonMode, "pivot");
    }

    var rpsByTarget = result.RefactorPressureScores.ToDictionary(rps => rps.TargetId, StringComparer.Ordinal);
    var decisionsByTarget = result.RefactorDecisions.ToDictionary(decision => decision.TargetId, StringComparer.Ordinal);
    var fileComplexity = result.ComplexityProfiles
        .Where(profile => profile.TargetKind == "file")
        .ToDictionary(profile => profile.TargetId, StringComparer.Ordinal);
    var symbolComplexity = result.ComplexityProfiles
        .Where(profile => profile.TargetKind == "symbol")
        .ToDictionary(profile => profile.TargetId, StringComparer.Ordinal);

    var pivots = matchedFiles.Select(file =>
    {
        rpsByTarget.TryGetValue(file.Id, out var rps);
        decisionsByTarget.TryGetValue(file.Id, out var decision);
        fileComplexity.TryGetValue(file.Id, out var complexity);

        var symbolHotspots = result.Symbols
            .Where(symbol => symbol.FileId == file.Id && symbol.Kind is "function" or "method" or "constructor")
            .Select(symbol =>
            {
                symbolComplexity.TryGetValue(symbol.Id, out var symbolProfile);
                return new
                {
                    name = symbol.QualifiedName,
                    display_name = symbol.DisplayName,
                    kind = symbol.Kind,
                    cognitive_complexity = symbolProfile?.CognitiveComplexity ?? 0,
                    cyclomatic_complexity = symbolProfile?.CyclomaticComplexity ?? 0,
                    branch_count = symbolProfile?.BranchCount ?? 0,
                    nesting_depth = symbolProfile?.NestingDepth ?? 0,
                    parameter_count = symbolProfile?.ParameterCount ?? 0,
                    loc = symbolProfile?.Loc ?? 0
                };
            })
            .OrderByDescending(symbol => symbol.cognitive_complexity)
            .ThenByDescending(symbol => symbol.branch_count)
            .ToArray();

        return new
        {
            target_file = file.NormalizedPath,
            target_id = file.Id,
            language = file.Language,
            rps = rps is null ? null : new
            {
                rps.Score,
                rps.Recommendation,
                rps.Drivers,
                rps.FiredGates,
                rps.Confidence
            },
            decision = decision is null ? null : new
            {
                decision_id = decision.Id,
                decision.Recommendation,
                decision.PivotType,
                decision.Drivers,
                decision.FiredGates,
                decision.CandidateSeams,
                decision.RpsBefore,
                decision.Confidence
            },
            file_complexity = complexity is null ? null : new
            {
                complexity.Loc,
                complexity.CognitiveComplexity,
                complexity.CyclomaticComplexity,
                complexity.NestingDepth,
                complexity.ParameterCount,
                complexity.BranchCount,
                complexity.FanIn,
                complexity.FanOut
            },
            symbol_hotspots = symbolHotspots
        };
    }).ToArray();

    if (jsonMode)
    {
        WriteJson(new
        {
            schema_version = "0.1.0",
            command = "pivot",
            generated_at = DateTimeOffset.UtcNow,
            status = "ok",
            data = pivots.Length == 1 ? (object)pivots[0] : pivots,
            warnings = result.Warnings
        });

        return 0;
    }

    foreach (var pivot in pivots)
        Console.WriteLine(JsonSerializer.Serialize(pivot, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        }));

    return 0;
}

static object BuildIndexSummaryPayload(IndexResult result) => new
{
    schema_version = "0.1.0",
    command = "index",
    generated_at = DateTimeOffset.UtcNow,
    status = "ok",
    data = new
    {
        repo_id = result.Repo.Id,
        files_scanned = result.Files.Count,
        files_parsed = result.FilesParsed,
        symbols_indexed = result.Symbols.Count,
        contracts_indexed = result.Contracts.Count,
        dependencies_indexed = result.Dependencies.Count,
        symbol_dependency_edges_indexed = result.SymbolDependencies.Count,
        boundaries_indexed = result.BoundaryInteractions.Count,
        effect_profiles_indexed = result.EffectProfiles.Count,
        complexity_profiles_indexed = result.ComplexityProfiles.Count,
        responsibility_profiles_indexed = result.ResponsibilityProfiles.Count,
        context_burdens_indexed = result.ContextBurdens.Count,
        refactor_pressure_scores_indexed = result.RefactorPressureScores.Count,
        refactor_decisions_indexed = result.RefactorDecisions.Count,
        index_version = result.Repo.SourceVersion,
        package_rules_version = result.PackageRulesVersion,
        warnings_count = result.Warnings.Count
    },
    warnings = result.Warnings
};

static object BuildFullIndexPayload(IndexResult result) => new
{
    schema_version = "0.1.0",
    command = "index",
    generated_at = DateTimeOffset.UtcNow,
    status = "ok",
    data = new
    {
        repo = result.Repo,
        modules = result.Files
            .Select(file => new { file.ModuleId, file.RepoId })
            .DistinctBy(module => module.ModuleId)
            .Select(module => new { id = module.ModuleId, qualified_name = module.ModuleId, repo_id = module.RepoId }),
        files = result.Files,
        symbols = result.Symbols,
        contracts = result.Contracts,
        file_dependencies = result.Dependencies,
        symbol_dependencies = result.SymbolDependencies,
        boundary_interactions = result.BoundaryInteractions,
        effect_profiles = result.EffectProfiles,
        complexity_profiles = result.ComplexityProfiles,
        responsibility_profiles = result.ResponsibilityProfiles,
        context_burdens = result.ContextBurdens,
        refactor_pressure_scores = result.RefactorPressureScores,
        refactor_decisions = result.RefactorDecisions,
        seam_extraction_plans = result.SeamExtractionPlans,
        extraction_patterns = result.ExtractionPatterns
    },
    warnings = result.Warnings
};

static bool MatchesTarget(FileRecord file, string target)
{
    var fileName = Path.GetFileName(file.NormalizedPath);
    if (fileName.Equals(target, StringComparison.OrdinalIgnoreCase))
        return true;
    if (file.NormalizedPath.Equals(target, StringComparison.OrdinalIgnoreCase))
        return true;
    if (file.Path.Equals(target, StringComparison.OrdinalIgnoreCase))
        return true;
    if (target.Contains('/') || target.Contains('\\'))
    {
        return file.NormalizedPath.EndsWith(target, StringComparison.OrdinalIgnoreCase) ||
               file.Path.EndsWith(target, StringComparison.OrdinalIgnoreCase);
    }

    return false;
}

static int WriteError(string message, bool jsonMode, string command)
{
    if (jsonMode)
    {
        WriteJson(new
        {
            schema_version = "0.1.0",
            command,
            generated_at = DateTimeOffset.UtcNow,
            status = "error",
            error = message,
            warnings = Array.Empty<string>()
        });
    }
    else
    {
        Console.Error.WriteLine(message);
    }

    return 1;
}

static int WriteHelp()
{
    Console.WriteLine("bo-cli");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  bo init [--json]");
    Console.WriteLine("  bo index [--json] [--full]");
    Console.WriteLine("  bo pivot <file|path> [--json]");
    return 0;
}

static void WriteJson(object payload) => CliJsonWriter.Write(payload);
