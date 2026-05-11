namespace BO.Core.Indexing;

public sealed class EffectProfileDeriver
{
    public IReadOnlyList<EffectProfileRecord> Derive(
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<BoundaryInteractionRecord> boundaryInteractions,
        SemanticProfileRules? semanticRules = null)
    {
        var rules = semanticRules?.EffectRules ?? SemanticProfileRules.Default.EffectRules;
        var interactionsByFile = boundaryInteractions
            .GroupBy(interaction => interaction.FileId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var profiles = new List<EffectProfileRecord>();

        foreach (var file in files)
        {
            if (!interactionsByFile.TryGetValue(file.Id, out var interactions) || interactions.Length == 0)
            {
                continue;
            }

            var boundaryTypes = interactions
                .Select(interaction => interaction.BoundaryType)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            var operationTypes = interactions
                .Select(interaction => interaction.OperationType)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            var sideEffectClasses = boundaryTypes
                .Concat(operationTypes)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            profiles.Add(new EffectProfileRecord(
                $"effect:{file.Id}",
                file.Id,
                "file",
                ReadsState(interactions, rules),
                WritesState(interactions, rules),
                EmitsEvents(interactions, rules),
                CallsExternalService(interactions, rules),
                MutatesInput: false,
                HasRetryLogic: false,
                HasTransactionLogic: false,
                HasAuthLogic: MatchesBoundaryType(interactions, rules.AuthBoundaryTypes),
                HasValidationLogic: false,
                HasCachingLogic: MatchesBoundaryType(interactions, rules.CachingBoundaryTypes),
                HasLoggingLogic: MatchesBoundaryType(interactions, rules.LoggingBoundaryTypes),
                sideEffectClasses,
                Confidence(interactions)));
        }

        return profiles;
    }

    private static bool ReadsState(
        IReadOnlyList<BoundaryInteractionRecord> interactions,
        EffectDerivationRules rules) =>
        MatchesOperationType(interactions, rules.ReadsStateOperationTypes);

    private static bool WritesState(
        IReadOnlyList<BoundaryInteractionRecord> interactions,
        EffectDerivationRules rules) =>
        MatchesOperationType(interactions, rules.WritesStateOperationTypes);

    private static bool EmitsEvents(
        IReadOnlyList<BoundaryInteractionRecord> interactions,
        EffectDerivationRules rules) =>
        MatchesOperationType(interactions, rules.EmitsEventsOperationTypes);

    private static bool CallsExternalService(
        IReadOnlyList<BoundaryInteractionRecord> interactions,
        EffectDerivationRules rules) =>
        MatchesBoundaryType(interactions, rules.CallsExternalServiceBoundaryTypes);

    private static bool MatchesOperationType(
        IReadOnlyList<BoundaryInteractionRecord> interactions,
        IReadOnlyList<string> operationTypes) =>
        interactions.Any(interaction => operationTypes.Contains(interaction.OperationType, StringComparer.Ordinal));

    private static bool MatchesBoundaryType(
        IReadOnlyList<BoundaryInteractionRecord> interactions,
        IReadOnlyList<string> boundaryTypes) =>
        interactions.Any(interaction => boundaryTypes.Contains(interaction.BoundaryType, StringComparer.Ordinal));

    private static double Confidence(IReadOnlyList<BoundaryInteractionRecord> interactions) =>
        Math.Round(interactions.Average(interaction => interaction.Confidence), 2, MidpointRounding.AwayFromZero);
}
