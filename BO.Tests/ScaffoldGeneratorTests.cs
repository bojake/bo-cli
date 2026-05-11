using BO.Core.Indexing;

namespace BO.Tests;

public sealed class ScaffoldGeneratorTests
{
    [Fact]
    public void Generate_KeepsExpressionBodiedMethodsBounded_AndSkipsNestedTypeMembers()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"bo-scaffold-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var sourceRelativePath = Path.Combine("src", "Sample", "SampleGodClass.cs");
            var sourcePath = Path.Combine(workspaceRoot, sourceRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(
                sourcePath,
                """
                using System.Text.Json;

                namespace Sample;

                public sealed class SampleGodClass
                {
                    private Task<string> ExecuteFooStepAsync(JsonElement config, CancellationToken cancellationToken) =>
                        ExecuteGraphUploadAsync(config, cancellationToken, "Foo");

                    private Task<string> ExecuteBarStepAsync(JsonElement config, CancellationToken cancellationToken) =>
                        ExecuteGraphUploadAsync(config, cancellationToken, "Bar");

                    private string DescribeNested(NestedThing nestedThing)
                        => nestedThing.ToString() ?? string.Empty;

                    private int DescribeFrame(NestedFrame frame)
                        => frame.Value;

                    private async Task<string> ExecuteGraphUploadAsync(
                        JsonElement config,
                        CancellationToken cancellationToken,
                        string stepType)
                    {
                        await Task.Yield();
                        return stepType;
                    }

                    private sealed class NestedThing
                    {
                        public void Apply()
                        {
                        }
                    }

                    private readonly record struct NestedFrame(int Value);
                }
                """);

            var recipe = new ExtractionRecipe
            {
                SeamName = "sample",
                SourceFile = sourceRelativePath,
                PivotType = "extract_policy",
                Risk = "medium",
                Confidence = 0.8,
                CreateFile = new CreateFileOperation
                {
                    Path = Path.Combine("src", "Sample", "SampleStepExecutor.cs"),
                    ClassName = "SampleStepExecutor",
                    InterfaceName = "ISampleStepExecutor",
                    Namespace = "Sample",
                    PlacementReason = "test",
                    SupportedStepTypes = ["Foo", "Bar"],
                    ConstructorParams = [],
                    MethodsToCopy =
                    [
                        new MethodToCopy { Name = "ExecuteFooStepAsync", StepType = "Foo" },
                        new MethodToCopy { Name = "ExecuteBarStepAsync", StepType = "Bar" },
                        new MethodToCopy { Name = "DescribeNested" },
                        new MethodToCopy { Name = "DescribeFrame" },
                        new MethodToCopy { Name = "Apply" }
                    ],
                    HelpersThatMove = [],
                    RecordsToMove = []
                },
                InterfaceFile = new InterfaceFileOperation
                {
                    Name = "ISampleStepExecutor",
                    Path = Path.Combine("src", "Sample", "ISampleStepExecutor.cs"),
                    Namespace = "Sample",
                    PlacementReason = "test"
                },
                ModifyGodClass = new ModifyGodClassOperation
                {
                    MethodsToDelete = [],
                    DispatchRewires = []
                },
                RegisterDi = new DiRegistration
                {
                    RegistrationLine = "services.AddScoped<ISampleStepExecutor, SampleStepExecutor>();"
                },
                EstimatedLocReduction = 10
            };

            var generator = new ScaffoldGenerator();
            var result = generator.Generate(recipe, workspaceRoot);

            Assert.NotNull(result);
            Assert.Contains("private Task<string> ExecuteFooStepAsync", result!.NewFileContent, StringComparison.Ordinal);
            Assert.Contains("private Task<string> ExecuteBarStepAsync", result.NewFileContent, StringComparison.Ordinal);
            Assert.DoesNotContain("private async Task<string> godClass.ExecuteGraphUploadAsync", result.NewFileContent, StringComparison.Ordinal);
            Assert.DoesNotContain("public void Apply()", result.NewFileContent, StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(result.NewFileContent, "private Task<string> ExecuteFooStepAsync("));
            Assert.Equal(1, CountOccurrences(result.NewFileContent, "private Task<string> ExecuteBarStepAsync("));
            Assert.Contains("private string DescribeNested(SampleGodClass.NestedThing nestedThing)", result.NewFileContent, StringComparison.Ordinal);
            Assert.Contains("private int DescribeFrame(SampleGodClass.NestedFrame frame)", result.NewFileContent, StringComparison.Ordinal);
            Assert.Empty(result.AdditionalDiRegistrationLines);
        }
        finally
        {
            try
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup for temp test workspace.
            }
        }
    }

    [Fact]
    public void Generate_PreservesAdditionalDiRegistrationLines()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"bo-scaffold-di-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var sourceRelativePath = Path.Combine("src", "Sample", "SampleGodClass.cs");
            var sourcePath = Path.Combine(workspaceRoot, sourceRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(
                sourcePath,
                """
                namespace Sample;

                public sealed class SampleGodClass
                {
                    private string BuildSummary(string input)
                    {
                        return input.Trim();
                    }
                }
                """);

            var recipe = new ExtractionRecipe
            {
                SeamName = "sample",
                SourceFile = sourceRelativePath,
                PivotType = "extract_policy",
                Risk = "low",
                Confidence = 0.9,
                CreateFile = new CreateFileOperation
                {
                    Path = Path.Combine("src", "Sample", "SummaryExtraction.cs"),
                    ClassName = "SummaryExtraction",
                    InterfaceName = "ISummaryExtraction",
                    Namespace = "Sample",
                    PlacementReason = "test",
                    SupportedStepTypes = [],
                    ConstructorParams = [],
                    MethodsToCopy =
                    [
                        new MethodToCopy { Name = "BuildSummary" }
                    ],
                    HelpersThatMove = [],
                    RecordsToMove = []
                },
                InterfaceFile = new InterfaceFileOperation
                {
                    Name = "ISummaryExtraction",
                    Path = Path.Combine("src", "Sample", "ISummaryExtraction.cs"),
                    Namespace = "Sample",
                    PlacementReason = "test"
                },
                ModifyGodClass = new ModifyGodClassOperation
                {
                    MethodsToDelete = ["BuildSummary"],
                    DispatchRewires = []
                },
                RegisterDi = new DiRegistration
                {
                    RegistrationLine = "services.AddScoped<ISummaryExtraction, SummaryExtraction>();",
                    AdditionalRegistrationLines = ["services.AddScoped<SummaryFacade>();"]
                },
                EstimatedLocReduction = 8
            };

            var generator = new ScaffoldGenerator();
            var result = generator.Generate(recipe, workspaceRoot);

            Assert.NotNull(result);
            Assert.Equal("services.AddScoped<ISummaryExtraction, SummaryExtraction>();", result!.DiRegistrationLine);
            Assert.Equal(["services.AddScoped<SummaryFacade>();"], result.AdditionalDiRegistrationLines);
        }
        finally
        {
            try
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup for temp test workspace.
            }
        }
    }

    [Fact]
    public void Generate_PreservesPublicContractDependencies_AndPromotesTransitiveNestedInterfaces()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"bo-scaffold-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var sourceRelativePath = Path.Combine("src", "Sample", "SampleGodClass.cs");
            var sourcePath = Path.Combine(workspaceRoot, sourceRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(
                sourcePath,
                """
                using System.Text.Json;

                namespace Sample;

                public sealed class SampleGodClass
                {
                    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
                    {
                        await ExecuteFooStepAsync(cancellationToken);
                    }

                    private async Task ExecuteFooStepAsync(CancellationToken cancellationToken)
                    {
                        await WriteTraceAsync(cancellationToken);
                    }

                    private Task WriteTraceAsync(CancellationToken cancellationToken)
                        => Task.CompletedTask;

                    private int DescribePipeline(FooPipeline pipeline)
                        => pipeline.Policies.Count;

                    private interface IFooPolicy
                    {
                        void Apply();
                    }

                    private abstract class FooFilterBase : IFooPolicy
                    {
                        public abstract void Apply();
                    }

                    private sealed class FooFilter : FooFilterBase
                    {
                        public override void Apply()
                        {
                        }
                    }

                    private sealed class FooPipeline
                    {
                        public IReadOnlyList<IFooPolicy> Policies { get; } = [];
                        public IReadOnlyList<FooFilter> Filters { get; } = [];
                    }
                }
                """);

            var recipe = new ExtractionRecipe
            {
                SeamName = "sample",
                SourceFile = sourceRelativePath,
                PivotType = "extract_policy",
                Risk = "medium",
                Confidence = 0.8,
                CreateFile = new CreateFileOperation
                {
                    Path = Path.Combine("src", "Sample", "SampleStepExecutor.cs"),
                    ClassName = "SampleStepExecutor",
                    InterfaceName = "ISampleStepExecutor",
                    Namespace = "Sample",
                    PlacementReason = "test",
                    SupportedStepTypes = [],
                    ConstructorParams = [],
                    MethodsToCopy =
                    [
                        new MethodToCopy { Name = "ExecuteFooStepAsync" },
                        new MethodToCopy { Name = "WriteTraceAsync" },
                        new MethodToCopy { Name = "DescribePipeline" }
                    ],
                    HelpersThatMove = [],
                    RecordsToMove = []
                },
                InterfaceFile = new InterfaceFileOperation
                {
                    Name = "ISampleStepExecutor",
                    Path = Path.Combine("src", "Sample", "ISampleStepExecutor.cs"),
                    Namespace = "Sample",
                    PlacementReason = "test"
                },
                ModifyGodClass = new ModifyGodClassOperation
                {
                    MethodsToDelete = [],
                    DispatchRewires = []
                },
                RegisterDi = new DiRegistration
                {
                    RegistrationLine = "services.AddScoped<ISampleStepExecutor, SampleStepExecutor>();"
                },
                EstimatedLocReduction = 10
            };

            var generator = new ScaffoldGenerator();
            var result = generator.Generate(recipe, workspaceRoot);

            Assert.NotNull(result);
            Assert.DoesNotContain("ExecuteFooStepAsync", result!.NewFileContent, StringComparison.Ordinal);
            Assert.DoesNotContain("WriteTraceAsync", result.NewFileContent, StringComparison.Ordinal);

            var sourceLines = File.ReadAllLines(sourcePath);
            var modified = ScaffoldGenerator.ApplyGodClassEdits(
                sourceLines,
                result.MethodRangesToDelete,
                result.VisibilityFixes);

            Assert.Contains("public async Task ExecuteAsync", modified, StringComparison.Ordinal);
            Assert.Contains("internal async Task ExecuteFooStepAsync", modified, StringComparison.Ordinal);
            Assert.Contains("internal Task WriteTraceAsync", modified, StringComparison.Ordinal);
            Assert.Contains("internal interface IFooPolicy", modified, StringComparison.Ordinal);
            Assert.Contains("internal abstract class FooFilterBase", modified, StringComparison.Ordinal);
            Assert.Contains("internal sealed class FooPipeline", modified, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup for temp test workspace.
            }
        }
    }

    [Fact]
    public void Generate_UsesRecipeStepTypesForDispatcher_AndPreservesIndirectPublicDependencies()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"bo-scaffold-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var sourceRelativePath = Path.Combine("src", "Sample", "SampleGodClass.cs");
            var sourcePath = Path.Combine(workspaceRoot, sourceRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(
                sourcePath,
                """
                using System.Text.Json;

                namespace Sample;

                public sealed class SampleGodClass
                {
                    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
                    {
                        await DispatchAsync(cancellationToken);
                    }

                    private async Task DispatchAsync(CancellationToken cancellationToken)
                    {
                        await ExecuteFooStepAsync(JsonDocument.Parse("{}").RootElement, cancellationToken);
                    }

                    private async Task<FooResult> ExecuteFooStepAsync(JsonElement config, CancellationToken cancellationToken)
                    {
                        await Task.Yield();
                        return new FooResult(config.ToString());
                    }

                    private async Task<string> ExecuteBarStepAsync(
                        JsonElement config,
                        int attempt,
                        CancellationToken cancellationToken)
                    {
                        await Task.Yield();
                        return $"{attempt}:{config}";
                    }

                    private sealed record FooResult(string Value);
                }
                """);

            var recipe = new ExtractionRecipe
            {
                SeamName = "sample",
                SourceFile = sourceRelativePath,
                PivotType = "extract_policy",
                Risk = "medium",
                Confidence = 0.8,
                CreateFile = new CreateFileOperation
                {
                    Path = Path.Combine("src", "Sample", "SampleStepExecutor.cs"),
                    ClassName = "SampleStepExecutor",
                    InterfaceName = "ISampleStepExecutor",
                    Namespace = "Sample",
                    PlacementReason = "test",
                    SupportedStepTypes = ["Bar"],
                    ConstructorParams = [],
                    MethodsToCopy =
                    [
                        new MethodToCopy { Name = "ExecuteFooStepAsync" },
                        new MethodToCopy { Name = "ExecuteBarStepAsync", StepType = "Bar" }
                    ],
                    HelpersThatMove = [],
                    RecordsToMove = []
                },
                InterfaceFile = new InterfaceFileOperation
                {
                    Name = "ISampleStepExecutor",
                    Path = Path.Combine("src", "Sample", "ISampleStepExecutor.cs"),
                    Namespace = "Sample",
                    PlacementReason = "test"
                },
                ModifyGodClass = new ModifyGodClassOperation
                {
                    MethodsToDelete = [],
                    DispatchRewires = []
                },
                RegisterDi = new DiRegistration
                {
                    RegistrationLine = "services.AddScoped<ISampleStepExecutor, SampleStepExecutor>();"
                },
                EstimatedLocReduction = 10
            };

            var generator = new ScaffoldGenerator();
            var result = generator.Generate(recipe, workspaceRoot);

            Assert.NotNull(result);
            Assert.DoesNotContain("WorkflowStepType.Foo", result!.NewFileContent, StringComparison.Ordinal);
            Assert.Contains("WorkflowStepType.Bar => await ExecuteBarStepAsync(step.Configuration, context, cancellationToken)", result.NewFileContent, StringComparison.Ordinal);
            Assert.DoesNotContain("private async Task<string> ExecuteFooStepAsync", result.NewFileContent, StringComparison.Ordinal);

            var sourceLines = File.ReadAllLines(sourcePath);
            var modified = ScaffoldGenerator.ApplyGodClassEdits(
                sourceLines,
                result.MethodRangesToDelete,
                result.VisibilityFixes);

            Assert.Contains("internal async Task<FooResult> ExecuteFooStepAsync", modified, StringComparison.Ordinal);
            Assert.Contains("internal sealed record FooResult", modified, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup for temp test workspace.
            }
        }
    }

    [Fact]
    public void GenerateInterfaceFile_TrimsExistingTrailingSemicolonFromSignature()
    {
        var recipe = new ExtractionRecipe
        {
            SeamName = "pure",
            SourceFile = Path.Combine("src", "Sample", "ISampleService.cs"),
            PivotType = "extract_pure_logic",
            Risk = "low",
            Confidence = 0.9,
            CreateFile = new CreateFileOperation
            {
                Path = Path.Combine("src", "Sample", "PureStepExecutor.cs"),
                ClassName = "PureStepExecutor",
                InterfaceName = "IPureStepExecutor",
                Namespace = "Sample",
                PlacementReason = "test",
                SupportedStepTypes = [],
                ConstructorParams = [],
                MethodsToCopy =
                [
                    new MethodToCopy { Name = "ExecuteAsync" }
                ],
                HelpersThatMove = [],
                RecordsToMove = []
            },
            InterfaceFile = new InterfaceFileOperation
            {
                Name = "IPureStepExecutor",
                Path = Path.Combine("src", "Sample", "IPureStepExecutor.cs"),
                Namespace = "Sample",
                PlacementReason = "test"
            },
            ModifyGodClass = new ModifyGodClassOperation
            {
                MethodsToDelete = ["ExecuteAsync"],
                DispatchRewires = []
            },
            RegisterDi = new DiRegistration
            {
                RegistrationLine = "services.AddScoped<IPureStepExecutor, PureStepExecutor>();"
            },
            EstimatedLocReduction = 1
        };

        var generateInterfaceFile = typeof(ScaffoldGenerator)
            .GetMethod("GenerateInterfaceFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(generateInterfaceFile);

        var sourceLines = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Sample;

            public interface ISampleService
            {
                Task ExecuteAsync(Guid runId, CancellationToken cancellationToken = default);
            }
            """
            .Split(Environment.NewLine);

        var generated = Assert.IsType<string>(generateInterfaceFile!.Invoke(
            null,
            ["Sample", "IPureStepExecutor", sourceLines, recipe, ArchitecturePlacementRules.Default.InterfacePlacement]));
        Assert.Contains("Task ExecuteAsync(Guid runId, CancellationToken cancellationToken = default);", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("default);;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateInterfaceFile_UsesConfiguredAllowedContractLayers()
    {
        var recipe = new ExtractionRecipe
        {
            SeamName = "ports",
            SourceFile = Path.Combine("source", "Acme.Adapters", "Handler.cs"),
            PivotType = "extract_policy",
            Risk = "low",
            Confidence = 0.9,
            CreateFile = new CreateFileOperation
            {
                Path = Path.Combine("source", "Acme.Adapters", "PortsHandler.cs"),
                ClassName = "PortsHandler",
                InterfaceName = "IPortsHandler",
                Namespace = "Acme.Adapters",
                PlacementReason = "test",
                SupportedStepTypes = [],
                ConstructorParams = [],
                MethodsToCopy = [new MethodToCopy { Name = "ExecuteAsync" }],
                HelpersThatMove = [],
                RecordsToMove = []
            },
            InterfaceFile = new InterfaceFileOperation
            {
                Name = "IPortsHandler",
                Path = Path.Combine("source", "Acme.UseCases", "Ports", "IPortsHandler.cs"),
                Namespace = "Acme.UseCases.Ports",
                PlacementReason = "test"
            },
            ModifyGodClass = new ModifyGodClassOperation
            {
                MethodsToDelete = ["ExecuteAsync"],
                DispatchRewires = []
            },
            RegisterDi = new DiRegistration
            {
                RegistrationLine = "services.AddScoped<IPortsHandler, PortsHandler>();"
            },
            EstimatedLocReduction = 1
        };
        var rules = ArchitecturePlacementRules.Default.InterfacePlacement with
        {
            AbstractionLayerNames = ["UseCases"],
            AllowedContractLayers = ["domain", "usecases"],
            DisallowedContractLayers = ["adapters"],
            Layers =
            [
                new ArchitectureLayerRule("domain", [".Domain"], ["Domain"]),
                new ArchitectureLayerRule("usecases", [".UseCases"], ["UseCases"]),
                new ArchitectureLayerRule("adapters", [".Adapters"], ["Adapters"])
            ]
        };
        var sourceLines = """
            using Acme.Domain.Orders;
            using Acme.UseCases.Ports;
            using Acme.Adapters.Sql;
            using ThirdParty.Json;

            namespace Acme.Adapters;

            public sealed class Handler
            {
                private Task ExecuteAsync(Order order, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """
            .Split(Environment.NewLine);
        var generateInterfaceFile = typeof(ScaffoldGenerator)
            .GetMethod("GenerateInterfaceFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var generated = Assert.IsType<string>(generateInterfaceFile!.Invoke(
            null,
            ["Acme.UseCases.Ports", "IPortsHandler", sourceLines, recipe, rules]));

        Assert.Contains("using Acme.Domain.Orders;", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("using Acme.Adapters.Sql;", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("using ThirdParty.Json;", generated, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
