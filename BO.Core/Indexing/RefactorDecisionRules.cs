using System.Text.Json;

namespace BO.Core.Indexing;

public sealed record RefactorDecisionRules(
    string Version,
    DecisionMinimumRules DecisionMinimums,
    PivotThresholdRules PivotThresholds,
    CandidateSeamRules CandidateSeams)
{
    public static RefactorDecisionRules Default { get; } = new(
        "0.1.0",
        new DecisionMinimumRules(50.0),
        new PivotThresholdRules(
            new OrchestrationOverloadThresholds(3, 5, 4),
            new BoundaryMixingThresholds(2),
            new SideEffectComputationThresholds(8, 4),
            new PolicyBranchingThresholds(4, 10),
            new LatentModuleThresholds(4, 3)),
        new CandidateSeamRules(
            3,
            0,
            5,
            3,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["persistence"] = "persistence_adapter",
                ["transport"] = "transport_adapter",
                ["security"] = "security_gate",
                ["caching"] = "cache_adapter",
                ["auditing"] = "audit_publisher",
                ["orchestration"] = "orchestrator"
            },
            "_handler",
            [
                new NamedSymbolSeamRule(
                    "cli_json_output",
                    ["function", "method"],
                    ["WriteJson", "WriteJsonAsync", "SerializeJson", "WriteJsonResponse"])
            ]));

    public static RefactorDecisionRules FromJson(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = document.RootElement;
        var defaults = Default;
        return new RefactorDecisionRules(
            GetString(root, defaults.Version, "version"),
            new DecisionMinimumRules(
                GetDouble(root, defaults.DecisionMinimums.MinimumRpsScore, "decisionMinimums", "minimumRpsScore")),
            new PivotThresholdRules(
                new OrchestrationOverloadThresholds(
                    GetInt(root, defaults.PivotThresholds.OrchestrationOverload.MinimumDominantResponsibilities, "pivotThresholds", "orchestrationOverload", "minimumDominantResponsibilities"),
                    GetInt(root, defaults.PivotThresholds.OrchestrationOverload.MinimumFanOut, "pivotThresholds", "orchestrationOverload", "minimumFanOut"),
                    GetInt(root, defaults.PivotThresholds.OrchestrationOverload.MinimumOutgoingCallCount, "pivotThresholds", "orchestrationOverload", "minimumOutgoingCallCount")),
                new BoundaryMixingThresholds(
                    GetInt(root, defaults.PivotThresholds.BoundaryMixing.MinimumDistinctBoundaryTypes, "pivotThresholds", "boundaryMixing", "minimumDistinctBoundaryTypes")),
                new SideEffectComputationThresholds(
                    GetInt(root, defaults.PivotThresholds.SideEffectComputation.MinimumCognitiveComplexity, "pivotThresholds", "sideEffectComputation", "minimumCognitiveComplexity"),
                    GetInt(root, defaults.PivotThresholds.SideEffectComputation.MinimumBranchCount, "pivotThresholds", "sideEffectComputation", "minimumBranchCount")),
                new PolicyBranchingThresholds(
                    GetInt(root, defaults.PivotThresholds.PolicyBranching.MinimumBranchCount, "pivotThresholds", "policyBranching", "minimumBranchCount"),
                    GetInt(root, defaults.PivotThresholds.PolicyBranching.MinimumCognitiveComplexity, "pivotThresholds", "policyBranching", "minimumCognitiveComplexity")),
                new LatentModuleThresholds(
                    GetInt(root, defaults.PivotThresholds.LatentModule.MinimumFileSymbolCount, "pivotThresholds", "latentModule", "minimumFileSymbolCount"),
                    GetInt(root, defaults.PivotThresholds.LatentModule.MinimumInternalCallCount, "pivotThresholds", "latentModule", "minimumInternalCallCount"))),
            new CandidateSeamRules(
                GetInt(root, defaults.CandidateSeams.OrchestratorMinimumCallCount, "candidateSeams", "orchestratorMinimumCallCount"),
                GetInt(root, defaults.CandidateSeams.ExtractableLeafCallCount, "candidateSeams", "extractableLeafCallCount"),
                GetInt(root, defaults.CandidateSeams.PureLogicComplexityNoteMinimum, "candidateSeams", "pureLogicComplexityNoteMinimum"),
                GetInt(root, defaults.CandidateSeams.BoundaryAdapterTargetLimit, "candidateSeams", "boundaryAdapterTargetLimit"),
                GetStringMap(root, defaults.CandidateSeams.RoleSeams, "candidateSeams", "roleSeams"),
                GetString(root, defaults.CandidateSeams.RoleFallbackSuffix, "candidateSeams", "roleFallbackSuffix"),
                GetNamedSymbolSeams(root, defaults.CandidateSeams.NamedSymbolSeams, "candidateSeams", "namedSymbolSeams")));
    }

    private static IReadOnlyDictionary<string, string> GetStringMap(
        JsonElement root,
        IReadOnlyDictionary<string, string> fallback,
        params string[] path)
    {
        if (!TryGet(root, path, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        return value.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(
                property => property.Name,
                property => property.Value.GetString() ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static IReadOnlyList<NamedSymbolSeamRule> GetNamedSymbolSeams(
        JsonElement root,
        IReadOnlyList<NamedSymbolSeamRule> fallback,
        params string[] path)
    {
        if (!TryGet(root, path, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        var rules = new List<NamedSymbolSeamRule>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("seamPrefix", out var seamPrefixElement) ||
                seamPrefixElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var seamPrefix = seamPrefixElement.GetString();
            if (string.IsNullOrWhiteSpace(seamPrefix))
            {
                continue;
            }

            rules.Add(new NamedSymbolSeamRule(
                seamPrefix,
                GetStringArray(item, "symbolKinds"),
                GetStringArray(item, "displayNames")));
        }

        return rules;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => item.GetString()!)
            .ToArray();
    }

    private static string GetString(JsonElement element, string fallback, params string[] path)
    {
        return TryGet(element, path, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static double GetDouble(JsonElement element, double fallback, params string[] path)
    {
        return TryGet(element, path, out var value) && value.TryGetDouble(out var parsed)
            ? parsed
            : fallback;
    }

    private static int GetInt(JsonElement element, int fallback, params string[] path)
    {
        return TryGet(element, path, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : fallback;
    }

    private static bool TryGet(JsonElement element, IReadOnlyList<string> path, out JsonElement value)
    {
        value = element;
        foreach (var part in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record DecisionMinimumRules(double MinimumRpsScore);

public sealed record PivotThresholdRules(
    OrchestrationOverloadThresholds OrchestrationOverload,
    BoundaryMixingThresholds BoundaryMixing,
    SideEffectComputationThresholds SideEffectComputation,
    PolicyBranchingThresholds PolicyBranching,
    LatentModuleThresholds LatentModule);

public sealed record OrchestrationOverloadThresholds(
    int MinimumDominantResponsibilities,
    int MinimumFanOut,
    int MinimumOutgoingCallCount);

public sealed record BoundaryMixingThresholds(int MinimumDistinctBoundaryTypes);

public sealed record SideEffectComputationThresholds(
    int MinimumCognitiveComplexity,
    int MinimumBranchCount);

public sealed record PolicyBranchingThresholds(
    int MinimumBranchCount,
    int MinimumCognitiveComplexity);

public sealed record LatentModuleThresholds(
    int MinimumFileSymbolCount,
    int MinimumInternalCallCount);

public sealed record CandidateSeamRules(
    int OrchestratorMinimumCallCount,
    int ExtractableLeafCallCount,
    int PureLogicComplexityNoteMinimum,
    int BoundaryAdapterTargetLimit,
    IReadOnlyDictionary<string, string> RoleSeams,
    string RoleFallbackSuffix,
    IReadOnlyList<NamedSymbolSeamRule> NamedSymbolSeams);

public sealed record NamedSymbolSeamRule(
    string SeamPrefix,
    IReadOnlyList<string> SymbolKinds,
    IReadOnlyList<string> DisplayNames);
