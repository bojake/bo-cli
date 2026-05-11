namespace BO.Core.Indexing;

public sealed class ResponsibilityProfileDeriver
{
    public IReadOnlyList<ResponsibilityProfileRecord> Derive(
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<FileDependencyRecord> dependencies,
        IReadOnlyList<BoundaryInteractionRecord> boundaryInteractions,
        IReadOnlyList<EffectProfileRecord> effectProfiles,
        SemanticProfileRules? semanticRules = null)
    {
        var rules = semanticRules?.ResponsibilityRules ?? SemanticProfileRules.Default.ResponsibilityRules;
        var dependenciesByFile = dependencies
            .GroupBy(dependency => dependency.FromFileId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var boundariesByFile = boundaryInteractions
            .GroupBy(interaction => interaction.FileId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var effectsByFile = effectProfiles.ToDictionary(profile => profile.TargetId, StringComparer.Ordinal);
        var profiles = new List<ResponsibilityProfileRecord>();

        foreach (var file in files)
        {
            boundariesByFile.TryGetValue(file.Id, out var interactions);
            effectsByFile.TryGetValue(file.Id, out var effectProfile);
            dependenciesByFile.TryGetValue(file.Id, out var outgoingDependencies);

            var boundaryTypes = interactions?
                .Select(interaction => interaction.BoundaryType)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray() ?? [];

            var dependencyCategories = ResolveDependencyCategories(outgoingDependencies ?? []);
            var dominantResponsibilities = InferWorkflowRoles(boundaryTypes, effectProfile, rules)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            var sideEffectClassCount = effectProfile?.SideEffectClasses.Count ?? 0;
            var spreadScore = CalculateSpreadScore(
                boundaryTypes.Length,
                dependencyCategories.Count,
                sideEffectClassCount,
                dominantResponsibilities.Length);

            profiles.Add(new ResponsibilityProfileRecord(
                $"responsibility:{file.Id}",
                file.Id,
                "file",
                boundaryTypes.Length,
                dependencyCategories.Count,
                0,
                sideEffectClassCount,
                spreadScore,
                dominantResponsibilities,
                effectProfile?.Confidence ?? 0.75));
        }

        return profiles;
    }

    private static HashSet<string> ResolveDependencyCategories(IReadOnlyList<FileDependencyRecord> dependencies)
    {
        var categories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in dependencies)
        {
            categories.Add(dependency.IsRuntime ? "runtime" : "compile_time");
        }

        return categories;
    }

    private static IReadOnlySet<string> InferWorkflowRoles(
        IReadOnlyList<string> boundaryTypes,
        EffectProfileRecord? effectProfile,
        ResponsibilityDerivationRules rules)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rule in rules.WorkflowRoles)
        {
            if (rule.BoundaryTypes.Any(boundaryType => boundaryTypes.Contains(boundaryType, StringComparer.Ordinal)) ||
                rule.EffectFlags.Any(effectFlag => MatchesEffectFlag(effectFlag, effectProfile)))
            {
                roles.Add(rule.Role);
            }
        }

        if (boundaryTypes.Count >= rules.OrchestrationMinimumBoundaryTypes &&
            !string.IsNullOrWhiteSpace(rules.OrchestrationRole))
        {
            roles.Add(rules.OrchestrationRole);
        }

        return roles;
    }

    private static bool MatchesEffectFlag(string effectFlag, EffectProfileRecord? effectProfile)
    {
        if (effectProfile is null)
        {
            return false;
        }

        return effectFlag switch
        {
            "reads_state" => effectProfile.ReadsState,
            "writes_state" => effectProfile.WritesState,
            "emits_events" => effectProfile.EmitsEvents,
            "calls_external_service" => effectProfile.CallsExternalService,
            "mutates_input" => effectProfile.MutatesInput,
            "has_retry_logic" => effectProfile.HasRetryLogic,
            "has_transaction_logic" => effectProfile.HasTransactionLogic,
            "has_auth_logic" => effectProfile.HasAuthLogic,
            "has_validation_logic" => effectProfile.HasValidationLogic,
            "has_caching_logic" => effectProfile.HasCachingLogic,
            "has_logging_logic" => effectProfile.HasLoggingLogic,
            _ => false
        };
    }

    private static double CalculateSpreadScore(
        int boundaryTypeCount,
        int dependencyCategoryCount,
        int sideEffectClassCount,
        int workflowRoleCount)
    {
        var score =
            boundaryTypeCount * 12.0 +
            dependencyCategoryCount * 8.0 +
            sideEffectClassCount * 5.0 +
            workflowRoleCount * 15.0;

        return Math.Round(Math.Min(score, 100.0), 2, MidpointRounding.AwayFromZero);
    }
}
