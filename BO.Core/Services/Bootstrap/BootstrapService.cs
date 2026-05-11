using BO.Core.Configuration;
using BO.Core.Ids;

namespace BO.Core.Services.Bootstrap;

public sealed class BootstrapService
{
    private readonly ArtifactLoader _artifactLoader;
    private readonly BoIdGenerator _idGenerator;

    public BootstrapService(ArtifactLoader artifactLoader, BoIdGenerator idGenerator)
    {
        _artifactLoader = artifactLoader;
        _idGenerator = idGenerator;
    }

    public BootstrapReport Initialize(string workspaceRoot)
    {
        var paths = ArtifactPathResolver.Resolve(workspaceRoot);
        var packageRulesFound = File.Exists(paths.PackageClassificationRulesPath);
        var version = packageRulesFound
            ? _artifactLoader.LoadPackageClassificationRules(paths.PackageClassificationRulesPath).Version
            : null;

        var boDirectory = Path.Combine(paths.WorkspaceRoot, ".bo");
        Directory.CreateDirectory(boDirectory);

        return new BootstrapReport(
            _idGenerator.CreateRepoId(paths.WorkspaceRoot),
            paths.WorkspaceRoot,
            packageRulesFound,
            version);
    }
}
