using BO.Core.Ids;
using BO.Core.Indexing;

namespace BO.Tests;

public sealed class SourceSymbolExtractorTests
{
    [Fact]
    public void Extract_FindsTopLevelTypeScriptSymbols()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            var filePath = Path.Combine(workspaceRoot, "src", "greeter.ts");
            File.WriteAllText(filePath,
                """
                export interface Greeter {
                  greet(name: string): string;
                }

                export class DefaultGreeter {}

                export function greet(name: string) {
                  return `hi ${name}`;
                }

                const hiddenValue = 42;
                """);

            var idGenerator = new BoIdGenerator();
            var repoId = idGenerator.CreateRepoId(workspaceRoot);
            var file = new FileRecord(
                idGenerator.CreateFileId(repoId, workspaceRoot, filePath),
                repoId,
                filePath,
                "src/greeter.ts",
                "typescript",
                false,
                false,
                idGenerator.CreateModuleId(repoId, "src"));

            var extractor = new SourceSymbolExtractor(idGenerator);
            var result = extractor.Extract([file]);

            Assert.Equal(1, result.FilesParsed);
            Assert.Empty(result.Warnings);
            Assert.Equal(4, result.Symbols.Count);
            Assert.Contains(result.Symbols, symbol => symbol.Kind == "interface" && symbol.DisplayName == "Greeter" && symbol.IsExported);
            Assert.Contains(result.Symbols, symbol => symbol.Kind == "class" && symbol.DisplayName == "DefaultGreeter" && symbol.IsExported);
            Assert.Contains(result.Symbols, symbol => symbol.Kind == "function" && symbol.DisplayName == "greet" && symbol.IsExported);
            Assert.Contains(result.Symbols, symbol => symbol.Kind == "variable" && symbol.DisplayName == "hiddenValue" && !symbol.IsExported);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public void Extract_FindsCommonJsExports()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "lib"));
            var filePath = Path.Combine(workspaceRoot, "lib", "handlers.js");
            File.WriteAllText(filePath,
                """
                function localThing() {
                  return 1;
                }

                exports.handleWebhook = async function(req, res) {
                  return res.sendStatus(204);
                };
                """);

            var idGenerator = new BoIdGenerator();
            var repoId = idGenerator.CreateRepoId(workspaceRoot);
            var file = new FileRecord(
                idGenerator.CreateFileId(repoId, workspaceRoot, filePath),
                repoId,
                filePath,
                "lib/handlers.js",
                "javascript",
                false,
                false,
                idGenerator.CreateModuleId(repoId, "lib"));

            var extractor = new SourceSymbolExtractor(idGenerator);
            var result = extractor.Extract([file]);

            Assert.Contains(result.Symbols, symbol => symbol.DisplayName == "localThing" && symbol.Kind == "function");
            Assert.Contains(result.Symbols, symbol => symbol.DisplayName == "handleWebhook" && symbol.IsExported);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public void Extract_MarksNamedExportsAndDefaultExports()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            var filePath = Path.Combine(workspaceRoot, "src", "exports.ts");
            File.WriteAllText(filePath,
                """
                const internalValue = 1;
                function helper() {
                  return internalValue;
                }

                export { helper };
                export default internalValue;
                """);

            var result = ExtractSingleFile(workspaceRoot, filePath, "src/exports.ts", "typescript", "src");

            Assert.Contains(result.Symbols, symbol => symbol.DisplayName == "helper" && symbol.IsExported);
            Assert.Contains(result.Symbols, symbol => symbol.DisplayName == "internalValue" && symbol.IsExported);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public void Extract_FindsClassMembers()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            var filePath = Path.Combine(workspaceRoot, "src", "service.ts");
            File.WriteAllText(filePath,
                """
                export class BillingService
                {
                  constructor(client: unknown) {}

                  async charge(amount: number) {
                    return amount;
                  }

                  retry = async () => true;
                }
                """);

            var result = ExtractSingleFile(workspaceRoot, filePath, "src/service.ts", "typescript", "src");

            Assert.Contains(result.Symbols, symbol => symbol.QualifiedName == "BillingService" && symbol.Kind == "class");
            Assert.Contains(result.Symbols, symbol => symbol.QualifiedName == "BillingService.constructor" && symbol.Kind == "constructor");
            Assert.Contains(result.Symbols, symbol => symbol.QualifiedName == "BillingService.charge" && symbol.Kind == "method");
            Assert.Contains(result.Symbols, symbol => symbol.QualifiedName == "BillingService.retry" && symbol.Kind == "method");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public void Extract_FindsCSharpTopLevelLocalFunctions()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            var filePath = Path.Combine(workspaceRoot, "Program.cs");
            File.WriteAllText(filePath,
                """
                using System.Text.Json;

                var command = args.Length == 0 ? "help" : args[0];

                static void WriteJson(object payload)
                {
                    Console.WriteLine(JsonSerializer.Serialize(payload));
                }

                static async Task<int> RunAsync()
                {
                    await Task.Yield();
                    return 0;
                }
                """);

            var result = ExtractSingleFile(workspaceRoot, filePath, "Program.cs", "csharp", ".");

            Assert.Contains(result.Symbols, symbol =>
                symbol.QualifiedName == "Program.WriteJson" &&
                symbol.DisplayName == "WriteJson" &&
                symbol.Kind == "method");
            Assert.Contains(result.Symbols, symbol =>
                symbol.QualifiedName == "Program.RunAsync" &&
                symbol.DisplayName == "RunAsync" &&
                symbol.Kind == "method");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static SymbolExtractionResult ExtractSingleFile(
        string workspaceRoot,
        string filePath,
        string normalizedPath,
        string language,
        string moduleName)
    {
        var idGenerator = new BoIdGenerator();
        var repoId = idGenerator.CreateRepoId(workspaceRoot);
        var file = new FileRecord(
            idGenerator.CreateFileId(repoId, workspaceRoot, filePath),
            repoId,
            filePath,
            normalizedPath,
            language,
            false,
            false,
            idGenerator.CreateModuleId(repoId, moduleName));

        var extractor = new SourceSymbolExtractor(idGenerator);
        return extractor.Extract([file]);
    }

    private static string CreateTempWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "bo-symbol-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
