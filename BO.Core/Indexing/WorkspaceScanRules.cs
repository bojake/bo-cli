using System.Text.Json;

namespace BO.Core.Indexing;

public sealed record WorkspaceScanRules(
    string Version,
    IReadOnlyList<string> ExcludedDirectories,
    IReadOnlyDictionary<string, WorkspaceModuleRule> ModuleRules,
    IReadOnlyList<WorkspacePathRule> TestPathRules,
    IReadOnlyList<WorkspacePathRule> GeneratedPathRules)
{
    public static WorkspaceScanRules Default { get; } = new(
        "0.1.0",
        [
            ".git",
            "coverage",
            "node_modules",
            "dist",
            "build",
            "bin",
            "obj",
            ".vs",
            "target",
            ".gradle",
            "cmake-build-debug",
            "cmake-build-release"
        ],
        new Dictionary<string, WorkspaceModuleRule>(StringComparer.Ordinal)
        {
            ["dotnet"] = new("first_path_segment", "root"),
            ["default"] = new("directory", "root")
        },
        [
            new WorkspacePathRule(
                "dotnet",
                ["/Tests/", ".Tests/", ".Test/"],
                ["Tests.cs", "Test.cs"]),
            new WorkspacePathRule(
                "default",
                ["/__tests__/"],
                [".test.ts", ".spec.ts", ".test.js", ".spec.js"])
        ],
        [
            new WorkspacePathRule(
                "dotnet",
                ["/obj/"],
                [".g.cs", ".designer.cs", ".generated.cs"]),
            new WorkspacePathRule(
                "default",
                ["/generated/"],
                [".generated.ts", ".generated.js", ".d.ts"])
        ]);

    public static WorkspaceScanRules FromJson(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = document.RootElement;
        var defaults = Default;

        return new WorkspaceScanRules(
            GetString(root, defaults.Version, "version"),
            GetStringArray(root, defaults.ExcludedDirectories, "excludedDirectories"),
            GetModuleRules(root, defaults.ModuleRules, "moduleRules"),
            GetPathRules(root, defaults.TestPathRules, "testPathRules"),
            GetPathRules(root, defaults.GeneratedPathRules, "generatedPathRules"));
    }

    private static IReadOnlyDictionary<string, WorkspaceModuleRule> GetModuleRules(
        JsonElement root,
        IReadOnlyDictionary<string, WorkspaceModuleRule> fallback,
        params string[] path)
    {
        if (!TryGet(root, path, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        var rules = new Dictionary<string, WorkspaceModuleRule>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            rules[property.Name] = new WorkspaceModuleRule(
                GetString(property.Value, "directory", "mode"),
                GetString(property.Value, "root", "rootModuleName"));
        }

        return rules;
    }

    private static IReadOnlyList<WorkspacePathRule> GetPathRules(
        JsonElement root,
        IReadOnlyList<WorkspacePathRule> fallback,
        params string[] path)
    {
        if (!TryGet(root, path, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        var rules = new List<WorkspacePathRule>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var languageFamily = GetString(item, string.Empty, "languageFamily");
            if (string.IsNullOrWhiteSpace(languageFamily))
            {
                continue;
            }

            rules.Add(new WorkspacePathRule(
                languageFamily,
                GetStringArray(item, [], "contains"),
                GetStringArray(item, [], "endsWith")));
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

public sealed record WorkspaceModuleRule(string Mode, string RootModuleName);

public sealed record WorkspacePathRule(
    string LanguageFamily,
    IReadOnlyList<string> Contains,
    IReadOnlyList<string> EndsWith);
