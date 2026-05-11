using System.Text.Json.Serialization;

namespace BO.Core.Indexing;

/// <summary>
/// A structured extraction recipe that contains all the information an AI agent
/// (or template engine) needs to execute a single seam extraction from a god class.
/// This is the bridge between BO's analysis and the actual code-editing phase.
/// </summary>
public sealed record ExtractionRecipe
{
    /// <summary>Requested refactor intent that produced this recipe.</summary>
    public RefactorIntent RefactorIntent { get; init; } = RefactorIntent.Default;

    /// <summary>Transformation families this recipe is allowed to use.</summary>
    public IReadOnlyList<RefactorTransformationFamily> AllowedTransformationFamilies { get; init; } =
        [RefactorTransformationFamily.StructuralExtraction];

    /// <summary>Seam domain name (e.g., "sftp", "sql", "pdf").</summary>
    public required string SeamName { get; init; }

    /// <summary>Source file containing the god class.</summary>
    public required string SourceFile { get; init; }

    /// <summary>Pivot type (e.g., "extract_boundary_adapter", "extract_policy").</summary>
    public required string PivotType { get; init; }

    /// <summary>Risk assessment: low, medium, high.</summary>
    public required string Risk { get; init; }

    /// <summary>Confidence score (0.0 – 1.0).</summary>
    public required double Confidence { get; init; }

    // ── Create Operations ────────────────────────────────────────────────────

    /// <summary>New file to create for the extracted executor.</summary>
    public required CreateFileOperation CreateFile { get; init; }

    /// <summary>Interface file placement to reuse or generate.</summary>
    public InterfaceFileOperation? InterfaceFile { get; init; }

    // ── Modify Operations ────────────────────────────────────────────────────

    /// <summary>Modifications to the god class (remove methods, rewire dispatch).</summary>
    public required ModifyGodClassOperation ModifyGodClass { get; init; }

    /// <summary>DI registration to add.</summary>
    public required DiRegistration RegisterDi { get; init; }

    /// <summary>Methods to promote to shared helpers.</summary>
    public IReadOnlyList<PromoteMethodOperation> PromoteMethods { get; init; } = [];

    // ── Metadata ─────────────────────────────────────────────────────────────

    /// <summary>Estimated lines of code removed from the god class.</summary>
    public required int EstimatedLocReduction { get; init; }

    /// <summary>Extraction pattern used (interface_dispatcher, etc.).</summary>
    public ExtractionPatternInfo? Pattern { get; init; }

    /// <summary>Why BO reused or narrowed the contract boundary for this recipe.</summary>
    public ContractBoundaryDecisionInfo? ContractBoundaryDecision { get; init; }

    /// <summary>Potential Level 3 generalization opportunity discovered for this recipe.</summary>
    public GeneralizationCandidateInfo? GeneralizationCandidate { get; init; }

    /// <summary>Potential Level 4 architectural promotion available for this recipe.</summary>
    public ArchitecturalPromotionCandidateInfo? ArchitecturalPromotionCandidate { get; init; }
}

public sealed record CreateFileOperation
{
    /// <summary>Relative path for the new file.</summary>
    public required string Path { get; init; }

    /// <summary>Class name for the new executor.</summary>
    public required string ClassName { get; init; }

    /// <summary>Interface the class implements.</summary>
    public required string InterfaceName { get; init; }

    /// <summary>Namespace for the new class.</summary>
    public required string Namespace { get; init; }

    /// <summary>Why this namespace/path was selected.</summary>
    public required string PlacementReason { get; init; }

    /// <summary>Step types this executor handles (for SupportedStepTypes property).</summary>
    public required IReadOnlyList<string> SupportedStepTypes { get; init; }

    /// <summary>Constructor parameters (DI services to inject).</summary>
    public required IReadOnlyList<string> ConstructorParams { get; init; }

    /// <summary>Methods to copy from the god class into the new class.</summary>
    public required IReadOnlyList<MethodToCopy> MethodsToCopy { get; init; }

    /// <summary>Helper methods that move entirely (only called by this seam).</summary>
    public required IReadOnlyList<MethodToCopy> HelpersThatMove { get; init; }

    /// <summary>Records/DTOs to move from the god class.</summary>
    public required IReadOnlyList<string> RecordsToMove { get; init; }
}

public sealed record InterfaceFileOperation
{
    /// <summary>Interface name.</summary>
    public required string Name { get; init; }

    /// <summary>Relative path for the interface file.</summary>
    public required string Path { get; init; }

    /// <summary>Namespace for the interface.</summary>
    public required string Namespace { get; init; }

    /// <summary>Why this namespace/path was selected.</summary>
    public required string PlacementReason { get; init; }

    /// <summary>
    /// Relative path of an existing interface file to reuse instead of generating.
    /// </summary>
    public string? ExistingPath { get; init; }
}

