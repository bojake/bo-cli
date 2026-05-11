using System.IO;

namespace BO.Core.Configuration;

public sealed record ArtifactPaths(
    string WorkspaceRoot,
    string RepoConfigurationPath,
    string PackageClassificationRulesPath,
    string ScoringConfigPath,
    string RefactorDecisionRulesPath,
    string WorkspaceScanRulesPath,
    string SemanticProfileRulesPath,
    string ArchitecturePlacementRulesPath,
    string SchemaPath);

public static class ArtifactPathResolver
{
    public static ArtifactPaths Resolve(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);

        return new ArtifactPaths(
            fullRoot,
            Path.Combine(fullRoot, ".bo", "config.json"),
            Path.Combine(fullRoot, "package_classification_rules.json"),
            Path.Combine(fullRoot, "scoring_config.json"),
            Path.Combine(fullRoot, "refactor_decision_rules.json"),
            Path.Combine(fullRoot, "workspace_scan_rules.json"),
            Path.Combine(fullRoot, "semantic_profile_rules.json"),
            Path.Combine(fullRoot, "architecture_placement_rules.json"),
            Path.Combine(fullRoot, "bo_schema.json"));
    }
}
