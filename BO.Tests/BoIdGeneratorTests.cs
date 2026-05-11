using BO.Core.Ids;

namespace BO.Tests;

public sealed class BoIdGeneratorTests
{
    [Fact]
    public void CreateRepoId_IsStableForSameWorkspace()
    {
        var generator = new BoIdGenerator();

        var first = generator.CreateRepoId(@"C:\core\gitroot\beyondordinary.ai");
        var second = generator.CreateRepoId(@"C:\core\gitroot\beyondordinary.ai");

        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateFileId_NormalizesRelativePath()
    {
        var generator = new BoIdGenerator();
        var repoId = generator.CreateRepoId(@"C:\repo");

        var fileId = generator.CreateFileId(repoId, @"C:\repo", @"C:\repo\src\engine\index.ts");

        Assert.Contains("src/engine/index.ts", fileId, StringComparison.Ordinal);
    }
}
