using System.Text.Json;
using BO.Core.Configuration;
using BO.Core.Ids;
using BO.Core.Indexing;
using BO.Core.Persistence.InMemory;
using BO.Core.Services.Index;
using Xunit.Sdk;

namespace BO.Tests;

public sealed class IndexGoldenOutputTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static IEnumerable<object[]> FixtureNames()
    {
        var fixturesRoot = Path.Combine(FixtureWorkspace.GetRepositoryRoot(), "BO.Tests", "Fixtures");
        foreach (var directory in Directory.EnumerateDirectories(fixturesRoot).OrderBy(path => path, StringComparer.Ordinal))
        {
            yield return new object[] { Path.GetFileName(directory) };
        }
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public async Task IndexFixture_MatchesGoldenOutput(string fixtureName)
    {
        using var fixture = FixtureWorkspace.Create(fixtureName);
        var store = new InMemoryGraphStore();
        var service = CreateService(store);

        var result = await service.IndexAsync(fixture.WorkspaceRoot);
        var snapshot = CreateSnapshot(fixtureName, result, store);
        var actualJson = JsonSerializer.Serialize(snapshot, JsonOptions);

        var expectedPath = Path.Combine(
            FixtureWorkspace.GetRepositoryRoot(),
            "BO.Tests",
            "Fixtures",
            fixtureName,
            "index.golden.json");

        if (!File.Exists(expectedPath))
        {
            throw new XunitException($"Golden file missing at '{expectedPath}'. Snapshot:{Environment.NewLine}{actualJson}");
        }

        var expectedJson = File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var normalizedActual = actualJson.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (!string.Equals(expectedJson, normalizedActual, StringComparison.Ordinal))
        {
            if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE") == "1")
            {
                File.WriteAllText(expectedPath, actualJson);
                return; // Updated golden file
            }
            throw new XunitException($"Golden mismatch for '{fixtureName}'. Actual snapshot:{Environment.NewLine}{actualJson}");
        }
    }

    [Fact]
    public async Task IndexFixture_WithCrLfLineEndings_MatchesGoldenOutput()
    {
        const string fixtureName = "partial-semantic";
        using var fixture = FixtureWorkspace.Create(fixtureName);

        foreach (var path in Directory.EnumerateFiles(fixture.WorkspaceRoot, "*.ts", SearchOption.AllDirectories))
        {
            var sourceText = File.ReadAllText(path);
            var normalizedText = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            var crlfText = normalizedText.Replace("\n", "\r\n", StringComparison.Ordinal);
            File.WriteAllText(path, crlfText);
        }

        var store = new InMemoryGraphStore();
        var service = CreateService(store);
        var result = await service.IndexAsync(fixture.WorkspaceRoot);
        var snapshot = CreateSnapshot(fixtureName, result, store);
        var actualJson = JsonSerializer.Serialize(snapshot, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

        var expectedPath = Path.Combine(
            FixtureWorkspace.GetRepositoryRoot(),
            "BO.Tests",
            "Fixtures",
            fixtureName,
            "index.golden.json");
        var expectedJson = File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

        Assert.Equal(expectedJson, actualJson);
    }

    private static IndexWorkspaceService CreateService(InMemoryGraphStore store)
    {
        return new IndexWorkspaceService(
            new ArtifactLoader(),
            new WorkspaceScanner(new BoIdGenerator()),
            new SourceSymbolExtractor(new BoIdGenerator()),
            new ContractExtractor(),
            new DependencyExtractor(new BoIdGenerator()),
            new SymbolDependencyExtractor(),
            new BoundaryExtractor(),
            new EffectProfileDeriver(),
            new ComplexityProfileDeriver(),
            new ResponsibilityProfileDeriver(),
            new ContextBurdenDeriver(),
            new RefactorPressureScorer(),
            new RefactorDecisionDeriver(),
            new SeamExtractionPlanner(),
            store);
    }

    private static IndexGoldenSnapshot CreateSnapshot(
        string fixtureName,
        IndexResult result,
        InMemoryGraphStore store)
    {
        var filesById = result.Files.ToDictionary(file => file.Id, StringComparer.Ordinal);
        var symbolsById = result.Symbols.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var moduleNames = result.Files
            .Select(file => file.ModuleId[(file.ModuleId.LastIndexOf(':') + 1)..])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var files = result.Files
            .OrderBy(file => file.NormalizedPath, StringComparer.Ordinal)
            .Select(file => new FileGoldenSnapshot(
                file.NormalizedPath,
                file.Language,
                file.IsTest,
                file.IsGenerated,
                file.ModuleId[(file.ModuleId.LastIndexOf(':') + 1)..]))
            .ToArray();

        var symbols = result.Symbols
            .OrderBy(symbol => filesById[symbol.FileId].NormalizedPath, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Kind, StringComparer.Ordinal)
            .Select(symbol => new SymbolGoldenSnapshot(
                filesById[symbol.FileId].NormalizedPath,
                symbol.QualifiedName,
                symbol.DisplayName,
                symbol.Kind,
                symbol.IsExported))
            .ToArray();

        return new IndexGoldenSnapshot(
            fixtureName,
            result.Files.Count,
            result.FilesParsed,
            result.Symbols.Count,
            result.Contracts.Count,
            result.Dependencies.Count,
            result.SymbolDependencies.Count,
            result.BoundaryInteractions.Count,
            result.EffectProfiles.Count,
            result.ComplexityProfiles.Count,
            result.ResponsibilityProfiles.Count,
            result.RefactorPressureScores.Count,
            result.RefactorDecisions.Count,
            result.SeamExtractionPlans.Count,
            moduleNames.Length,
            result.Warnings.OrderBy(warning => warning, StringComparer.Ordinal).ToArray(),
            files,
            symbols,
            result.Contracts
                .OrderBy(contract => contract.SymbolId, StringComparer.Ordinal)
                .Select(contract => new ContractGoldenSnapshot(
                    result.Symbols.First(symbol => symbol.Id == contract.SymbolId).QualifiedName,
                    contract.InputTypes,
                    contract.OutputTypes,
                    contract.GenericConstraints,
                    contract.ThrowsOrErrorModes,
                    contract.Nullability.AcceptsNullableInput,
                    contract.Nullability.ReturnsNullableOutput,
                    contract.Nullability.HasOptionalParameters,
                    contract.AsyncMode))
                .ToArray(),
            result.Dependencies
                .OrderBy(dependency => filesById[dependency.FromFileId].NormalizedPath, StringComparer.Ordinal)
                .ThenBy(dependency => filesById[dependency.ToFileId].NormalizedPath, StringComparer.Ordinal)
                .Select(dependency => new DependencyGoldenSnapshot(
                    filesById[dependency.FromFileId].NormalizedPath,
                    filesById[dependency.ToFileId].NormalizedPath,
                    dependency.ImportText))
                .ToArray(),
            result.SymbolDependencies
                .OrderBy(dependency => result.Symbols.First(symbol => symbol.Id == dependency.FromSymbolId).QualifiedName, StringComparer.Ordinal)
                .ThenBy(dependency => dependency.RelationType, StringComparer.Ordinal)
                .ThenBy(dependency => result.Symbols.First(symbol => symbol.Id == dependency.ToSymbolId).QualifiedName, StringComparer.Ordinal)
                .Select(dependency => new SymbolDependencyGoldenSnapshot(
                    result.Symbols.First(symbol => symbol.Id == dependency.FromSymbolId).QualifiedName,
                    result.Symbols.First(symbol => symbol.Id == dependency.ToSymbolId).QualifiedName,
                    dependency.RelationType,
                    dependency.Evidence))
                .ToArray(),
            result.BoundaryInteractions
                .OrderBy(interaction => filesById[interaction.FileId].NormalizedPath, StringComparer.Ordinal)
                .ThenBy(interaction => interaction.BoundaryType, StringComparer.Ordinal)
                .ThenBy(interaction => interaction.TargetName, StringComparer.Ordinal)
                .Select(interaction => new BoundaryGoldenSnapshot(
                    filesById[interaction.FileId].NormalizedPath,
                    interaction.BoundaryType,
                    interaction.OperationType,
                    interaction.TargetName,
                    interaction.EffectMode))
                .ToArray(),
            result.EffectProfiles
                .OrderBy(profile => filesById[profile.TargetId].NormalizedPath, StringComparer.Ordinal)
                .Select(profile => new EffectProfileGoldenSnapshot(
                    filesById[profile.TargetId].NormalizedPath,
                    profile.ReadsState,
                    profile.WritesState,
                    profile.EmitsEvents,
                    profile.CallsExternalService,
                    profile.HasAuthLogic,
                    profile.HasCachingLogic,
                    profile.HasLoggingLogic,
                    profile.SideEffectClasses))
                .ToArray(),
            result.ComplexityProfiles
                .OrderBy(profile => profile.TargetKind, StringComparer.Ordinal)
                .ThenBy(profile => profile.TargetKind == "file"
                    ? filesById[profile.TargetId].NormalizedPath
                    : (symbolsById.TryGetValue(profile.TargetId, out var sym) ? sym.QualifiedName : profile.TargetId),
                    StringComparer.Ordinal)
                .Select(profile => new ComplexityProfileGoldenSnapshot(
                    profile.TargetKind == "file"
                        ? filesById[profile.TargetId].NormalizedPath
                        : (symbolsById.TryGetValue(profile.TargetId, out var sym) ? sym.QualifiedName : profile.TargetId),
                    profile.TargetKind,
                    profile.Loc,
                    profile.CognitiveComplexity,
                    profile.CyclomaticComplexity,
                    profile.NestingDepth,
                    profile.ParameterCount,
                    profile.BranchCount,
                    profile.SideEffectCount,
                    profile.FanIn,
                    profile.FanOut))
                .ToArray(),
            result.ResponsibilityProfiles
                .OrderBy(profile => filesById[profile.TargetId].NormalizedPath, StringComparer.Ordinal)
                .Select(profile => new ResponsibilityProfileGoldenSnapshot(
                    filesById[profile.TargetId].NormalizedPath,
                    profile.BoundaryTypeCount,
                    profile.DependencyCategoryCount,
                    profile.CapabilityClusterCount,
                    profile.SideEffectClassCount,
                    profile.ResponsibilitySpreadScore,
                    profile.DominantResponsibilities))
                .ToArray(),
            result.RefactorPressureScores
                .OrderBy(rps => filesById[rps.TargetId].NormalizedPath, StringComparer.Ordinal)
                .Select(rps => new RefactorPressureScoreGoldenSnapshot(
                    filesById[rps.TargetId].NormalizedPath,
                    rps.Score,
                    rps.Recommendation,
                    rps.Drivers,
                    rps.FiredGates,
                    rps.Confidence))
                .ToArray(),
            result.RefactorDecisions
                .OrderBy(d => d.TargetId, StringComparer.Ordinal)
                .Select(d => new RefactorDecisionGoldenSnapshot(
                    filesById.TryGetValue(d.TargetId, out var df) ? df.NormalizedPath : d.TargetId,
                    d.Recommendation,
                    d.PivotType,
                    d.Drivers,
                    d.FiredGates,
                    d.CandidateSeams,
                    d.RpsBefore,
                    d.Confidence))
                .ToArray(),
            result.SeamExtractionPlans
                .OrderBy(p => p.SeamName, StringComparer.Ordinal)
                .Select(p => new SeamExtractionPlanGoldenSnapshot(
                    filesById.TryGetValue(p.TargetFileId, out var pf) ? pf.NormalizedPath : p.TargetFileId,
                    p.SeamName,
                    p.ProposedClassName,
                    p.MethodsToExtract.Count,
                    p.Dependencies.Count,
                    p.EstimatedLocReduction,
                    p.Risk,
                    p.Confidence))
                .ToArray(),
            new GraphGoldenSnapshot(
                store.NodeCount,
                store.EdgeCount,
                store.Schema!.Nodes.Select(node => node.Label).OrderBy(label => label, StringComparer.Ordinal).ToArray(),
                store.Schema.Edges.Select(edge => edge.Label).OrderBy(label => label, StringComparer.Ordinal).ToArray()));
    }
}

public sealed record IndexGoldenSnapshot(
    string Fixture,
    int FilesScanned,
    int FilesParsed,
    int SymbolsIndexed,
    int ContractsIndexed,
    int DependenciesIndexed,
    int SymbolDependenciesIndexed,
    int BoundariesIndexed,
    int EffectProfilesIndexed,
    int ComplexityProfilesIndexed,
    int ResponsibilityProfilesIndexed,
    int RefactorPressureScoresIndexed,
    int RefactorDecisionsIndexed,
    int SeamExtractionPlansIndexed,
    int ModuleCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<FileGoldenSnapshot> Files,
    IReadOnlyList<SymbolGoldenSnapshot> Symbols,
    IReadOnlyList<ContractGoldenSnapshot> Contracts,
    IReadOnlyList<DependencyGoldenSnapshot> Dependencies,
    IReadOnlyList<SymbolDependencyGoldenSnapshot> SymbolDependencies,
    IReadOnlyList<BoundaryGoldenSnapshot> Boundaries,
    IReadOnlyList<EffectProfileGoldenSnapshot> EffectProfiles,
    IReadOnlyList<ComplexityProfileGoldenSnapshot> ComplexityProfiles,
    IReadOnlyList<ResponsibilityProfileGoldenSnapshot> ResponsibilityProfiles,
    IReadOnlyList<RefactorPressureScoreGoldenSnapshot> RefactorPressureScores,
    IReadOnlyList<RefactorDecisionGoldenSnapshot> RefactorDecisions,
    IReadOnlyList<SeamExtractionPlanGoldenSnapshot> SeamExtractionPlans,
    GraphGoldenSnapshot Graph);

public sealed record FileGoldenSnapshot(
    string Path,
    string Language,
    bool IsTest,
    bool IsGenerated,
    string Module);

public sealed record SymbolGoldenSnapshot(
    string File,
    string QualifiedName,
    string DisplayName,
    string Kind,
    bool IsExported);

public sealed record ContractGoldenSnapshot(
    string SymbolQualifiedName,
    IReadOnlyList<string> InputTypes,
    IReadOnlyList<string> OutputTypes,
    IReadOnlyList<string> GenericConstraints,
    IReadOnlyList<string> ThrowsOrErrorModes,
    bool AcceptsNullableInput,
    bool ReturnsNullableOutput,
    bool HasOptionalParameters,
    string AsyncMode);

public sealed record DependencyGoldenSnapshot(
    string FromFile,
    string ToFile,
    string ImportText);

public sealed record SymbolDependencyGoldenSnapshot(
    string FromSymbol,
    string ToSymbol,
    string RelationType,
    string Evidence);

public sealed record BoundaryGoldenSnapshot(
    string File,
    string BoundaryType,
    string OperationType,
    string TargetName,
    string EffectMode);

public sealed record EffectProfileGoldenSnapshot(
    string File,
    bool ReadsState,
    bool WritesState,
    bool EmitsEvents,
    bool CallsExternalService,
    bool HasAuthLogic,
    bool HasCachingLogic,
    bool HasLoggingLogic,
    IReadOnlyList<string> SideEffectClasses);

public sealed record ComplexityProfileGoldenSnapshot(
    string Target,
    string TargetKind,
    int Loc,
    int CognitiveComplexity,
    int CyclomaticComplexity,
    int NestingDepth,
    int ParameterCount,
    int BranchCount,
    int SideEffectCount,
    int FanIn,
    int FanOut);

public sealed record ResponsibilityProfileGoldenSnapshot(
    string File,
    int BoundaryTypeCount,
    int DependencyCategoryCount,
    int CapabilityClusterCount,
    int SideEffectClassCount,
    double ResponsibilitySpreadScore,
    IReadOnlyList<string> DominantResponsibilities);

public sealed record GraphGoldenSnapshot(
    int NodeCount,
    int EdgeCount,
    IReadOnlyList<string> NodeLabels,
    IReadOnlyList<string> EdgeLabels);

public sealed record RefactorPressureScoreGoldenSnapshot(
    string File,
    double Score,
    string Recommendation,
    IReadOnlyList<string> Drivers,
    IReadOnlyList<string> FiredGates,
    double Confidence);

public sealed record RefactorDecisionGoldenSnapshot(
    string File,
    string Recommendation,
    string PivotType,
    IReadOnlyList<string> Drivers,
    IReadOnlyList<string> FiredGates,
    IReadOnlyList<string> CandidateSeams,
    double RpsBefore,
    double Confidence);

public sealed record SeamExtractionPlanGoldenSnapshot(
    string File,
    string SeamName,
    string ProposedClassName,
    int MethodCount,
    int DependencyCount,
    int EstimatedLocReduction,
    string Risk,
    double Confidence);
