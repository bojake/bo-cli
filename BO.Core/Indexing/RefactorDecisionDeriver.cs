namespace BO.Core.Indexing;

/// <summary>
/// Produces a <see cref="RefactorDecisionRecord"/> for every file where the
/// RPS triggers a refactor recommendation (RPS ≥ 50) or a hard pivot gate fires.
///
/// The decision includes:
///   - A classified pivot type (from derivation_rules.md §Refactor decision)
///   - Evidence-backed drivers (already persisted categories)
///   - Candidate seams (symbols or responsibility domains that are natural extraction boundaries)
///
/// Uses symbol-level complexity profiles to rank seam candidates, and infers
/// per-symbol IO exposure through the symbol→file→boundary call graph.
/// </summary>
public sealed class RefactorDecisionDeriver
{
    // ── Allowed pivot types (derivation_rules.md §Pivot type suggestion) ─────
    private const string ExtractPureLogic = "extract_pure_logic";
    private const string ExtractBoundaryAdapter = "extract_boundary_adapter";
    private const string ExtractPolicy = "extract_policy";
    private const string SplitOrchestrationFromExecution = "split_orchestration_from_execution";
    private const string PromoteLatentModule = "promote_latent_module";

    private readonly RefactorDecisionRules _defaultRules;

    public RefactorDecisionDeriver()
        : this(RefactorDecisionRules.Default)
    {
    }

    public RefactorDecisionDeriver(RefactorDecisionRules defaultRules)
    {
        _defaultRules = defaultRules;
    }

    public IReadOnlyList<RefactorDecisionRecord> Derive(
        IReadOnlyList<RefactorPressureScoreRecord> rpsScores,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyList<SymbolDependencyRecord> symbolDependencies,
        IReadOnlyList<BoundaryInteractionRecord> boundaryInteractions,
        IReadOnlyList<EffectProfileRecord> effectProfiles,
        IReadOnlyList<ResponsibilityProfileRecord> responsibilityProfiles,
        IReadOnlyList<ComplexityProfileRecord> complexityProfiles)
        => Derive(
            rpsScores,
            symbols,
            symbolDependencies,
            boundaryInteractions,
            effectProfiles,
            responsibilityProfiles,
            complexityProfiles,
            _defaultRules);

