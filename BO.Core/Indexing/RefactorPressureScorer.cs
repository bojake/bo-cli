namespace BO.Core.Indexing;

/// <summary>
/// Computes Refactor Pressure Scores (RPS) from complexity and responsibility
/// profiles using the weights and thresholds defined in scoring_config.json.
///
/// RPS formula (from summary_architecture.md §13):
///
///   RPS =
///     0.18 * CognitiveComplexityNorm +
///     0.07 * NestingDepthNorm +
///     0.05 * LOCNorm +
///     0.20 * ResponsibilitySpreadNorm +
///     0.15 * ArchitecturalStressNorm +
///     0.15 * ChangePainNorm +          (not yet available — defaults to 0)
///     0.20 * ContextBurdenNorm          (not yet available — defaults to 0)
///
/// Each subscore is normalized to 0–100 using soft thresholds from §11:
///   Cognitive complexity > 15, Nesting > 4, Parameters > 6, Fan-out > 10, etc.
/// </summary>
public sealed class RefactorPressureScorer
{
    private readonly RefactorScoringRules _defaultRules;

    public RefactorPressureScorer()
        : this(RefactorScoringRules.Default)
    {
    }

    public RefactorPressureScorer(RefactorScoringRules defaultRules)
    {
        _defaultRules = defaultRules;
    }

    public IReadOnlyList<RefactorPressureScoreRecord> Score(
        IReadOnlyList<ComplexityProfileRecord> complexityProfiles,
        IReadOnlyList<ResponsibilityProfileRecord> responsibilityProfiles,
        IReadOnlyList<ContextBurdenRecord> contextBurdens)
        => Score(complexityProfiles, responsibilityProfiles, contextBurdens, _defaultRules);

