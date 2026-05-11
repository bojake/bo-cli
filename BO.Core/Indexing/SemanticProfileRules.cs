using System.Text.Json;

namespace BO.Core.Indexing;

public sealed record SemanticProfileRules(
    string Version,
    EffectDerivationRules EffectRules,
    ResponsibilityDerivationRules ResponsibilityRules)
{
    public static SemanticProfileRules Default { get; } = new(
        "0.1.0",
        new EffectDerivationRules(
            ["read", "cache_get", "authenticate", "authorize"],
            ["write", "delete", "cache_put", "serialize"],
            ["publish"],
            ["http", "queue", "auth", "email", "search", "feature_flag"],
            ["auth"],
            ["cache"],
            ["logging"]),
        new ResponsibilityDerivationRules(
            [
                new WorkflowRoleRule("persistence", ["db"], []),
                new WorkflowRoleRule("transport", ["http"], []),
                new WorkflowRoleRule("security", ["auth"], ["has_auth_logic"]),
                new WorkflowRoleRule("caching", ["cache"], ["has_caching_logic"]),
                new WorkflowRoleRule("auditing", ["queue"], ["emits_events"])
            ],
            "orchestration",
            2));

    public static SemanticProfileRules FromJson(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = document.RootElement;
        var defaults = Default;

        return new SemanticProfileRules(
            GetString(root, defaults.Version, "version"),
            new EffectDerivationRules(
                GetStringArray(root, defaults.EffectRules.ReadsStateOperationTypes, "effectRules", "readsStateOperationTypes"),
                GetStringArray(root, defaults.EffectRules.WritesStateOperationTypes, "effectRules", "writesStateOperationTypes"),
                GetStringArray(root, defaults.EffectRules.EmitsEventsOperationTypes, "effectRules", "emitsEventsOperationTypes"),
                GetStringArray(root, defaults.EffectRules.CallsExternalServiceBoundaryTypes, "effectRules", "callsExternalServiceBoundaryTypes"),
                GetStringArray(root, defaults.EffectRules.AuthBoundaryTypes, "effectRules", "authBoundaryTypes"),
                GetStringArray(root, defaults.EffectRules.CachingBoundaryTypes, "effectRules", "cachingBoundaryTypes"),
                GetStringArray(root, defaults.EffectRules.LoggingBoundaryTypes, "effectRules", "loggingBoundaryTypes")),
            new ResponsibilityDerivationRules(
                GetWorkflowRoleRules(root, defaults.ResponsibilityRules.WorkflowRoles, "responsibilityRules", "workflowRoles"),
                GetString(root, defaults.ResponsibilityRules.OrchestrationRole, "responsibilityRules", "orchestrationRole"),
                GetInt(root, defaults.ResponsibilityRules.OrchestrationMinimumBoundaryTypes, "responsibilityRules", "orchestrationMinimumBoundaryTypes")));
    }

    private static IReadOnlyList<WorkflowRoleRule> GetWorkflowRoleRules(
        JsonElement root,
        IReadOnlyList<WorkflowRoleRule> fallback,
        params string[] path)
    {
        if (!TryGet(root, path, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        var rules = new List<WorkflowRoleRule>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var role = GetString(item, string.Empty, "role");
            if (string.IsNullOrWhiteSpace(role))
            {
                continue;
            }

            rules.Add(new WorkflowRoleRule(
                role,
                GetStringArray(item, [], "boundaryTypes"),
                GetStringArray(item, [], "effectFlags")));
        }

        return rules;
    }

    private static IReadOnlyList<string> GetStringArray(
        JsonElement root,
        IReadOnlyList<string> fallback,
        params string[] path)
    {
        if (!TryGet(root, path, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        return value.EnumerateArray()
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

public sealed record EffectDerivationRules(
    IReadOnlyList<string> ReadsStateOperationTypes,
    IReadOnlyList<string> WritesStateOperationTypes,
    IReadOnlyList<string> EmitsEventsOperationTypes,
    IReadOnlyList<string> CallsExternalServiceBoundaryTypes,
    IReadOnlyList<string> AuthBoundaryTypes,
    IReadOnlyList<string> CachingBoundaryTypes,
    IReadOnlyList<string> LoggingBoundaryTypes);

public sealed record ResponsibilityDerivationRules(
    IReadOnlyList<WorkflowRoleRule> WorkflowRoles,
    string OrchestrationRole,
    int OrchestrationMinimumBoundaryTypes);

public sealed record WorkflowRoleRule(
    string Role,
    IReadOnlyList<string> BoundaryTypes,
    IReadOnlyList<string> EffectFlags);
