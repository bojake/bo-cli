namespace BO.Core.Services.Bootstrap;

public sealed record BootstrapReport(
    string RepoId,
    string WorkspaceRoot,
    bool PackageRulesFound,
    string? PackageRulesVersion);