public sealed record MethodToCopy
{
    /// <summary>Method name (e.g., "ExecuteResourceSftpUploadStepAsync").</summary>
    public required string Name { get; init; }

    /// <summary>Inferred step type enum value for dispatch routing.</summary>
    public string? StepType { get; init; }
}

public sealed record ModifyGodClassOperation
{
    /// <summary>Methods to delete from the god class after extraction.</summary>
    public required IReadOnlyList<string> MethodsToDelete { get; init; }

    /// <summary>Dispatch cases to rewire from inline calls to dispatcher delegation.</summary>
    public required IReadOnlyList<DispatchRewire> DispatchRewires { get; init; }
}

public sealed record DispatchRewire
{
    /// <summary>The step type enum value (e.g., "WorkflowStepType.ResourceSftpUpload").</summary>
    public required string StepType { get; init; }

    /// <summary>Existing inline call to replace.</summary>
    public required string OldPattern { get; init; }

    /// <summary>New dispatcher call.</summary>
    public required string NewPattern { get; init; }
}

public sealed record DiRegistration
{
    /// <summary>File where DI is registered.</summary>
    public string? RegistrationFile { get; init; }

    /// <summary>The DI registration line to add.</summary>
    public required string RegistrationLine { get; init; }

    /// <summary>Additional DI registration lines that belong with the primary registration.</summary>
    public IReadOnlyList<string> AdditionalRegistrationLines { get; init; } = [];
}

public sealed record PromoteMethodOperation
{
    /// <summary>Method to promote.</summary>
    public required string MethodName { get; init; }

    /// <summary>Target class to promote to.</summary>
    public required string TargetClass { get; init; }

    /// <summary>Reason for promotion.</summary>
    public required string Reason { get; init; }
}

public sealed record ExtractionPatternInfo
{
    /// <summary>Pattern type (e.g., "interface_dispatcher").</summary>
    public required string PatternType { get; init; }

    /// <summary>Interface name.</summary>
    public required string InterfaceName { get; init; }

    /// <summary>Dispatcher class name.</summary>
    public string? DispatcherName { get; init; }

    /// <summary>Exemplar implementation to follow.</summary>
    public string? ExemplarFile { get; init; }

    /// <summary>Preferred DI registration pattern when the repo already expresses one.</summary>
    public string? DiRegistrationPattern { get; init; }
}

public sealed record ContractBoundaryDecisionInfo
{
    /// <summary>Boundary outcome such as reuse_existing or generate_narrower.</summary>
    public required string Outcome { get; init; }

    /// <summary>Machine-readable reason for the boundary choice.</summary>
    public required string Reason { get; init; }

    /// <summary>Existing interface BO evaluated, when one was observed.</summary>
    public string? ExistingInterfaceName { get; init; }

    /// <summary>Comparison strategy used to make the boundary decision.</summary>
    public string? ComparisonMode { get; init; }
}

public sealed record GeneralizationCandidateInfo
{
    /// <summary>Generalization outcome such as shared_surface_candidate.</summary>
    public required string Outcome { get; init; }

    /// <summary>Machine-readable reason for the candidate.</summary>
    public required string Reason { get; init; }

    /// <summary>Comparison mode used to discover the candidate.</summary>
    public string? ComparisonMode { get; init; }

    /// <summary>Other seam names that share the candidate surface.</summary>
    public IReadOnlyList<string> PeerSeams { get; init; } = [];

    /// <summary>Number of recipes participating in the candidate group.</summary>
    public int CandidateGroupSize { get; init; }

    /// <summary>Suggested shared abstraction name when BO can derive one safely.</summary>
    public string? SuggestedSharedAbstractionName { get; init; }

    /// <summary>Suggested shared implementation base name when BO can derive one safely.</summary>
    public string? SuggestedSharedImplementationBaseName { get; init; }
}

public sealed record ArchitecturalPromotionCandidateInfo
{
    /// <summary>Architectural promotion outcome such as subsystem_candidate.</summary>
    public required string Outcome { get; init; }

    /// <summary>Machine-readable reason for the promotion.</summary>
    public required string Reason { get; init; }

    /// <summary>Promotion strategy used to derive the subsystem.</summary>
    public string? PromotionMode { get; init; }

    /// <summary>Suggested subsystem role name BO would promote this cluster toward.</summary>
    public string? SuggestedSubsystemName { get; init; }

    /// <summary>Suggested owner-facing facade name for the subsystem.</summary>
    public string? SuggestedFacadeName { get; init; }

    /// <summary>Suggested DI registration line for the generated facade artifact.</summary>
    public string? SuggestedFacadeRegistrationLine { get; init; }

    /// <summary>Other seam names participating in the same architectural cluster.</summary>
    public IReadOnlyList<string> PeerSeams { get; init; } = [];

    /// <summary>Number of recipes participating in the architectural cluster.</summary>
    public int CandidateGroupSize { get; init; }
}
