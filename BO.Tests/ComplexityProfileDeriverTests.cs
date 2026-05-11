using BO.Core.Indexing;

namespace BO.Tests;

public sealed class ComplexityProfileDeriverTests
{
    [Fact]
    public void Derive_ComputesConservativeFileMetrics()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            var filePath = Path.Combine(workspaceRoot, "service.ts");
            File.WriteAllText(filePath,
                """
                export function run(flag: boolean) {
                  if (flag) {
                    return true;
                  }

                  return false;
                }
                """);

            var file = new FileRecord(
                "file:repo:test:service.ts",
                "repo:test",
                filePath,
                "service.ts",
                "typescript",
                false,
                false,
                "module:repo:test:root");

            var effectProfile = new EffectProfileRecord(
                "effect:file:repo:test:service.ts",
                file.Id,
                "file",
                ReadsState: true,
                WritesState: false,
                EmitsEvents: false,
                CallsExternalService: false,
                MutatesInput: false,
                HasRetryLogic: false,
                HasTransactionLogic: false,
                HasAuthLogic: false,
                HasValidationLogic: false,
                HasCachingLogic: false,
                HasLoggingLogic: false,
                ["read"],
                0.8);

            var dependency = new FileDependencyRecord(
                "edge:a:imports:b",
                file.Id,
                "file:repo:test:other.ts",
                "./other",
                false,
                true);

            var deriver = new ComplexityProfileDeriver();
            var profiles = deriver.Derive([file], Array.Empty<SymbolRecord>(), [dependency], [effectProfile]);

            Assert.Single(profiles);
            Assert.True(profiles[0].Loc >= 4);
            Assert.True(profiles[0].CyclomaticComplexity >= 2);
            Assert.True(profiles[0].BranchCount >= 1);
            Assert.Equal(1, profiles[0].FanOut);
            Assert.Equal(1, profiles[0].SideEffectCount);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static string CreateTempWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "bo-complexity-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
