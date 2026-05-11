using BO.Core.Ids;
using BO.Core.Indexing;

namespace BO.Tests;

public sealed class SymbolDependencyExtractorTests
{
    [Fact]
    public void Extract_FindsLocalCallsImportedCallsInstantiationsAndTypeUsages()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src", "core"));
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src", "http"));
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src", "models"));

            File.WriteAllText(
                Path.Combine(workspaceRoot, "src", "core", "greeter.ts"),
                """
                export class FriendlyGreeter {
                  constructor(prefix: string) {}

                  greet(name: string) {
                    return prefixValue() + name;
                  }
                }

                export function prefixValue() {
                  return "hi ";
                }
                """);

            File.WriteAllText(
                Path.Combine(workspaceRoot, "src", "models", "result.ts"),
                """
                export interface Result {
                  ok: boolean;
                }
                """);

            File.WriteAllText(
                Path.Combine(workspaceRoot, "src", "http", "handlers.ts"),
                """
                export function handlePing(value: unknown) {
                  return value;
                }
                """);

            File.WriteAllText(
                Path.Combine(workspaceRoot, "src", "index.ts"),
                """
                import { FriendlyGreeter } from "./core/greeter";
                import { handlePing } from "./http/handlers";
                import type { Result } from "./models/result";

                export function bootstrap(seed: Result) {
                  const greeter = new FriendlyGreeter("hi");
                  greeter.greet("world");
                  return handlePing(seed);
                }
                """);

            var idGenerator = new BoIdGenerator();
            var scanner = new WorkspaceScanner(idGenerator);
            var scanResult = scanner.Scan(workspaceRoot, "test");
            var symbolExtraction = new SourceSymbolExtractor(idGenerator).Extract(scanResult.Files);
            var symbols = symbolExtraction.Symbols;
            var contracts = new ContractExtractor().Extract(scanResult.Files, symbols);
            var dependencies = new DependencyExtractor(idGenerator).Extract(scanResult.Files);
            var symbolDependencies = new SymbolDependencyExtractor().Extract(scanResult.Files, symbols, contracts, dependencies);

            Assert.Contains(symbolDependencies, edge =>
                edge.RelationType == "calls" &&
                symbols.Single(symbol => symbol.Id == edge.FromSymbolId).QualifiedName == "FriendlyGreeter.greet" &&
                symbols.Single(symbol => symbol.Id == edge.ToSymbolId).QualifiedName == "prefixValue");

            Assert.Contains(symbolDependencies, edge =>
                edge.RelationType == "instantiates" &&
                symbols.Single(symbol => symbol.Id == edge.FromSymbolId).QualifiedName == "bootstrap" &&
                symbols.Single(symbol => symbol.Id == edge.ToSymbolId).QualifiedName == "FriendlyGreeter");

            Assert.Contains(symbolDependencies, edge =>
                edge.RelationType == "calls" &&
                symbols.Single(symbol => symbol.Id == edge.FromSymbolId).QualifiedName == "bootstrap" &&
                symbols.Single(symbol => symbol.Id == edge.ToSymbolId).QualifiedName == "FriendlyGreeter.greet");

            Assert.Contains(symbolDependencies, edge =>
                edge.RelationType == "calls" &&
                symbols.Single(symbol => symbol.Id == edge.FromSymbolId).QualifiedName == "bootstrap" &&
                symbols.Single(symbol => symbol.Id == edge.ToSymbolId).QualifiedName == "handlePing");

            Assert.Contains(symbolDependencies, edge =>
                edge.RelationType == "uses_type" &&
                symbols.Single(symbol => symbol.Id == edge.FromSymbolId).QualifiedName == "bootstrap" &&
                symbols.Single(symbol => symbol.Id == edge.ToSymbolId).QualifiedName == "Result");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static string CreateTempWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "bo-symbol-dep-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