    public IReadOnlyList<RefactorDecisionRecord> Derive(
        IReadOnlyList<RefactorPressureScoreRecord> rpsScores,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyList<SymbolDependencyRecord> symbolDependencies,
        IReadOnlyList<BoundaryInteractionRecord> boundaryInteractions,
        IReadOnlyList<EffectProfileRecord> effectProfiles,
        IReadOnlyList<ResponsibilityProfileRecord> responsibilityProfiles,
        IReadOnlyList<ComplexityProfileRecord> complexityProfiles,
        RefactorDecisionRules? decisionRules)
    {
        var rules = decisionRules ?? _defaultRules;
        var decisions = new List<RefactorDecisionRecord>();

        // Pre-index lookups
        var effectByTarget = effectProfiles
            .ToDictionary(p => p.TargetId, StringComparer.Ordinal);
        var responsibilityByTarget = responsibilityProfiles
            .ToDictionary(p => p.TargetId, StringComparer.Ordinal);
        var complexityByTarget = complexityProfiles
            .Where(p => p.TargetKind == "file")
            .ToDictionary(p => p.TargetId, StringComparer.Ordinal);
        var symbolsByFile = symbols
            .GroupBy(s => s.FileId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var boundariesByFile = boundaryInteractions
            .GroupBy(b => b.FileId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        // Symbol-level complexity profiles (indexed by symbol ID)
        var symbolComplexity = complexityProfiles
            .Where(p => p.TargetKind == "symbol")
            .ToDictionary(p => p.TargetId, StringComparer.Ordinal);

        // Build inter-symbol call graph for seam analysis
        var callsFrom = symbolDependencies
            .Where(d => d.RelationType == "calls")
            .GroupBy(d => d.FromSymbolId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        // Build symbol → file lookup for IO inference via call graph
        var symbolToFile = symbols.ToDictionary(s => s.Id, s => s.FileId, StringComparer.Ordinal);

        foreach (var rps in rpsScores)
        {
            // Only generate decisions when RPS ≥ 50 or a hard gate fired
            if (rps.Score < rules.DecisionMinimums.MinimumRpsScore && rps.FiredGates.Count == 0)
            {
                continue;
            }

            effectByTarget.TryGetValue(rps.TargetId, out var effect);
            responsibilityByTarget.TryGetValue(rps.TargetId, out var responsibility);
            complexityByTarget.TryGetValue(rps.TargetId, out var complexity);
            symbolsByFile.TryGetValue(rps.TargetId, out var fileSymbols);
            boundariesByFile.TryGetValue(rps.TargetId, out var fileBoundaries);

            var pivotType = ClassifyPivotType(effect, responsibility, complexity, fileBoundaries, fileSymbols, callsFrom, rules);
            var candidateSeams = IdentifyCandidateSeams(
                pivotType, effect, responsibility, fileSymbols, fileBoundaries,
                callsFrom, symbolComplexity, symbolToFile, boundariesByFile, rules);

            decisions.Add(new RefactorDecisionRecord(
                $"decision:refactor:{rps.TargetId}",
                rps.TargetId,
                rps.Recommendation,
                pivotType,
                rps.Drivers,
                rps.FiredGates,
                candidateSeams,
                rps.Score,
                rps.Confidence));
        }

        return decisions;
    }

    /// <summary>
    /// Classifies the most appropriate pivot type based on the evidence signals.
    /// Rules from summary_architecture.md §3:
    ///
    ///   A. extract_pure_logic       — computation mixed with IO
    ///   B. extract_policy           — branching on type/mode/state is the complexity driver
    ///   C. extract_boundary_adapter — domain logic mixed with DB/API/cache/filesystem
    ///   D. split_orchestration      — one method coordinates + executes
    ///   E. promote_latent_module    — co-changing symbols form a hidden subsystem
    /// </summary>
    private static string ClassifyPivotType(
        EffectProfileRecord? effect,
        ResponsibilityProfileRecord? responsibility,
        ComplexityProfileRecord? complexity,
        BoundaryInteractionRecord[]? boundaries,
        SymbolRecord[]? fileSymbols,
        Dictionary<string, SymbolDependencyRecord[]> callsFrom,
        RefactorDecisionRules rules)
    {
        // ── Priority 1: Orchestration overload ───────────────────────────────
        if (responsibility is not null && complexity is not null)
        {
            var hasHighResponsibility = responsibility.DominantResponsibilities.Count >=
                rules.PivotThresholds.OrchestrationOverload.MinimumDominantResponsibilities;
            var hasHighFanOut = complexity.FanOut >= rules.PivotThresholds.OrchestrationOverload.MinimumFanOut;
            var hasHighCallCount = fileSymbols is not null &&
                fileSymbols.Any(s => callsFrom.TryGetValue(s.Id, out var calls) &&
                                     calls.Length >= rules.PivotThresholds.OrchestrationOverload.MinimumOutgoingCallCount);

            if (hasHighResponsibility && (hasHighFanOut || hasHighCallCount))
            {
                return SplitOrchestrationFromExecution;
            }
        }

        // ── Priority 2: Boundary mixing ─────────────────────────────────────
        if (boundaries is not null && boundaries.Length > 0)
        {
            var distinctBoundaryTypes = boundaries
                .Select(b => b.BoundaryType)
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (distinctBoundaryTypes >= rules.PivotThresholds.BoundaryMixing.MinimumDistinctBoundaryTypes)
            {
                return ExtractBoundaryAdapter;
            }
        }

        // ── Priority 3: Side effects mixed with computation ─────────────────
        if (effect is not null && complexity is not null)
        {
            var hasIO = effect.WritesState || effect.CallsExternalService || effect.EmitsEvents;
            var hasComputation =
                complexity.CognitiveComplexity >= rules.PivotThresholds.SideEffectComputation.MinimumCognitiveComplexity ||
                complexity.BranchCount >= rules.PivotThresholds.SideEffectComputation.MinimumBranchCount;

            if (hasIO && hasComputation)
            {
                return ExtractPureLogic;
            }
        }

        // ── Priority 4: Policy/strategy branching ───────────────────────────
        if (complexity is not null)
        {
            var hasMostlyBranching =
                complexity.BranchCount >= rules.PivotThresholds.PolicyBranching.MinimumBranchCount &&
                complexity.CognitiveComplexity >= rules.PivotThresholds.PolicyBranching.MinimumCognitiveComplexity;

            var hasLowIO = effect is null || (!effect.WritesState && !effect.CallsExternalService);

            if (hasMostlyBranching && hasLowIO)
            {
                return ExtractPolicy;
            }
        }

        // ── Priority 5: Latent module ───────────────────────────────────────
        if (fileSymbols is not null && fileSymbols.Length >= rules.PivotThresholds.LatentModule.MinimumFileSymbolCount)
        {
            var internalCallCount = 0;
            var fileSymbolIds = new HashSet<string>(fileSymbols.Select(s => s.Id), StringComparer.Ordinal);

            foreach (var symbol in fileSymbols)
            {
                if (callsFrom.TryGetValue(symbol.Id, out var outgoing))
                {
                    internalCallCount += outgoing.Count(d => fileSymbolIds.Contains(d.ToSymbolId));
                }
            }

            if (internalCallCount >= rules.PivotThresholds.LatentModule.MinimumInternalCallCount)
            {
                return PromoteLatentModule;
            }
        }

        // ── Default ─────────────────────────────────────────────────────────
        return ExtractPureLogic;
    }

    /// <summary>
    /// Identifies concrete candidate seam names, ranked by symbol-level complexity
    /// and annotated with IO/pure classification via call-graph traversal.
    /// </summary>
    private static IReadOnlyList<string> IdentifyCandidateSeams(
        string pivotType,
        EffectProfileRecord? effect,
        ResponsibilityProfileRecord? responsibility,
        SymbolRecord[]? fileSymbols,
        BoundaryInteractionRecord[]? boundaries,
        Dictionary<string, SymbolDependencyRecord[]> callsFrom,
        Dictionary<string, ComplexityProfileRecord> symbolComplexity,
        Dictionary<string, string> symbolToFile,
        Dictionary<string, BoundaryInteractionRecord[]> boundariesByFile,
        RefactorDecisionRules rules)
    {
        var seams = new List<string>();

        switch (pivotType)
        {
            case SplitOrchestrationFromExecution:
                // Domain-based seams from responsibility roles
                if (responsibility is not null)
                {
                    foreach (var role in responsibility.DominantResponsibilities)
                    {
                        seams.Add(RoleToSeamName(role, rules));
                    }
                }
                // Rank methods by outgoing call count — the highest is the orchestrator
                if (fileSymbols is not null)
                {
                    var ranked = fileSymbols
                        .Where(s => s.Kind is "function" or "method")
                        .Select(s => (Symbol: s, CallCount: callsFrom.TryGetValue(s.Id, out var c) ? c.Length : 0))
                        .OrderByDescending(x => x.CallCount)
                        .ToArray();

                    foreach (var (symbol, callCount) in ranked)
                    {
                        if (callCount >= rules.CandidateSeams.OrchestratorMinimumCallCount)
                        {
                            seams.Add($"orchestrator:{symbol.DisplayName}");
                        }
                        else if (callCount == rules.CandidateSeams.ExtractableLeafCallCount)
                        {
                            // Leaf method — candidate for extraction
                            var ioTag = InfersIO(symbol.Id, callsFrom, symbolToFile, boundariesByFile) ? "io" : "pure";
                            seams.Add($"extractable_{ioTag}:{symbol.DisplayName}");
                        }
                    }
                }
                break;

            case ExtractBoundaryAdapter:
                if (boundaries is not null)
                {
                    var boundaryTypes = boundaries
                        .Select(b => b.BoundaryType)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(b => b, StringComparer.Ordinal);

                    foreach (var bt in boundaryTypes)
                    {
                        seams.Add($"{bt}_adapter");

                        var targets = boundaries
                            .Where(b => b.BoundaryType == bt)
                            .Select(b => b.TargetName)
                            .Distinct(StringComparer.Ordinal)
                            .Take(Math.Max(0, rules.CandidateSeams.BoundaryAdapterTargetLimit));

                        foreach (var target in targets)
                        {
                            if (!string.IsNullOrEmpty(target))
                            {
                                seams.Add($"{bt}_adapter:{target}");
                            }
                        }
                    }

                    // Classify methods as touching specific boundary types
                    if (fileSymbols is not null)
                    {
                        foreach (var symbol in fileSymbols.Where(s => s.Kind is "function" or "method"))
                        {
                            var touchedBoundaries = InferBoundaryTypes(symbol.Id, callsFrom, symbolToFile, boundariesByFile);
                            if (touchedBoundaries.Count > 0)
                            {
                                seams.Add($"touches[{string.Join(",", touchedBoundaries)}]:{symbol.DisplayName}");
                            }
                        }
                    }
                }
                break;

            case ExtractPureLogic:
                // Use symbol-level complexity to identify which methods are hotspots,
                // and call-graph IO inference to classify pure vs IO methods.
                if (fileSymbols is not null)
                {
                    var classified = fileSymbols
                        .Where(s => s.Kind is "function" or "method")
                        .Select(s =>
                        {
                            var hasIO = InfersIO(s.Id, callsFrom, symbolToFile, boundariesByFile);
                            symbolComplexity.TryGetValue(s.Id, out var symCplx);
                            return (Symbol: s, HasIO: hasIO, Complexity: symCplx);
                        })
                        .OrderByDescending(x => x.Complexity?.CognitiveComplexity ?? 0)
                        .ToArray();

                    foreach (var (symbol, hasIO, cplx) in classified)
                    {
                        var tag = hasIO ? "io" : "pure";
                        var complexityNote = cplx is not null &&
                                             cplx.CognitiveComplexity >= rules.CandidateSeams.PureLogicComplexityNoteMinimum
                            ? $"[cc={cplx.CognitiveComplexity}]" : "";
                        seams.Add($"{tag}{complexityNote}:{symbol.DisplayName}");
                    }
                }

                // Side-effect extraction dimensions
                if (effect is not null)
                {
                    foreach (var sec in effect.SideEffectClasses)
                    {
                        seams.Add($"effect:{sec}");
                    }
                }
                break;

            case ExtractPolicy:
                // Rank methods by branching density — the highest is the policy hotspot
                if (fileSymbols is not null)
                {
                    var ranked = fileSymbols
                        .Where(s => s.Kind is "function" or "method")
                        .Where(s => !IsExplicitInterfaceImplementation(s))
                        .Select(s =>
                        {
                            symbolComplexity.TryGetValue(s.Id, out var cplx);
                            return (Symbol: s, Branches: cplx?.BranchCount ?? 0, Cognitive: cplx?.CognitiveComplexity ?? 0);
                        })
                        .OrderByDescending(x => x.Branches)
                        .ToArray();

                    foreach (var (symbol, branches, cognitive) in ranked)
                    {
                        if (branches > 0)
                        {
                            seams.Add($"policy[b={branches},cc={cognitive}]:{symbol.DisplayName}");
                        }
                        else
                        {
                            seams.Add($"helper:{symbol.DisplayName}");
                        }
                    }
                }
                break;

            case PromoteLatentModule:
                if (fileSymbols is not null)
                {
                    var fileSymbolIds = new HashSet<string>(
                        fileSymbols.Select(s => s.Id), StringComparer.Ordinal);

                    var members = fileSymbols
                        .Where(s =>
                        {
                            if (!callsFrom.TryGetValue(s.Id, out var calls)) return false;
                            return calls.Any(d => fileSymbolIds.Contains(d.ToSymbolId));
                        })
                        .Select(s =>
                        {
                            symbolComplexity.TryGetValue(s.Id, out var cplx);
                            return (Symbol: s, Complexity: cplx);
                        })
                        .OrderByDescending(x => x.Complexity?.CognitiveComplexity ?? 0)
                        .ToArray();

                    foreach (var (symbol, cplx) in members)
                    {
                        var cc = cplx?.CognitiveComplexity ?? 0;
                        seams.Add(cc > 0
                            ? $"module_member[cc={cc}]:{symbol.DisplayName}"
                            : $"module_member:{symbol.DisplayName}");
                    }
                }
                break;
        }

        if (fileSymbols is not null)
        {
            foreach (var symbol in fileSymbols)
            {
                foreach (var namedSeam in rules.CandidateSeams.NamedSymbolSeams)
                {
                    if (MatchesNamedSymbolSeam(symbol, namedSeam))
                    {
                        seams.Add($"{namedSeam.SeamPrefix}:{symbol.DisplayName}");
                    }
                }
            }
        }

        return seams.Distinct(StringComparer.Ordinal)
            .ToArray(); // preserve ranked order, don't re-sort alphabetically
    }

    private static bool MatchesNamedSymbolSeam(SymbolRecord symbol, NamedSymbolSeamRule rule)
    {
        if (rule.SymbolKinds.Count > 0 &&
            !rule.SymbolKinds.Any(kind => string.Equals(kind, symbol.Kind, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return rule.DisplayNames.Any(name =>
            string.Equals(name, symbol.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExplicitInterfaceImplementation(SymbolRecord symbol)
    {
        return symbol.Signature.Contains($"{symbol.DisplayName}(", StringComparison.Ordinal) &&
               symbol.Signature.Contains('.', StringComparison.Ordinal) &&
               !symbol.Signature.Contains($" {symbol.DisplayName}(", StringComparison.Ordinal);
    }

    // ── IO inference via call graph ──────────────────────────────────────────

    /// <summary>
    /// Infers whether a symbol performs IO by following its outgoing calls
    /// one level deep and checking if those callees live in files with
    /// boundary interactions.
    /// </summary>
    private static bool InfersIO(
        string symbolId,
        Dictionary<string, SymbolDependencyRecord[]> callsFrom,
        Dictionary<string, string> symbolToFile,
        Dictionary<string, BoundaryInteractionRecord[]> boundariesByFile)
    {
        if (!callsFrom.TryGetValue(symbolId, out var outgoing))
        {
            return false;
        }

        foreach (var dep in outgoing)
        {
            if (symbolToFile.TryGetValue(dep.ToSymbolId, out var calleeFileId) &&
                boundariesByFile.ContainsKey(calleeFileId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the set of boundary types a symbol touches via its outgoing call graph.
    /// </summary>
    private static IReadOnlyList<string> InferBoundaryTypes(
        string symbolId,
        Dictionary<string, SymbolDependencyRecord[]> callsFrom,
        Dictionary<string, string> symbolToFile,
        Dictionary<string, BoundaryInteractionRecord[]> boundariesByFile)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        if (!callsFrom.TryGetValue(symbolId, out var outgoing))
        {
            return [];
        }

        foreach (var dep in outgoing)
        {
            if (symbolToFile.TryGetValue(dep.ToSymbolId, out var calleeFileId) &&
                boundariesByFile.TryGetValue(calleeFileId, out var calleeBoundaries))
            {
                foreach (var b in calleeBoundaries)
                {
                    result.Add(b.BoundaryType);
                }
            }
        }

        return result.OrderBy(b => b, StringComparer.Ordinal).ToArray();
    }

    private static string RoleToSeamName(string role, RefactorDecisionRules rules)
    {
        if (rules.CandidateSeams.RoleSeams.TryGetValue(role, out var seamName))
        {
            return seamName;
        }

        return $"{role}{rules.CandidateSeams.RoleFallbackSuffix}";
    }
}
