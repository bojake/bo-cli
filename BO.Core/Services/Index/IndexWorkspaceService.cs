using BO.Core.Configuration;
using BO.Core.Indexing;
using BO.Core.Persistence;

namespace BO.Core.Services.Index;

public sealed class IndexWorkspaceService
{
    private readonly ArtifactLoader _artifactLoader;
    private readonly WorkspaceScanner _workspaceScanner;
    private readonly SourceSymbolExtractor _sourceSymbolExtractor;
    private readonly ContractExtractor _contractExtractor;
    private readonly DependencyExtractor _dependencyExtractor;
    private readonly SymbolDependencyExtractor _symbolDependencyExtractor;
    private readonly BoundaryExtractor _boundaryExtractor;
    private readonly EffectProfileDeriver _effectProfileDeriver;
    private readonly ComplexityProfileDeriver _complexityProfileDeriver;
    private readonly ResponsibilityProfileDeriver _responsibilityProfileDeriver;
    private readonly ContextBurdenDeriver _contextBurdenDeriver;
    private readonly RefactorPressureScorer _refactorPressureScorer;
    private readonly RefactorDecisionDeriver _refactorDecisionDeriver;
    private readonly SeamExtractionPlanner _seamExtractionPlanner;
    private readonly IBoGraphStore _graphStore;

    public IndexWorkspaceService(
        ArtifactLoader artifactLoader,
        WorkspaceScanner workspaceScanner,
        SourceSymbolExtractor sourceSymbolExtractor,
        ContractExtractor contractExtractor,
        DependencyExtractor dependencyExtractor,
        SymbolDependencyExtractor symbolDependencyExtractor,
        BoundaryExtractor boundaryExtractor,
        EffectProfileDeriver effectProfileDeriver,
        ComplexityProfileDeriver complexityProfileDeriver,
        ResponsibilityProfileDeriver responsibilityProfileDeriver,
        ContextBurdenDeriver contextBurdenDeriver,
        RefactorPressureScorer refactorPressureScorer,
        RefactorDecisionDeriver refactorDecisionDeriver,
        SeamExtractionPlanner seamExtractionPlanner,
        IBoGraphStore graphStore)
    {
        _artifactLoader = artifactLoader;
        _workspaceScanner = workspaceScanner;
        _sourceSymbolExtractor = sourceSymbolExtractor;
        _contractExtractor = contractExtractor;
        _dependencyExtractor = dependencyExtractor;
        _symbolDependencyExtractor = symbolDependencyExtractor;
        _boundaryExtractor = boundaryExtractor;
        _effectProfileDeriver = effectProfileDeriver;
        _complexityProfileDeriver = complexityProfileDeriver;
        _responsibilityProfileDeriver = responsibilityProfileDeriver;
        _contextBurdenDeriver = contextBurdenDeriver;
        _refactorPressureScorer = refactorPressureScorer;
        _refactorDecisionDeriver = refactorDecisionDeriver;
        _seamExtractionPlanner = seamExtractionPlanner;
        _graphStore = graphStore;
    }

    public async Task<IndexResult> IndexAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var paths = ArtifactPathResolver.Resolve(workspaceRoot);
        var rules = _artifactLoader.LoadPackageClassificationRules(paths.PackageClassificationRulesPath);
        var boConfiguration = _artifactLoader.LoadBoConfiguration(paths.RepoConfigurationPath);
        var scoringRules = _artifactLoader.LoadRefactorScoringRules(paths.ScoringConfigPath);
        var decisionRules = _artifactLoader.LoadRefactorDecisionRules(paths.RefactorDecisionRulesPath);
        var scanRules = _artifactLoader.LoadWorkspaceScanRules(paths.WorkspaceScanRulesPath);
        var semanticRules = _artifactLoader.LoadSemanticProfileRules(paths.SemanticProfileRulesPath);
        var scanResult = _workspaceScanner.Scan(paths.WorkspaceRoot, rules.Version, scanRules, boConfiguration);
        var symbolExtraction = _sourceSymbolExtractor.Extract(scanResult.Files);
        var contracts = _contractExtractor.Extract(scanResult.Files, symbolExtraction.Symbols);
        var dependencies = _dependencyExtractor.Extract(scanResult.Files);
        var symbolDependencies = _symbolDependencyExtractor.Extract(scanResult.Files, symbolExtraction.Symbols, contracts, dependencies);
        var boundaryInteractions = _boundaryExtractor.Extract(scanResult.Files, rules, boConfiguration);
        var effectProfiles = _effectProfileDeriver.Derive(scanResult.Files, boundaryInteractions, semanticRules);
        var complexityProfiles = _complexityProfileDeriver.Derive(scanResult.Files, symbolExtraction.Symbols, dependencies, effectProfiles);
        var responsibilityProfiles = _responsibilityProfileDeriver.Derive(
            scanResult.Files,
            dependencies,
            boundaryInteractions,
            effectProfiles,
            semanticRules);
        var contextBurdens = _contextBurdenDeriver.Derive(
            scanResult.Files, dependencies, complexityProfiles);
        var refactorPressureScores = _refactorPressureScorer.Score(
            complexityProfiles,
            responsibilityProfiles,
            contextBurdens,
            scoringRules);
        var refactorDecisions = _refactorDecisionDeriver.Derive(
            refactorPressureScores,
            symbolExtraction.Symbols,
            symbolDependencies,
            boundaryInteractions,
            effectProfiles,
            responsibilityProfiles,
            complexityProfiles,
            decisionRules);
        var (seamExtractionPlans, extractionPatterns) = _seamExtractionPlanner.Plan(
            refactorDecisions,
            symbolExtraction.Symbols,
            symbolDependencies,
            boundaryInteractions,
            complexityProfiles,
            scanResult.Files,
            RefactorIntent.Default);
        var result = new IndexResult(
            scanResult.Repo,
            scanResult.Files,
            symbolExtraction.Symbols,
            contracts,
            dependencies,
            symbolDependencies,
            boundaryInteractions,
            effectProfiles,
            complexityProfiles,
            responsibilityProfiles,
            contextBurdens,
            refactorPressureScores,
            refactorDecisions,
            seamExtractionPlans,
            extractionPatterns,
            symbolExtraction.FilesParsed,
            scanResult.PackageRulesVersion,
            [.. scanResult.Warnings, .. symbolExtraction.Warnings]);
        var batch = GraphRecordFactory.CreateIndexBatch(result);

