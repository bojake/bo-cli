using System.Text.Json;

namespace BO.Core.Indexing;

public sealed record RefactorScoringRules(
    string FormulaKey,
    RpsWeights Weights,
    ArchitecturalStressWeights ArchitecturalStressWeights,
    RpsNormalizationCeilings NormalizationCeilings,
    RpsThresholds Thresholds,
    HardPivotGateRules HardPivotGates,
    RpsDriverThresholds DriverThresholds)
{
    public static RefactorScoringRules Default { get; } = new(
        "rps_v0_1",
        new RpsWeights(0.18, 0.07, 0.05, 0.20, 0.15, 0.15, 0.20),
        new ArchitecturalStressWeights(0.25, 0.25, 0.20, 0.15, 0.15),
        new RpsNormalizationCeilings(500.0, 8.0, 3000.0, 20.0, 20.0, 100.0, 20.0),
        new RpsThresholds(34.0, 35.0, 49.0, 50.0, 64.0, 65.0, 79.0, 80.0),
        new HardPivotGateRules(
            new ResponsibilityOverloadGate(true, 4),
            new ContextOverloadGate(true, 8, 12000),
            new DependencyHubInstabilityGate(true, 15, 15),
            new GodClassGate(true, 500, 120),
            new ExtremeComplexityGate(true, 1000)),
        new RpsDriverThresholds(50.0, 50.0, 50.0, 40.0, 50.0, 50.0));

    public static RefactorScoringRules FromJson(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        if (!document.RootElement.TryGetProperty("rps", out var rps))
        {
            return Default;
        }

        var defaults = Default;
        return new RefactorScoringRules(
            GetString(rps, defaults.FormulaKey, "formula_key"),
            new RpsWeights(
                GetDouble(rps, defaults.Weights.CognitiveComplexityNorm, "weights", "cognitive_complexity_norm"),
                GetDouble(rps, defaults.Weights.NestingDepthNorm, "weights", "nesting_depth_norm"),
                GetDouble(rps, defaults.Weights.LocNorm, "weights", "loc_norm"),
                GetDouble(rps, defaults.Weights.ResponsibilitySpreadNorm, "weights", "responsibility_spread_norm"),
                GetDouble(rps, defaults.Weights.ArchitecturalStressNorm, "weights", "architectural_stress_norm"),
                GetDouble(rps, defaults.Weights.ChangePainNorm, "weights", "change_pain_norm"),
                GetDouble(rps, defaults.Weights.ContextBurdenNorm, "weights", "context_burden_norm")),
            new ArchitecturalStressWeights(
                GetDouble(rps, defaults.ArchitecturalStressWeights.FanInNorm, "architectural_stress_weights", "fan_in_norm"),
                GetDouble(rps, defaults.ArchitecturalStressWeights.FanOutNorm, "architectural_stress_weights", "fan_out_norm"),
                GetDouble(rps, defaults.ArchitecturalStressWeights.CentralityNorm, "architectural_stress_weights", "centrality_norm"),
                GetDouble(rps, defaults.ArchitecturalStressWeights.CircularDependencyRiskNorm, "architectural_stress_weights", "circular_dependency_risk_norm"),
                GetDouble(rps, defaults.ArchitecturalStressWeights.ImportDiversityNorm, "architectural_stress_weights", "import_diversity_norm")),
            new RpsNormalizationCeilings(
                GetDouble(rps, defaults.NormalizationCeilings.CognitiveComplexity, "normalization_ceilings", "cognitive_complexity"),
                GetDouble(rps, defaults.NormalizationCeilings.NestingDepth, "normalization_ceilings", "nesting_depth"),
                GetDouble(rps, defaults.NormalizationCeilings.Loc, "normalization_ceilings", "loc"),
                GetDouble(rps, defaults.NormalizationCeilings.FanIn, "normalization_ceilings", "fan_in"),
                GetDouble(rps, defaults.NormalizationCeilings.FanOut, "normalization_ceilings", "fan_out"),
                GetDouble(rps, defaults.NormalizationCeilings.ResponsibilitySpread, "normalization_ceilings", "responsibility_spread"),
                GetDouble(rps, defaults.NormalizationCeilings.ContextBurdenFiles, "normalization_ceilings", "context_burden_files")),
            new RpsThresholds(
                GetDouble(rps, defaults.Thresholds.NoneMax, "thresholds", "none_max"),
                GetDouble(rps, defaults.Thresholds.ObserveMin, "thresholds", "observe_min"),
                GetDouble(rps, defaults.Thresholds.ObserveMax, "thresholds", "observe_max"),
                GetDouble(rps, defaults.Thresholds.SuggestRefactorMin, "thresholds", "suggest_refactor_min"),
                GetDouble(rps, defaults.Thresholds.SuggestRefactorMax, "thresholds", "suggest_refactor_max"),
                GetDouble(rps, defaults.Thresholds.StrongPivotMin, "thresholds", "strong_pivot_min"),
                GetDouble(rps, defaults.Thresholds.StrongPivotMax, "thresholds", "strong_pivot_max"),
                GetDouble(rps, defaults.Thresholds.RefactorNowMin, "thresholds", "refactor_now_min")),
            new HardPivotGateRules(
                new ResponsibilityOverloadGate(
                    GetBool(rps, defaults.HardPivotGates.ResponsibilityOverload.Enabled, "hard_pivot_gates", "responsibility_overload", "enabled"),
                    GetInt(rps, defaults.HardPivotGates.ResponsibilityOverload.MinimumResponsibilityClusters, "hard_pivot_gates", "responsibility_overload", "minimum_responsibility_clusters")),
                new ContextOverloadGate(
                    GetBool(rps, defaults.HardPivotGates.ContextOverload.Enabled, "hard_pivot_gates", "context_overload", "enabled"),
                    GetInt(rps, defaults.HardPivotGates.ContextOverload.SafeEditFileCount, "hard_pivot_gates", "context_overload", "safe_edit_file_count"),
                    GetInt(rps, defaults.HardPivotGates.ContextOverload.SafeEditTokenCost, "hard_pivot_gates", "context_overload", "safe_edit_token_cost")),
                new DependencyHubInstabilityGate(
                    GetBool(rps, defaults.HardPivotGates.DependencyHubInstability.Enabled, "hard_pivot_gates", "dependency_hub_instability", "enabled"),
                    GetInt(rps, defaults.HardPivotGates.DependencyHubInstability.MinimumFanIn, "hard_pivot_gates", "dependency_hub_instability", "minimum_fan_in"),
                    GetInt(rps, defaults.HardPivotGates.DependencyHubInstability.MinimumFanOut, "hard_pivot_gates", "dependency_hub_instability", "minimum_fan_out")),
                new GodClassGate(
                    GetBool(rps, defaults.HardPivotGates.GodClass.Enabled, "hard_pivot_gates", "god_class", "enabled"),
                    GetInt(rps, defaults.HardPivotGates.GodClass.Loc, "hard_pivot_gates", "god_class", "loc"),
                    GetInt(rps, defaults.HardPivotGates.GodClass.CognitiveComplexity, "hard_pivot_gates", "god_class", "cognitive_complexity")),
                new ExtremeComplexityGate(
                    GetBool(rps, defaults.HardPivotGates.ExtremeComplexity.Enabled, "hard_pivot_gates", "extreme_complexity", "enabled"),
                    GetInt(rps, defaults.HardPivotGates.ExtremeComplexity.CognitiveComplexity, "hard_pivot_gates", "extreme_complexity", "cognitive_complexity"))),
            new RpsDriverThresholds(
                GetDouble(rps, defaults.DriverThresholds.CognitiveComplexityNorm, "driver_thresholds", "cognitive_complexity_norm"),
                GetDouble(rps, defaults.DriverThresholds.NestingDepthNorm, "driver_thresholds", "nesting_depth_norm"),
                GetDouble(rps, defaults.DriverThresholds.LocNorm, "driver_thresholds", "loc_norm"),
                GetDouble(rps, defaults.DriverThresholds.ResponsibilitySpreadNorm, "driver_thresholds", "responsibility_spread_norm"),
                GetDouble(rps, defaults.DriverThresholds.ArchitecturalStressNorm, "driver_thresholds", "architectural_stress_norm"),
                GetDouble(rps, defaults.DriverThresholds.FanOutNorm, "driver_thresholds", "fan_out_norm")));
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

    private static bool GetBool(JsonElement element, bool fallback, params string[] path)
    {
        return TryGet(element, path, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
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

public sealed record RpsWeights(
    double CognitiveComplexityNorm,
    double NestingDepthNorm,
    double LocNorm,
    double ResponsibilitySpreadNorm,
    double ArchitecturalStressNorm,
    double ChangePainNorm,
    double ContextBurdenNorm);

public sealed record ArchitecturalStressWeights(
    double FanInNorm,
    double FanOutNorm,
    double CentralityNorm,
    double CircularDependencyRiskNorm,
    double ImportDiversityNorm);

public sealed record RpsNormalizationCeilings(
    double CognitiveComplexity,
    double NestingDepth,
    double Loc,
    double FanIn,
    double FanOut,
    double ResponsibilitySpread,
    double ContextBurdenFiles);

public sealed record RpsThresholds(
    double NoneMax,
    double ObserveMin,
    double ObserveMax,
    double SuggestRefactorMin,
    double SuggestRefactorMax,
    double StrongPivotMin,
    double StrongPivotMax,
    double RefactorNowMin);

public sealed record HardPivotGateRules(
    ResponsibilityOverloadGate ResponsibilityOverload,
    ContextOverloadGate ContextOverload,
    DependencyHubInstabilityGate DependencyHubInstability,
    GodClassGate GodClass,
    ExtremeComplexityGate ExtremeComplexity);

public sealed record ResponsibilityOverloadGate(bool Enabled, int MinimumResponsibilityClusters);

public sealed record ContextOverloadGate(bool Enabled, int SafeEditFileCount, int SafeEditTokenCost);

public sealed record DependencyHubInstabilityGate(bool Enabled, int MinimumFanIn, int MinimumFanOut);

public sealed record GodClassGate(bool Enabled, int Loc, int CognitiveComplexity);

public sealed record ExtremeComplexityGate(bool Enabled, int CognitiveComplexity);

public sealed record RpsDriverThresholds(
    double CognitiveComplexityNorm,
    double NestingDepthNorm,
    double LocNorm,
    double ResponsibilitySpreadNorm,
    double ArchitecturalStressNorm,
    double FanOutNorm);
