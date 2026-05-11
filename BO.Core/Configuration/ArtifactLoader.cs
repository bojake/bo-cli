using System.Text.Json;
using BO.Core.Indexing;

namespace BO.Core.Configuration;

public sealed class ArtifactLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PackageClassificationRules LoadPackageClassificationRules(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Package classification rules file was not found.", path);
        }

        var json = File.ReadAllText(path);
        var rules = JsonSerializer.Deserialize<PackageClassificationRules>(json, JsonOptions);

        if (rules is null)
        {
            throw new InvalidOperationException("Failed to deserialize package classification rules.");
        }

        return rules;
    }

    public BoConfiguration LoadBoConfiguration(string path)
    {
        if (!File.Exists(path))
        {
            return BoConfiguration.Empty;
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<BoConfiguration>(json, JsonOptions);
        if (config is null)
        {
            throw new InvalidOperationException("Failed to deserialize BO configuration.");
        }

        return config;
    }

    public RefactorScoringRules LoadRefactorScoringRules(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Refactor scoring configuration file was not found.", path);
        }

        return RefactorScoringRules.FromJson(File.ReadAllText(path));
    }

    public RefactorDecisionRules LoadRefactorDecisionRules(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Refactor decision rules file was not found.", path);
        }

        return RefactorDecisionRules.FromJson(File.ReadAllText(path));
    }

    public WorkspaceScanRules LoadWorkspaceScanRules(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Workspace scan rules file was not found.", path);
        }

        return WorkspaceScanRules.FromJson(File.ReadAllText(path));
    }

    public SemanticProfileRules LoadSemanticProfileRules(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Semantic profile rules file was not found.", path);
        }

        return SemanticProfileRules.FromJson(File.ReadAllText(path));
    }

    public ArchitecturePlacementRules LoadArchitecturePlacementRules(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Architecture placement rules file was not found.", path);
        }

        return ArchitecturePlacementRules.FromJson(File.ReadAllText(path));
    }
}
