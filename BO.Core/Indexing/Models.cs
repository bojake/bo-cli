namespace BO.Core.Indexing;

public sealed record RepoRecord(
    string Id,
    string Name,
    string RootPath,
    IReadOnlyList<string> Languages,
    string SourceVersion);

public sealed record FileRecord(
    string Id,
    string RepoId,
    string Path,
    string NormalizedPath,
    string Language,
    bool IsTest,
    bool IsGenerated,
    string ModuleId);

public sealed record SymbolRecord(
    string Id,
    string RepoId,
    string FileId,
    string ModuleId,
    string QualifiedName,
    string DisplayName,
    string Kind,
    string Language,
    string Signature,
    int DeclarationLine,
    bool IsExported);

public sealed record ContractNullability(
    bool AcceptsNullableInput,
    bool ReturnsNullableOutput,
    bool HasOptionalParameters);

public sealed record ContractRecord(
    string Id,
    string SymbolId,
    IReadOnlyList<string> InputTypes,
    IReadOnlyList<string> OutputTypes,
    IReadOnlyList<string> GenericConstraints,
    IReadOnlyList<string> ThrowsOrErrorModes,
    IReadOnlyList<string> SchemaShapes,
    ContractNullability Nullability,
    string AsyncMode,
    double Confidence);

public sealed record FileDependencyRecord(
    string Id,
    string FromFileId,
    string ToFileId,
    string ImportText,
    bool IsRuntime,
    bool IsCompileTime);

public sealed record SymbolDependencyRecord(
    string Id,
    string FromSymbolId,
    string ToSymbolId,
    string RelationType,
    string Evidence,
    double Confidence);

public sealed record BoundaryInteractionRecord(
    string Id,
    string FileId,
    string BoundaryType,
    string OperationType,
    string TargetName,
    string EffectMode,
    double Confidence);

public sealed record EffectProfileRecord(
    string Id,
    string TargetId,
    string TargetKind,
    bool ReadsState,
    bool WritesState,
    bool EmitsEvents,
    bool CallsExternalService,
    bool MutatesInput,
    bool HasRetryLogic,
    bool HasTransactionLogic,
    bool HasAuthLogic,
    bool HasValidationLogic,
    bool HasCachingLogic,
    bool HasLoggingLogic,
    IReadOnlyList<string> SideEffectClasses,
    double Confidence);

public sealed record ComplexityProfileRecord(
    string Id,
    string TargetId,
    string TargetKind,
    int Loc,
    int CognitiveComplexity,
    int CyclomaticComplexity,
    int NestingDepth,
    int ParameterCount,
    int BranchCount,
    int SideEffectCount,
    int FanIn,
    int FanOut,
    double Confidence);

public sealed record ResponsibilityProfileRecord(
    string Id,
    string TargetId,
    string TargetKind,
    int BoundaryTypeCount,
    int DependencyCategoryCount,
    int CapabilityClusterCount,
    int SideEffectClassCount,
    double ResponsibilitySpreadScore,
    IReadOnlyList<string> DominantResponsibilities,
    double Confidence);

public sealed record RefactorPressureScoreRecord(
    string Id,
    string TargetId,
    string TargetKind,
    double Score,
    string Recommendation,
    IReadOnlyList<string> Drivers,
    IReadOnlyList<string> FiredGates,
    double Confidence);

public sealed record RefactorDecisionRecord(
    string Id,
    string TargetId,
    string Recommendation,
    string PivotType,
    IReadOnlyList<string> Drivers,
    IReadOnlyList<string> FiredGates,
    IReadOnlyList<string> CandidateSeams,
    double RpsBefore,
    double Confidence);

/// <summary>
/// Measures the "safe edit neighborhood" — how many files a developer must
/// understand to safely modify a target file.
/// </summary>
public sealed record ContextBurdenRecord(
    string Id,
    string TargetId,
    string TargetKind,
    int SafeEditFileCount,
    int SafeEditTokenEstimate,
    IReadOnlyList<string> NeighborhoodFileIds,
    double Confidence);

public sealed record IndexResult(
    RepoRecord Repo,
    IReadOnlyList<FileRecord> Files,
    IReadOnlyList<SymbolRecord> Symbols,
    IReadOnlyList<ContractRecord> Contracts,
    IReadOnlyList<FileDependencyRecord> Dependencies,
    IReadOnlyList<SymbolDependencyRecord> SymbolDependencies,
    IReadOnlyList<BoundaryInteractionRecord> BoundaryInteractions,
    IReadOnlyList<EffectProfileRecord> EffectProfiles,
    IReadOnlyList<ComplexityProfileRecord> ComplexityProfiles,
    IReadOnlyList<ResponsibilityProfileRecord> ResponsibilityProfiles,
    IReadOnlyList<ContextBurdenRecord> ContextBurdens,
    IReadOnlyList<RefactorPressureScoreRecord> RefactorPressureScores,
    IReadOnlyList<RefactorDecisionRecord> RefactorDecisions,
    IReadOnlyList<SeamExtractionPlanRecord> SeamExtractionPlans,
    IReadOnlyList<ExtractionPatternRecord> ExtractionPatterns,
    int FilesParsed,
    string PackageRulesVersion,
    IReadOnlyList<string> Warnings);

// ── Seam Extraction Pipeline Records ─────────────────────────────────────────

/// <summary>
/// A discovered extraction pattern (interface + dispatcher) that already exists
/// in the codebase and can be leveraged for seam extraction.
/// </summary>
public sealed record ExtractionPatternRecord(
    string Id,
    string FileId,
    string PatternType,
    string InterfaceName,
    string? DispatcherSymbolId,
    string? DispatchSiteSymbolId,
    string DispatchConstruct,
    string DiRegistrationPattern,
    IReadOnlyList<string> ExemplarSymbolIds,
    IReadOnlyList<string> SharedHelperSymbolIds,
    double Confidence);

/// <summary>
/// Classifies a method dependency within a seam as moves, stays_injected,
/// already_shared, or needs_promotion — driving the extraction plan.
/// </summary>
public sealed record DependencyClassificationRecord(
    string Id,
    string SeamName,
    string SymbolId,
    string SymbolDisplayName,
    string Classification,
    string Reason,
    int CallersInSeam,
    int CallersOutsideSeam,
    string? PromotionTarget,
    string? InjectionInterface);

/// <summary>
/// An actionable extraction plan for a single seam.
/// Contains all information needed for an AI agent (or future codegen engine)
/// to execute the extraction without manual code archaeology.
/// </summary>
public sealed record SeamExtractionPlanRecord(
    string Id,
    string TargetFileId,
    string SeamName,
    string PivotType,
    string? ExtractionPatternId,
    string ProposedClassName,
    IReadOnlyList<string> StepTypesToRoute,
    IReadOnlyList<string> MethodsToExtract,
    IReadOnlyList<DependencyClassificationRecord> Dependencies,
    IReadOnlyList<string> ServiceInjectionsNeeded,
    IReadOnlyList<string> RecordsToMove,
    int EstimatedLocReduction,
    string Risk,
    double Confidence,
    IReadOnlyList<string>? MethodSymbolIds = null);
