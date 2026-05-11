using BO.Core.Ids;
using BO.Core.Indexing;

namespace BO.Tests;

public sealed class ContractExtractorTests
{
    [Fact]
    public void Extract_DerivesContractsForCallableSymbols()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            var filePath = Path.Combine(workspaceRoot, "src", "contracts.ts");
            File.WriteAllText(filePath,
                """
                export async function loadUser<T extends User>(id: string, fallback?: T | null): Promise<T | null> {
                  if (!id) {
                    throw new Error("missing id");
                  }

                  return fallback ?? null;
                }

                export class UserService {
                  constructor(client: unknown) {}
                }

                export const mapUser = (name: string): Result<User, Error> => createResult(name);
                """);

            var idGenerator = new BoIdGenerator();
            var repoId = idGenerator.CreateRepoId(workspaceRoot);
            var file = new FileRecord(
                idGenerator.CreateFileId(repoId, workspaceRoot, filePath),
                repoId,
                filePath,
                "src/contracts.ts",
                "typescript",
                false,
                false,
                idGenerator.CreateModuleId(repoId, "src"));

            var symbolResult = new SourceSymbolExtractor(idGenerator).Extract([file]);
            var contracts = new ContractExtractor().Extract([file], symbolResult.Symbols);

            var loadUserContract = Assert.Single(
                contracts,
                contract => symbolResult.Symbols.Single(symbol => symbol.Id == contract.SymbolId).QualifiedName == "loadUser");
            Assert.Equal(["string", "T | null"], loadUserContract.InputTypes);
            Assert.Equal(["Promise<T | null>"], loadUserContract.OutputTypes);
            Assert.Equal(["T extends User"], loadUserContract.GenericConstraints);
            Assert.Equal(["throw"], loadUserContract.ThrowsOrErrorModes);
            Assert.True(loadUserContract.Nullability.AcceptsNullableInput);
            Assert.True(loadUserContract.Nullability.ReturnsNullableOutput);
            Assert.True(loadUserContract.Nullability.HasOptionalParameters);
            Assert.Equal("async", loadUserContract.AsyncMode);

            var constructorContract = Assert.Single(
                contracts,
                contract => symbolResult.Symbols.Single(symbol => symbol.Id == contract.SymbolId).QualifiedName == "UserService.constructor");
            Assert.Equal(["unknown"], constructorContract.InputTypes);
            Assert.Equal(["UserService"], constructorContract.OutputTypes);
            Assert.Equal("sync", constructorContract.AsyncMode);

            var mapUserContract = Assert.Single(
                contracts,
                contract => symbolResult.Symbols.Single(symbol => symbol.Id == contract.SymbolId).QualifiedName == "mapUser");
            Assert.Equal(["string"], mapUserContract.InputTypes);
            Assert.Equal(["Result<User, Error>"], mapUserContract.OutputTypes);
            Assert.Contains("result_return", mapUserContract.ThrowsOrErrorModes);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public void Extract_DerivesContractsForMultiLineCSharpMethodSignatures()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            var filePath = Path.Combine(workspaceRoot, "src", "RunExecutionService.cs");
            File.WriteAllText(filePath,
                """
                namespace FileTransferTool.Infrastructure.Workers;

                public sealed class WorkflowDefinition;
                public sealed class WorkflowStepDefinition;
                public sealed class WorkflowExecutionContext;

                public sealed class RunExecutionService
                {
                    private async Task<string> ExecuteSubWorkflowByNameAsync(
                        string subWorkflowName,
                        WorkflowDefinition workflow,
                        Guid runId,
                        WorkflowStepDefinition parentStep,
                        WorkflowExecutionContext context,
                        Func<int> nextTraceSequence,
                        CancellationToken cancellationToken)
                    {
                        await Task.Yield();
                        return subWorkflowName;
                    }
                }
                """);

            var idGenerator = new BoIdGenerator();
            var repoId = idGenerator.CreateRepoId(workspaceRoot);
            var file = new FileRecord(
                idGenerator.CreateFileId(repoId, workspaceRoot, filePath),
                repoId,
                filePath,
                "src/RunExecutionService.cs",
                "csharp",
                false,
                false,
                idGenerator.CreateModuleId(repoId, "src"));

            var symbolResult = new SourceSymbolExtractor(idGenerator).Extract([file]);
            var contracts = new ContractExtractor().Extract([file], symbolResult.Symbols);

            var contract = Assert.Single(
                contracts,
                candidate => symbolResult.Symbols.Single(symbol => symbol.Id == candidate.SymbolId).DisplayName == "ExecuteSubWorkflowByNameAsync");
            Assert.Equal(
                [
                    "string",
                    "WorkflowDefinition",
                    "Guid",
                    "WorkflowStepDefinition",
                    "WorkflowExecutionContext",
                    "Func<int>",
                    "CancellationToken"
                ],
                contract.InputTypes);
            Assert.Equal(["Task<string>"], contract.OutputTypes);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static string CreateTempWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "bo-contract-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record User;
}