    public IReadOnlyList<RefactorPressureScoreRecord> Score(
        IReadOnlyList<ComplexityProfileRecord> complexityProfiles,
        IReadOnlyList<ResponsibilityProfileRecord> responsibilityProfiles,
        IReadOnlyList<ContextBurdenRecord> contextBurdens,
        RefactorScoringRules? scoringRules)
    {
        var rules = scoringRules ?? _defaultRules;
        var responsibilityByTarget = responsibilityProfiles
            .ToDictionary(profile => profile.TargetId, StringComparer.Ordinal);
        var contextBurdenByTarget = contextBurdens
            .ToDictionary(cb => cb.TargetId, StringComparer.Ordinal);

        var scores = new List<RefactorPressureScoreRecord>();

        // Only score file-level complexity; symbol-level profiles are used
        // by RefactorDecisionDeriver for seam identification, not aggregate RPS.
        foreach (var complexity in complexityProfiles.Where(p => p.TargetKind == "file"))
        {
            responsibilityByTarget.TryGetValue(complexity.TargetId, out var responsibility);

            // ── Normalize individual metrics to 0–100 ───────────────────────
            var cognitiveNorm = Normalize(complexity.CognitiveComplexity, rules.NormalizationCeilings.CognitiveComplexity);
            var nestingNorm = Normalize(complexity.NestingDepth, rules.NormalizationCeilings.NestingDepth);
            var locNorm = Normalize(complexity.Loc, rules.NormalizationCeilings.Loc);
            var fanInNorm = Normalize(complexity.FanIn, rules.NormalizationCeilings.FanIn);
            var fanOutNorm = Normalize(complexity.FanOut, rules.NormalizationCeilings.FanOut);
            var responsibilitySpreadNorm = responsibility?.ResponsibilitySpreadScore ?? 0.0;

            // ── Architectural stress (partial — fan-in/out only, others default to 0) ──
            var architecturalStressNorm =
                rules.ArchitecturalStressWeights.FanInNorm * fanInNorm +
                rules.ArchitecturalStressWeights.FanOutNorm * fanOutNorm;
            // Rescale to 0–100 based on available weights
            var availableStressWeight =
                rules.ArchitecturalStressWeights.FanInNorm +
                rules.ArchitecturalStressWeights.FanOutNorm;
            architecturalStressNorm = availableStressWeight > 0
                ? architecturalStressNorm / availableStressWeight
                : 0.0;

            // ── ChangePain: not yet available ────────────────────────────────
            var changePainNorm = 0.0;

            // ── ContextBurden: from safe-edit traversal ──────────────────────
            contextBurdenByTarget.TryGetValue(complexity.TargetId, out var contextBurden);
            var contextBurdenNorm = contextBurden is not null
                ? Normalize(contextBurden.SafeEditFileCount, rules.NormalizationCeilings.ContextBurdenFiles)
                : 0.0;

            // ── Compute aggregate RPS ────────────────────────────────────────
            // Redistribute weights to compensate for unavailable metrics.
            // Without this, max achievable RPS would be (1 - missing_weight) * 100.
            var availableWeight =
                rules.Weights.CognitiveComplexityNorm + rules.Weights.NestingDepthNorm + rules.Weights.LocNorm +
                rules.Weights.ResponsibilitySpreadNorm + rules.Weights.ArchitecturalStressNorm +
                (contextBurden is not null ? rules.Weights.ContextBurdenNorm : 0.0) +
                (changePainNorm > 0 ? rules.Weights.ChangePainNorm : 0.0);

            var scale = availableWeight > 0 ? 1.0 / availableWeight : 1.0;

            var rawRps =
                (rules.Weights.CognitiveComplexityNorm * cognitiveNorm +
                 rules.Weights.NestingDepthNorm * nestingNorm +
                 rules.Weights.LocNorm * locNorm +
                 rules.Weights.ResponsibilitySpreadNorm * responsibilitySpreadNorm +
                 rules.Weights.ArchitecturalStressNorm * architecturalStressNorm +
                 rules.Weights.ChangePainNorm * changePainNorm +
                 rules.Weights.ContextBurdenNorm * contextBurdenNorm) * scale;

            var rps = Math.Round(Math.Clamp(rawRps, 0.0, 100.0), 2, MidpointRounding.AwayFromZero);

            // ── Hard pivot gates ─────────────────────────────────────────────
            var firedGates = new List<string>();

            if (rules.HardPivotGates.ResponsibilityOverload.Enabled &&
                responsibility is not null &&
                responsibility.DominantResponsibilities.Count >= rules.HardPivotGates.ResponsibilityOverload.MinimumResponsibilityClusters)
            {
                firedGates.Add("responsibility_overload");
            }

            // Context overload gate: from safe-edit traversal
            if (rules.HardPivotGates.ContextOverload.Enabled &&
                contextBurden is not null)
            {
                if (contextBurden.SafeEditFileCount >= rules.HardPivotGates.ContextOverload.SafeEditFileCount ||
                    contextBurden.SafeEditTokenEstimate >= rules.HardPivotGates.ContextOverload.SafeEditTokenCost)
                {
                    firedGates.Add("context_overload");
                }
            }

            // High churn hotspot gate: not yet measurable (requires git history)
            // Dependency hub instability: check fan-in + fan-out percentiles (simplified)
            if (rules.HardPivotGates.DependencyHubInstability.Enabled &&
                complexity.FanIn >= rules.HardPivotGates.DependencyHubInstability.MinimumFanIn &&
                complexity.FanOut >= rules.HardPivotGates.DependencyHubInstability.MinimumFanOut)
            {
                firedGates.Add("dependency_hub_instability");
            }

            // God class gate: file is massive AND complex
            if (rules.HardPivotGates.GodClass.Enabled &&
                complexity.Loc >= rules.HardPivotGates.GodClass.Loc &&
                complexity.CognitiveComplexity >= rules.HardPivotGates.GodClass.CognitiveComplexity)
            {
                firedGates.Add("god_class");
            }

            // Extreme complexity gate: standalone CC is off the charts
            if (rules.HardPivotGates.ExtremeComplexity.Enabled &&
                complexity.CognitiveComplexity >= rules.HardPivotGates.ExtremeComplexity.CognitiveComplexity)
            {
                firedGates.Add("extreme_complexity");
            }

            // ── Determine recommendation class ──────────────────────────────
            var recommendation = ClassifyRecommendation(rps, firedGates, rules);

            // ── Collect evidence drivers ─────────────────────────────────────
            var drivers = new List<string>();
            if (cognitiveNorm > rules.DriverThresholds.CognitiveComplexityNorm) drivers.Add("high_cognitive_complexity");
            if (nestingNorm > rules.DriverThresholds.NestingDepthNorm) drivers.Add("high_nesting_depth");
            if (locNorm > rules.DriverThresholds.LocNorm) drivers.Add("high_loc");
            if (responsibilitySpreadNorm > rules.DriverThresholds.ResponsibilitySpreadNorm) drivers.Add("high_responsibility_spread");
            if (architecturalStressNorm > rules.DriverThresholds.ArchitecturalStressNorm) drivers.Add("high_architectural_stress");
            if (fanOutNorm > rules.DriverThresholds.FanOutNorm) drivers.Add("high_fan_out");

            // ── Confidence: reflects how many RPS components are available ───
            var totalWeight = rules.Weights.CognitiveComplexityNorm + rules.Weights.NestingDepthNorm +
                              rules.Weights.LocNorm + rules.Weights.ResponsibilitySpreadNorm + rules.Weights.ArchitecturalStressNorm +
                              rules.Weights.ChangePainNorm + rules.Weights.ContextBurdenNorm;
            var confidence = Math.Round(availableWeight / totalWeight, 2, MidpointRounding.AwayFromZero);

            scores.Add(new RefactorPressureScoreRecord(
                $"rps:{complexity.TargetId}",
                complexity.TargetId,
                complexity.TargetKind,
                rps,
                recommendation,
                [.. drivers],
                [.. firedGates],
                confidence));
        }

        return scores;
    }

    private static string ClassifyRecommendation(
        double rps,
        IReadOnlyList<string> firedGates,
        RefactorScoringRules rules)
    {
        // Hard gates can elevate the recommendation
        if (firedGates.Count > 0)
        {
            // Any gate firing at minimum triggers "suggest_refactor"
            if (rps >= rules.Thresholds.StrongPivotMin || firedGates.Count >= 2)
            {
                return "refactor_now";
            }

            return rps >= rules.Thresholds.SuggestRefactorMin ? "strong_pivot" : "suggest_refactor";
        }

        if (rps >= rules.Thresholds.RefactorNowMin) return "refactor_now";
        if (rps >= rules.Thresholds.StrongPivotMin) return "strong_pivot";
        if (rps >= rules.Thresholds.SuggestRefactorMin) return "suggest_refactor";
        if (rps >= rules.Thresholds.ObserveMin) return "observe";
        return "none";
    }

    private static double Normalize(double value, double ceiling)
    {
        if (ceiling <= 0) return 0;
        return Math.Clamp(value / ceiling * 100.0, 0.0, 100.0);
    }
}
