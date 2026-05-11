namespace BO.Core.Indexing;

/// <summary>
/// Produces <see cref="SeamExtractionPlanRecord"/> for every seam identified by
/// the <see cref="RefactorDecisionDeriver"/>. Each plan contains:
///   - Which methods to extract from the god class
///   - Per-dependency classification (moves / stays_injected / already_shared / needs_promotion)
///   - Required service injections for the new class
///   - Proposed class name and estimated LOC reduction
///
/// This bridges the gap between "what to refactor" (seam identification) and
/// "how to refactor" (actionable extraction plan).
/// </summary>
public sealed class SeamExtractionPlanner
{
    // ── Dependency classifications ───────────────────────────────────────────
    private const string Moves = "moves";
    private const string StaysInjected = "stays_injected";
    private const string AlreadyShared = "already_shared";
    private const string NeedsPromotion = "needs_promotion";

    // ── Pattern types ────────────────────────────────────────────────────────
    private const string InterfaceDispatcher = "interface_dispatcher";

    private static readonly Dictionary<string, string> ClassNamePartSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["output"] = "writer"
    };

    private static readonly HashSet<string> TerminalClassNameRoles = new(StringComparer.Ordinal)
    {
        "Writer"
    };

    private readonly SeamDomainRules _domainRules;

    public SeamExtractionPlanner()
        : this(SeamDomainRules.LoadDefault())
    {
    }

    public SeamExtractionPlanner(SeamDomainRules domainRules)
    {
        _domainRules = domainRules;
    }

    public (IReadOnlyList<SeamExtractionPlanRecord> Plans, IReadOnlyList<ExtractionPatternRecord> Patterns) Plan(
        IReadOnlyList<RefactorDecisionRecord> decisions,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyList<SymbolDependencyRecord> symbolDependencies,
        IReadOnlyList<BoundaryInteractionRecord> boundaryInteractions,
        IReadOnlyList<ComplexityProfileRecord> complexityProfiles,
        IReadOnlyList<FileRecord> files,
        RefactorIntent? refactorIntent = null)
    {
        _ = refactorIntent ?? RefactorIntent.Default;
        var plans = new List<SeamExtractionPlanRecord>();

        // Pre-index lookups
        var symbolsById = symbols.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var symbolsByFile = symbols
            .GroupBy(s => s.FileId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var filesById = files.ToDictionary(f => f.Id, StringComparer.Ordinal);

        // Build intra-file call graph: who calls whom within the same file
        var callsFrom = symbolDependencies
            .Where(d => d.RelationType == "calls")
            .GroupBy(d => d.FromSymbolId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var calledBy = symbolDependencies
            .Where(d => d.RelationType == "calls")
            .GroupBy(d => d.ToSymbolId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        // Symbol complexity lookup
        var symbolComplexity = complexityProfiles
            .Where(p => p.TargetKind == "symbol")
            .ToDictionary(p => p.TargetId, StringComparer.Ordinal);

        // Boundary interactions by file
        var boundariesByFile = boundaryInteractions
            .GroupBy(b => b.FileId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        // Detect extraction patterns (interface + dispatcher pairs) across the codebase
        var extractionPatterns = DiscoverExtractionPatterns(symbols, symbolDependencies, symbolsByFile);

        foreach (var decision in decisions)
        {
            if (decision.CandidateSeams.Count == 0)
            {
                continue;
            }

            if (!symbolsByFile.TryGetValue(decision.TargetId, out var fileSymbols))
            {
                continue;
            }

            // Group seam candidates into boundary domains (e.g., "sftp_adapter", "smtp_adapter")
            var boundarySeams = GroupSeamsByBoundaryDomain(decision.CandidateSeams, fileSymbols);

            // Find the best extraction pattern for this file
            var pattern = FindBestPattern(decision.TargetId, extractionPatterns, symbols, symbolDependencies);

            foreach (var (seamName, seamMethods) in boundarySeams)
            {
                var filteredSeamMethods = FilterMethodsForCoordinatedExtraction(seamName, seamMethods, fileSymbols);
                if (filteredSeamMethods.Count == 0)
                {
                    continue;
                }

                var seamMethodIds = new HashSet<string>(
                    filteredSeamMethods.Select(m => m.Id), StringComparer.Ordinal);

                // ── Stage 2: Dependency Classification ───────────────────────
                var dependencies = ClassifyDependencies(
                    seamName, seamMethodIds, fileSymbols, callsFrom, calledBy,
                    symbolsById, extractionPatterns);

                // ── Compute service injections needed ────────────────────────
                var injections = dependencies
                    .Where(d => d.Classification == StaysInjected && d.InjectionInterface is not null)
                    .Select(d => d.InjectionInterface!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                // ── Identify records/DTOs to move ────────────────────────────
                var recordsToMove = IdentifyRecordsToMove(
                    seamMethodIds, fileSymbols, callsFrom, calledBy);

                // ── Estimate LOC reduction ───────────────────────────────────
                var locReduction = EstimateLocReduction(
                    filteredSeamMethods.ToList(), dependencies, recordsToMove, symbolComplexity);

                // ── Assess risk ──────────────────────────────────────────────
                var risk = AssessRisk(dependencies, filteredSeamMethods.Count);

                // ── Generate proposed class name ─────────────────────────────
                var className = GenerateClassName(seamName, seamMethods, fileSymbols, pattern);

                // ── Extract step types to route (for dispatcher patterns) ────
                var stepTypes = InferStepTypes(filteredSeamMethods.ToList(), fileSymbols, callsFrom);

                plans.Add(new SeamExtractionPlanRecord(
                    $"plan:extract:{decision.TargetId}:{seamName}",
                    decision.TargetId,
                    seamName,
                    decision.PivotType,
                    pattern?.Id,
                    className,
                    stepTypes,
                    filteredSeamMethods.Select(m => m.DisplayName).ToList(),
                    dependencies,
                    injections,
                    recordsToMove.Select(r => r.DisplayName).ToList(),
                    locReduction,
                    risk,
                    decision.Confidence * 0.9,
                    filteredSeamMethods.Select(m => m.Id).ToList()));
            }
        }

        return (plans, extractionPatterns);
    }

    // ── Pattern Discovery ────────────────────────────────────────────────────

    /// <summary>
    /// Discovers interface + dispatcher patterns in the codebase.
    /// Looks for interfaces that have multiple implementations registered via DI,
    /// and a class that consumes IEnumerable&lt;TInterface&gt; for routing.
    /// </summary>
    private static IReadOnlyList<ExtractionPatternRecord> DiscoverExtractionPatterns(
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyList<SymbolDependencyRecord> symbolDependencies,
        Dictionary<string, SymbolRecord[]> symbolsByFile)
    {
        var patterns = new List<ExtractionPatternRecord>();

        // Find interfaces that act as step executor contracts
        // Heuristic: interface with "Executor" or "Handler" in the name that has
        // multiple implementing classes
        var interfaces = symbols
            .Where(s => s.Kind == "interface" &&
                        (s.DisplayName.Contains("Executor", StringComparison.OrdinalIgnoreCase) ||
                         s.DisplayName.Contains("Handler", StringComparison.OrdinalIgnoreCase) ||
                         s.DisplayName.Contains("Strategy", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var implementsRelations = symbolDependencies
            .Where(d => d.RelationType is "implements" or "extends")
            .GroupBy(d => d.ToSymbolId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        foreach (var iface in interfaces)
        {
            if (!implementsRelations.TryGetValue(iface.Id, out var implementers))
            {
                continue;
            }

            // Need at least 2 implementations to indicate a strategy/command pattern
            if (implementers.Length < 2)
            {
                continue;
            }

            // Find dispatcher — a class that depends on IEnumerable<IInterface>
            // or directly on the interface via constructor injection
            var dispatchers = symbolDependencies
                .Where(d => d.ToSymbolId == iface.Id &&
                            d.RelationType is "uses" or "depends_on" or "calls")
                .Select(d => d.FromSymbolId)
                .Where(id => symbols.Any(s => s.Id == id &&
                    (s.DisplayName.Contains("Dispatcher", StringComparison.OrdinalIgnoreCase) ||
                     s.DisplayName.Contains("Router", StringComparison.OrdinalIgnoreCase) ||
                     s.DisplayName.Contains("Factory", StringComparison.OrdinalIgnoreCase))))
                .ToList();

            // Collect exemplar implementations
            var exemplars = implementers
                .Select(d => d.FromSymbolId)
                .Take(3)
                .ToList();

            // Identify shared helper classes in the same directory
            var ifaceFile = iface.FileId;
            var sharedHelpers = new List<string>();
            if (symbolsByFile.TryGetValue(ifaceFile, out var ifaceFileSymbols))
            {
                sharedHelpers.AddRange(ifaceFileSymbols
                    .Where(s => s.Kind == "class" &&
                                (s.DisplayName.Contains("Resolver", StringComparison.OrdinalIgnoreCase) ||
                                 s.DisplayName.Contains("Helper", StringComparison.OrdinalIgnoreCase) ||
                                 s.DisplayName.Contains("Utility", StringComparison.OrdinalIgnoreCase)))
                    .Select(s => s.Id));
            }

            patterns.Add(new ExtractionPatternRecord(
                $"pattern:{iface.Id}",
                ifaceFile,
                InterfaceDispatcher,
                iface.DisplayName,
                dispatchers.FirstOrDefault(),
                null, // dispatch site discovered separately
                "switch", // most common dispatch construct
                $"services.AddScoped<{iface.DisplayName}, {{ClassName}}>()",
                exemplars,
                sharedHelpers,
                implementers.Length >= 3 ? 0.95 : 0.75));
        }

        return patterns;
    }

    /// <summary>
    /// Finds the best extraction pattern for a given file by checking if the file's
    /// class already participates in a known pattern (e.g., the god class has a
    /// dispatcher injected into it).
    /// </summary>
    private static ExtractionPatternRecord? FindBestPattern(
        string targetFileId,
        IReadOnlyList<ExtractionPatternRecord> patterns,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyList<SymbolDependencyRecord> dependencies)
    {
        if (patterns.Count == 0)
        {
            return null;
        }

        // Check if any pattern's dispatcher is used by the god class
        var targetSymbolIds = new HashSet<string>(
            symbols.Where(s => s.FileId == targetFileId).Select(s => s.Id),
            StringComparer.Ordinal);

        foreach (var pattern in patterns.OrderByDescending(p => p.Confidence))
        {
            if (pattern.DispatcherSymbolId is null) continue;

            // Does the god class depend on the dispatcher?
            var usesDispatcher = dependencies.Any(d =>
                targetSymbolIds.Contains(d.FromSymbolId) &&
                d.ToSymbolId == pattern.DispatcherSymbolId);

            if (usesDispatcher)
            {
                return pattern;
            }
        }

        return null;
    }

    // ── Seam Grouping ────────────────────────────────────────────────────────

    /// <summary>
    /// Groups candidate seams by boundary domain and maps them to actual methods.
    /// For example, seams ["sftp_adapter", "sftp_adapter:SftpClient", "touches[sftp]:Upload"]
    /// get grouped into "sftp_adapter" with methods [Upload, Download, ...].
    /// </summary>
    private Dictionary<string, List<SymbolRecord>> GroupSeamsByBoundaryDomain(
        IReadOnlyList<string> candidateSeams,
        SymbolRecord[] fileSymbols)
    {
        var groups = new Dictionary<string, List<SymbolRecord>>(StringComparer.Ordinal);
        var methodsByName = fileSymbols
            .Where(s => s.Kind is "function" or "method")
            .GroupBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(candidate => IsExplicitInterfaceImplementation(candidate) ? 1 : 0)
                    .ThenBy(candidate => QualifiedNameDepth(candidate.QualifiedName))
                    .ThenBy(candidate => candidate.DeclarationLine)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var seam in candidateSeams)
        {
            // Parse seam format: "domain_adapter:MethodName" or "policy[b=X,cc=Y]:MethodName"
            var colonIdx = seam.IndexOf(':');
            if (colonIdx < 0) continue;

            var prefix = seam[..colonIdx];
            var methodName = seam[(colonIdx + 1)..];

            if (string.IsNullOrWhiteSpace(methodName)) continue;
            if (!methodsByName.TryGetValue(methodName, out var symbol)) continue;

            // Determine the grouping domain
            string domain;
            if (prefix.StartsWith("helper", StringComparison.OrdinalIgnoreCase))
            {
                domain = InferHelperDomain(symbol);
            }
            else if (prefix.StartsWith("policy", StringComparison.OrdinalIgnoreCase) ||
                prefix.StartsWith("io", StringComparison.OrdinalIgnoreCase))
            {
                // For policy/IO seams, bracket contents are metrics (b=X,cc=Y),
                // not domains. Use the richer support-domain classifier first so
                // high-branch helper logic doesn't collapse into core_orchestration.
                domain = InferPolicyOrIoDomain(symbol);
            }
            else
            {
                domain = ExtractDomainFromSeam(prefix);
            }

            if (string.IsNullOrWhiteSpace(domain)) continue;

            if (!groups.TryGetValue(domain, out var methods))
            {
                methods = new List<SymbolRecord>();
                groups[domain] = methods;
            }

            if (!methods.Any(m => m.Id == symbol.Id))
            {
                methods.Add(symbol);
            }
        }

        // Filter out groups with only 1 method and no clear domain (noise)
        return groups
            .Where(g => g.Value.Count > 0)
            .ToDictionary(g => g.Key, g => g.Value, StringComparer.Ordinal);
    }

    private static IReadOnlyList<SymbolRecord> FilterMethodsForCoordinatedExtraction(
        string seamName,
        IReadOnlyList<SymbolRecord> seamMethods,
        IReadOnlyList<SymbolRecord> fileSymbols)
    {
        IEnumerable<SymbolRecord> filtered = seamMethods;

        if (string.Equals(seamName, "core_orchestration", StringComparison.Ordinal))
        {
            filtered = filtered.Where(symbol => symbol.DisplayName is not "ExecuteAsync" and not "ExecuteStepCoreAsync");
        }

        if (string.Equals(seamName, "sql_transform", StringComparison.Ordinal))
        {
            var sourceOwnerName = InferPrimarySourceOwnerName(fileSymbols);
            if (!string.IsNullOrWhiteSpace(sourceOwnerName))
            {
                var directOwnerMethods = filtered
                    .Where(symbol => IsDirectSourceOwnerMethod(symbol, sourceOwnerName))
                    .ToArray();

                if (directOwnerMethods.Length > 0)
                {
                    filtered = directOwnerMethods;
                }
            }
        }

        return filtered.ToArray();
    }

    private static string? InferPrimarySourceOwnerName(IReadOnlyList<SymbolRecord> fileSymbols)
    {
        return fileSymbols
            .Where(symbol => symbol.Kind == "class")
            .OrderBy(symbol => QualifiedNameDepth(symbol.QualifiedName))
            .ThenBy(symbol => symbol.DeclarationLine)
            .Select(symbol => symbol.DisplayName)
            .FirstOrDefault();
    }

    private static bool IsDirectSourceOwnerMethod(SymbolRecord symbol, string sourceOwnerName)
    {
        var marker = $".{sourceOwnerName}.";
        var markerIndex = symbol.QualifiedName.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var remainder = symbol.QualifiedName[(markerIndex + marker.Length)..];
        return !remainder.Contains('.', StringComparison.Ordinal);
    }

    private string InferHelperDomain(SymbolRecord symbol)
    {
        var support = InferSupportDomain(symbol);
        if (support is not null)
        {
            return support;
        }

        var inferred = InferDomainFromMethodName(symbol.DisplayName);
        return string.Equals(inferred, _domainRules.DefaultMethodDomain, StringComparison.Ordinal)
            ? _domainRules.CoreHelperFallbackDomain
            : inferred;
    }

    private string InferPolicyOrIoDomain(SymbolRecord symbol)
    {
        var support = InferSupportDomain(symbol);
        if (support is not null)
        {
            return support;
        }

        return InferDomainFromMethodName(symbol.DisplayName);
    }

    private string? InferSupportDomain(SymbolRecord symbol)
    {
        var methodName = symbol.DisplayName;
        var qualifiedName = symbol.QualifiedName;

        foreach (var rule in _domainRules.SupportDomains)
        {
            if (MatchesSupportDomainRule(rule, methodName, qualifiedName))
            {
                return rule.Domain;
            }
        }

        return null;
    }

    private static bool MatchesSupportDomainRule(
        SeamSupportDomainRule rule,
        string methodName,
        string qualifiedName)
    {
        return rule.ExactMethodNames.Any(candidate => methodName.Equals(candidate, StringComparison.Ordinal)) ||
               rule.MethodContains.Any(candidate => methodName.Contains(candidate, StringComparison.OrdinalIgnoreCase)) ||
               rule.QualifiedNameContains.Any(candidate => qualifiedName.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Infers a boundary domain from a method name using naming conventions.
    /// E.g., "ExecuteResourceFtpUploadStepAsync" → "ftp"
    ///       "ExecuteSqlMoveDataStepAsync" → "sql"
    ///       "ExecuteFileSplitPdfStepAsync" → "pdf"
    /// </summary>
    private string InferDomainFromMethodName(string methodName)
    {
        foreach (var rule in _domainRules.MethodDomains)
        {
            if (rule.Keywords.Any(keyword => methodName.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return rule.Domain;
            }
        }

        return _domainRules.DefaultMethodDomain;
    }

    private static int QualifiedNameDepth(string qualifiedName)
    {
        return string.IsNullOrWhiteSpace(qualifiedName)
            ? int.MaxValue
            : qualifiedName.Count(ch => ch == '.');
    }

    private static bool IsExplicitInterfaceImplementation(SymbolRecord symbol)
    {
        return symbol.Signature.Contains($"{symbol.DisplayName}(", StringComparison.Ordinal) &&
               symbol.Signature.Contains('.', StringComparison.Ordinal) &&
               !symbol.Signature.Contains($" {symbol.DisplayName}(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts the boundary domain from seam prefixes like "sftp_adapter",
    /// "touches[sftp,ssh]", "boundary_cluster:domain".
    /// </summary>
    private static string ExtractDomainFromSeam(string prefix)
    {
        // "sftp_adapter" → "sftp"
        if (prefix.EndsWith("_adapter", StringComparison.Ordinal))
        {
            return prefix[..^"_adapter".Length];
        }

        // "touches[sftp,ssh]" → "sftp" (primary)
        // "boundary_cluster" → "boundary_cluster"
        var bracketStart = prefix.IndexOf('[');
        if (bracketStart >= 0)
        {
            var bracketEnd = prefix.IndexOf(']', bracketStart);
            if (bracketEnd > bracketStart)
            {
                var inner = prefix[(bracketStart + 1)..bracketEnd];
                var firstDomain = inner.Split(',')[0].Trim();
                // Skip if the bracket content is metrics (e.g., "b=66")
                if (!firstDomain.Contains('='))
                {
                    return firstDomain;
                }
            }
        }

        return prefix;
    }

    // ── Dependency Classification (P0 — the hardest step) ────────────────────

    /// <summary>
    /// For each method called by the seam methods, classifies whether it:
    ///   - moves: only called by seam methods, should move to new class
    ///   - stays_injected: called by seam + other methods, accessed via interface
    ///   - already_shared: already in a shared helper class
    ///   - needs_promotion: private helper used by seam + others, must become public
    /// </summary>
    private static IReadOnlyList<DependencyClassificationRecord> ClassifyDependencies(
        string seamName,
        HashSet<string> seamMethodIds,
        SymbolRecord[] fileSymbols,
        Dictionary<string, SymbolDependencyRecord[]> callsFrom,
        Dictionary<string, SymbolDependencyRecord[]> calledBy,
        Dictionary<string, SymbolRecord> symbolsById,
        IReadOnlyList<ExtractionPatternRecord> patterns)
    {
        var classifications = new List<DependencyClassificationRecord>();
        var allFileSymbolIds = new HashSet<string>(
            fileSymbols.Select(s => s.Id), StringComparer.Ordinal);

        // Collect shared helper symbol IDs from extraction patterns
        var sharedHelperIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pattern in patterns)
        {
            foreach (var helperId in pattern.SharedHelperSymbolIds)
            {
                sharedHelperIds.Add(helperId);
            }
        }

        // For each seam method, find all outgoing calls
        var allCallees = new HashSet<string>(StringComparer.Ordinal);
        foreach (var methodId in seamMethodIds)
        {
            if (callsFrom.TryGetValue(methodId, out var outgoing))
            {
                foreach (var dep in outgoing)
                {
                    // Only classify intra-file dependencies
                    if (allFileSymbolIds.Contains(dep.ToSymbolId) &&
                        !seamMethodIds.Contains(dep.ToSymbolId))
                    {
                        allCallees.Add(dep.ToSymbolId);
                    }
                }
            }
        }

        // Walk transitive helper calls (depth 2) to catch helpers-of-helpers
        var transitiveCallees = new HashSet<string>(allCallees, StringComparer.Ordinal);
        foreach (var calleeId in allCallees)
        {
            if (callsFrom.TryGetValue(calleeId, out var subCalls))
            {
                foreach (var dep in subCalls)
                {
                    if (allFileSymbolIds.Contains(dep.ToSymbolId) &&
                        !seamMethodIds.Contains(dep.ToSymbolId))
                    {
                        transitiveCallees.Add(dep.ToSymbolId);
                    }
                }
            }
        }

        foreach (var calleeId in transitiveCallees)
        {
            if (!symbolsById.TryGetValue(calleeId, out var callee))
            {
                continue;
            }

            // Count callers inside vs. outside the seam
            var callersInSeam = 0;
            var callersOutsideSeam = 0;

            if (calledBy.TryGetValue(calleeId, out var callers))
            {
                foreach (var caller in callers)
                {
                    if (seamMethodIds.Contains(caller.FromSymbolId) ||
                        transitiveCallees.Contains(caller.FromSymbolId))
                    {
                        callersInSeam++;
                    }
                    else if (allFileSymbolIds.Contains(caller.FromSymbolId))
                    {
                        callersOutsideSeam++;
                    }
                }
            }

            // Classify
            string classification;
            string reason;
            string? promotionTarget = null;
            string? injectionInterface = null;

            if (sharedHelperIds.Contains(calleeId))
            {
                classification = AlreadyShared;
                reason = "Already in shared helper class";
            }
            else if (callersOutsideSeam == 0)
            {
                classification = Moves;
                reason = $"Only called by methods in this seam ({callersInSeam} callers)";
            }
            else if (callee.IsExported || callee.Kind is "interface" or "class")
            {
                classification = StaysInjected;
                reason = $"Called by {callersOutsideSeam} methods outside seam";
                injectionInterface = InferInjectionInterface(callee, symbolsById);
            }
            else
            {
                // Private helper used by both seam and non-seam methods
                classification = NeedsPromotion;
                reason = $"Private helper used by {callersInSeam} seam + {callersOutsideSeam} non-seam callers";
                promotionTarget = "shared_helper"; // will be resolved to actual helper class later
            }

            classifications.Add(new DependencyClassificationRecord(
                $"dep:{seamName}:{calleeId}",
                seamName,
                calleeId,
                callee.DisplayName,
                classification,
                reason,
                callersInSeam,
                callersOutsideSeam,
                promotionTarget,
                injectionInterface));
        }

        return classifications;
    }

    private static string? InferInjectionInterface(
        SymbolRecord symbol,
        Dictionary<string, SymbolRecord> symbolsById)
    {
        // Simple heuristic: if the method name suggests a service pattern,
        // look for an interface with a matching name
        var name = symbol.DisplayName;
        if (name.StartsWith("Get", StringComparison.Ordinal) ||
            name.StartsWith("Enforce", StringComparison.Ordinal) ||
            name.StartsWith("Validate", StringComparison.Ordinal))
        {
            // Look for "I" + containing class name pattern
            // For a method on RunExecutionService, the interface is IRunExecutionMonitoringSupport
            return null; // Will be resolved during scaffold generation
        }

        return null;
    }

    // ── Records Analysis ─────────────────────────────────────────────────────

    /// <summary>
    /// Identifies records/DTOs/value types that are only used by seam methods
    /// and should move to the new class.
    /// </summary>
    private static IReadOnlyList<SymbolRecord> IdentifyRecordsToMove(
        HashSet<string> seamMethodIds,
        SymbolRecord[] fileSymbols,
        Dictionary<string, SymbolDependencyRecord[]> callsFrom,
        Dictionary<string, SymbolDependencyRecord[]> calledBy)
    {
        var records = fileSymbols
            .Where(s => s.Kind is "record" or "struct" or "type_alias")
            .ToArray();

        var result = new List<SymbolRecord>();
        foreach (var record in records)
        {
            // Check if this record is referenced only by seam methods
            if (!calledBy.TryGetValue(record.Id, out var users) || users.Length == 0)
            {
                continue;
            }

            var onlySeamUsers = users.All(u =>
                seamMethodIds.Contains(u.FromSymbolId));

            if (onlySeamUsers)
            {
                result.Add(record);
            }
        }

        return result;
    }

    // ── Estimation ───────────────────────────────────────────────────────────

    private static int EstimateLocReduction(
        List<SymbolRecord> seamMethods,
        IReadOnlyList<DependencyClassificationRecord> dependencies,
        IReadOnlyList<SymbolRecord> recordsToMove,
        Dictionary<string, ComplexityProfileRecord> symbolComplexity)
    {
        var loc = 0;

        // LOC from methods being extracted
        foreach (var method in seamMethods)
        {
            loc += symbolComplexity.TryGetValue(method.Id, out var cplx)
                ? cplx.Loc
                : 25; // conservative default
        }

        // LOC from helpers that move
        foreach (var dep in dependencies.Where(d => d.Classification == Moves))
        {
            loc += symbolComplexity.TryGetValue(dep.SymbolId, out var cplx)
                ? cplx.Loc
                : 15;
        }

        // LOC from records that move
        loc += recordsToMove.Count * 8; // average record is ~8 lines

        return loc;
    }

    private static string AssessRisk(
        IReadOnlyList<DependencyClassificationRecord> dependencies,
        int methodCount)
    {
        var needsPromotionCount = dependencies.Count(d => d.Classification == NeedsPromotion);
        var circularRisk = dependencies.Any(d => d.CallersInSeam > 0 && d.CallersOutsideSeam > 0
            && d.Classification == StaysInjected);

        if (needsPromotionCount > 3 || circularRisk)
        {
            return "high";
        }

        if (needsPromotionCount > 0 || methodCount > 8)
        {
            return "medium";
        }

        return "low";
    }

    // ── Name Generation ──────────────────────────────────────────────────────

    private static string GenerateClassName(
        string seamName,
        IReadOnlyList<SymbolRecord> seamMethods,
        SymbolRecord[] fileSymbols,
        ExtractionPatternRecord? pattern)
    {
        var parts = seamName.Split('_', '-')
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(NormalizeClassNamePart)
            .Select(ToPascalCase);

        var seamNamePart = string.Join("", parts);
        var hostClassName = InferHostClassName(seamMethods, fileSymbols);

        var hostStem = hostClassName is null
            ? string.Empty
            : StripKnownSuffix(hostClassName);

        var suffix = HasTerminalClassNameRole(seamNamePart)
            ? string.Empty
            : InferSuffix(hostClassName, seamMethods, pattern);
        var prefix = string.IsNullOrWhiteSpace(hostStem) ? string.Empty : hostStem;

        return $"{prefix}{seamNamePart}{suffix}";
    }

    private static string? InferHostClassName(
        IReadOnlyList<SymbolRecord> seamMethods,
        SymbolRecord[] fileSymbols)
    {
        var classNames = fileSymbols
            .Where(symbol => symbol.Kind == "class")
            .Select(symbol => symbol.DisplayName)
            .ToHashSet(StringComparer.Ordinal);

        return seamMethods
            .Select(GetContainingTypeName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => !string.Equals(name, "Program", StringComparison.Ordinal))
            .Where(name => classNames.Contains(name!))
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string? GetContainingTypeName(SymbolRecord symbol)
    {
        var suffix = $".{symbol.DisplayName}";
        if (!symbol.QualifiedName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        var ownerName = symbol.QualifiedName[..^suffix.Length]
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        return ownerName;
    }

    private static string NormalizeClassNamePart(string part)
    {
        return ClassNamePartSynonyms.TryGetValue(part, out var synonym)
            ? synonym
            : part;
    }

    private static string ToPascalCase(string part)
    {
        return string.IsNullOrEmpty(part)
            ? string.Empty
            : char.ToUpperInvariant(part[0]) + part[1..];
    }

    private static bool HasTerminalClassNameRole(string seamNamePart)
    {
        return TerminalClassNameRoles.Any(role => seamNamePart.EndsWith(role, StringComparison.Ordinal));
    }

    private static string StripKnownSuffix(string className)
    {
        foreach (var suffix in new[] { "Service", "Worker", "Executor", "Handler", "Controller" })
        {
            if (className.EndsWith(suffix, StringComparison.Ordinal) &&
                className.Length > suffix.Length)
            {
                return className[..^suffix.Length];
            }
        }

        return className;
    }

    private static string InferSuffix(
        string? hostClassName,
        IReadOnlyList<SymbolRecord> seamMethods,
        ExtractionPatternRecord? pattern)
    {
        if (pattern is not null || seamMethods.Any(m => m.DisplayName.StartsWith("Execute", StringComparison.Ordinal)))
        {
            return "StepExecutor";
        }

        if (!string.IsNullOrWhiteSpace(hostClassName))
        {
            foreach (var suffix in new[] { "Service", "Worker", "Handler", "Controller", "Executor" })
            {
                if (hostClassName.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return suffix;
                }
            }
        }

        return "Service";
    }

    /// <summary>
    /// Infers step type enum values that should be routed to the new executor.
    /// For dispatcher patterns, these are the switch case values in the dispatch site.
    /// </summary>
    private static IReadOnlyList<string> InferStepTypes(
        List<SymbolRecord> seamMethods,
        SymbolRecord[] fileSymbols,
        Dictionary<string, SymbolDependencyRecord[]> callsFrom)
    {
        // Heuristic: method name "ExecuteResource{Type}StepAsync" → step type "Resource{Type}"
        var stepTypes = new List<string>();
        foreach (var method in seamMethods)
        {
            var name = method.DisplayName;
            if (name.StartsWith("Execute", StringComparison.Ordinal) &&
                name.EndsWith("StepAsync", StringComparison.Ordinal))
            {
                var stepType = name["Execute".Length..^"StepAsync".Length];
                stepTypes.Add(stepType);
            }
            else if (name.StartsWith("Execute", StringComparison.Ordinal) &&
                     name.EndsWith("Step", StringComparison.Ordinal))
            {
                var stepType = name["Execute".Length..^"Step".Length];
                stepTypes.Add(stepType);
            }
        }

        return stepTypes;
    }
}
