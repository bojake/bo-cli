using System.Text.Json;

namespace BO.Core.Indexing;

public sealed record ArchitecturePlacementRules(
    string Version,
    InterfacePlacementRules InterfacePlacement)
{
    public static ArchitecturePlacementRules Default { get; } = new(
        "0.1.0",
        new InterfacePlacementRules(
            ["src"],
            "Extracted",
            ["Application"],
            ["Abstractions", "Interfaces", "Contracts"],
            ["Abstractions"],
            [".Application/Abstractions/", "Application/Abstractions/"],
            ["domain", "application"],
            ["infrastructure", "web", "worker"],
            [
                new ArchitectureLayerRule("domain", [".Domain"], ["Domain"]),
                new ArchitectureLayerRule("application", [".Application"], ["Application"]),
                new ArchitectureLayerRule("infrastructure", [".Infrastructure"], ["Infrastructure"]),
                new ArchitectureLayerRule("web", [".Web"], ["Web"]),
                new ArchitectureLayerRule("worker", [".Worker"], ["Worker"])
            ]));

    public static ArchitecturePlacementRules FromJson(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = document.RootElement;
        var defaults = Default;
        var defaultPlacement = defaults.InterfacePlacement;

        return new ArchitecturePlacementRules(
            GetString(root, defaults.Version, "version"),
            new InterfacePlacementRules(
                GetStringArray(root, defaultPlacement.SourceRootDirectoryNames, "interfacePlacement", "sourceRootDirectoryNames"),
                GetString(root, defaultPlacement.FallbackNamespace, "interfacePlacement", "fallbackNamespace"),
                GetStringArray(root, defaultPlacement.AbstractionLayerNames, "interfacePlacement", "abstractionLayerNames"),
                GetStringArray(root, defaultPlacement.AbstractionDirectoryNames, "interfacePlacement", "abstractionDirectoryNames"),
                GetStringArray(root, defaultPlacement.PreferredAbstractionDirectoryNames, "interfacePlacement", "preferredAbstractionDirectoryNames"),
                GetStringArray(root, defaultPlacement.PreferredExistingInterfacePathContains, "interfacePlacement", "preferredExistingInterfacePathContains"),
                GetStringArray(root, defaultPlacement.AllowedContractLayers, "interfacePlacement", "allowedContractLayers"),
                GetStringArray(root, defaultPlacement.DisallowedContractLayers, "interfacePlacement", "disallowedContractLayers"),
                GetLayerRules(root, defaultPlacement.Layers, "interfacePlacement", "layers")));
    }

    private static IReadOnlyList<ArchitectureLayerRule> GetLayerRules(
        JsonElement root,
        IReadOnlyList<ArchitectureLayerRule> fallback,
        params string[] path)
    {
        if (!TryGet(root, path, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        var rules = new List<ArchitectureLayerRule>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = GetString(item, string.Empty, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            rules.Add(new ArchitectureLayerRule(
                name,
                GetStringArray(item, [], "namespaceMarkers"),
                GetStringArray(item, [], "namespacePrefixes")));
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

public sealed record InterfacePlacementRules(
    IReadOnlyList<string> SourceRootDirectoryNames,
    string FallbackNamespace,
    IReadOnlyList<string> AbstractionLayerNames,
    IReadOnlyList<string> AbstractionDirectoryNames,
    IReadOnlyList<string> PreferredAbstractionDirectoryNames,
    IReadOnlyList<string> PreferredExistingInterfacePathContains,
    IReadOnlyList<string> AllowedContractLayers,
    IReadOnlyList<string> DisallowedContractLayers,
    IReadOnlyList<ArchitectureLayerRule> Layers);

public sealed record ArchitectureLayerRule(
    string Name,
    IReadOnlyList<string> NamespaceMarkers,
    IReadOnlyList<string> NamespacePrefixes);