        await _graphStore.EnsureSchemaAsync(GraphStoreSchemas.BoV01, cancellationToken);
        await _graphStore.ApplyWriteBatchAsync(batch, cancellationToken);

        return result;
    }

    /// <summary>
    /// Fast-path indexing for pivot analysis. Scans all files (cheap FS walk)
    /// but only AST-parses the target files for symbols, complexity, and boundaries.
    /// Skips graph store writes — the result is used directly by the CLI.
    /// </summary>
    public IndexResult IndexPivot(
        string workspaceRoot,
        Func<FileRecord, bool> targetPredicate,
        RefactorIntent? refactorIntent = null,
        SeamDomainRules? seamDomainRules = null)
    {
        var effectiveIntent = refactorIntent ?? RefactorIntent.Default;
        var paths = ArtifactPathResolver.Resolve(workspaceRoot);
        var rules = _artifactLoader.LoadPackageClassificationRules(paths.PackageClassificationRulesPath);
        var boConfiguration = _artifactLoader.LoadBoConfiguration(paths.RepoConfigurationPath);
        var scoringRules = _artifactLoader.LoadRefactorScoringRules(paths.ScoringConfigPath);
        var decisionRules = _artifactLoader.LoadRefactorDecisionRules(paths.RefactorDecisionRulesPath);
        var scanRules = _artifactLoader.LoadWorkspaceScanRules(paths.WorkspaceScanRulesPath);
        var semanticRules = _artifactLoader.LoadSemanticProfileRules(paths.SemanticProfileRulesPath);

        // Phase 1: cheap FS walk — enumerate all files
        var scanResult = _workspaceScanner.Scan(paths.WorkspaceRoot, rules.Version, scanRules, boConfiguration);

        // Phase 2: identify target files
        var targetFiles = scanResult.Files.Where(targetPredicate).ToList();
        if (targetFiles.Count == 0)
        {
            return new IndexResult(
                scanResult.Repo,
                scanResult.Files,
                [], [], [], [], [], [], [], [], [], [], [], [], [],
                0,
                scanResult.PackageRulesVersion,
                [.. scanResult.Warnings, "No files matched the pivot target."]);
        }

        // Phase 3: AST-parse ONLY target files (the slow step)
        var symbolExtraction = _sourceSymbolExtractor.Extract(targetFiles);
        var contracts = _contractExtractor.Extract(targetFiles, symbolExtraction.Symbols);
        var dependencies = _dependencyExtractor.Extract(targetFiles);
        var symbolDependencies = _symbolDependencyExtractor.Extract(targetFiles, symbolExtraction.Symbols, contracts, dependencies);
        var boundaryInteractions = _boundaryExtractor.Extract(targetFiles, rules, boConfiguration);
        var effectProfiles = _effectProfileDeriver.Derive(targetFiles, boundaryInteractions, semanticRules);
        var complexityProfiles = _complexityProfileDeriver.Derive(targetFiles, symbolExtraction.Symbols, dependencies, effectProfiles);
        var responsibilityProfiles = _responsibilityProfileDeriver.Derive(
            targetFiles,
            dependencies,
            boundaryInteractions,
            effectProfiles,
            semanticRules);
        var contextBurdens = _contextBurdenDeriver.Derive(
            targetFiles, dependencies, complexityProfiles);
        var refactorPressureScores = _refactorPressureScorer.Score(
            complexityProfiles,
            responsibilityProfiles,
            contextBurdens,
            scoringRules);
        var refactorDecisions = _refactorDecisionDeriver.Derive(
            refactorPressureScores,
            symbolExtraction.Symbols,
            symbolDependencies,
            boundaryInteractions,
            effectProfiles,
            responsibilityProfiles,
            complexityProfiles,
            decisionRules);
        var seamExtractionPlanner = seamDomainRules is null
            ? _seamExtractionPlanner
            : new SeamExtractionPlanner(seamDomainRules);
        var (seamExtractionPlans, extractionPatterns) = seamExtractionPlanner.Plan(
            refactorDecisions,
            symbolExtraction.Symbols,
            symbolDependencies,
            boundaryInteractions,
            complexityProfiles,
            targetFiles,
            effectiveIntent);

        return new IndexResult(
            scanResult.Repo,
            scanResult.Files,   // keep full file list for resolution
            symbolExtraction.Symbols,
            contracts,
            dependencies,
            symbolDependencies,
            boundaryInteractions,
            effectProfiles,
            complexityProfiles,
            responsibilityProfiles,
            contextBurdens,
            refactorPressureScores,
            refactorDecisions,
            seamExtractionPlans,
            extractionPatterns,
            symbolExtraction.FilesParsed,
            scanResult.PackageRulesVersion,
            [.. scanResult.Warnings, .. symbolExtraction.Warnings]);
    }
}
