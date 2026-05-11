using BO.Core.Indexing;

namespace BO.Tests;

public sealed class RefactorDecisionDeriverTests
{
    [Fact]
    public void Derive_PlansCliJsonOutputExtraction_ForTopLevelWriteJson()
    {
        var fileId = "file:program";
        var symbolId = "sym:write-json";

        RefactorPressureScoreRecord[] rps =
        [
            new RefactorPressureScoreRecord(
                "rps:file:program",
                fileId,
                "file",
                70,
                "extract",
                ["complexity"],
                ["pivot"],
                0.9)
        ];

        SymbolRecord[] symbols =
        [
            new SymbolRecord(
                "sym:unrelated-record-class",
                "repo:test",
                fileId,
                "module:cli",
                "BuildVerificationResult",
                "BuildVerificationResult",
                "class",
                "csharp",
                "public sealed class BuildVerificationResult",
                20,
                false),
            new SymbolRecord(
                symbolId,
                "repo:test",
                fileId,
                "module:cli",
                "Program.WriteJson",
                "WriteJson",
                "method",
                "csharp",
                "static void WriteJson(object payload)",
                120,
                false)
        ];

        ComplexityProfileRecord[] complexity =
        [
            new ComplexityProfileRecord("complexity:file", fileId, "file", 500, 12, 8, 3, 0, 6, 1, 0, 0, 0.9),
            new ComplexityProfileRecord("complexity:write-json", symbolId, "symbol", 12, 2, 1, 1, 1, 0, 1, 0, 0, 0.9)
        ];

        var deriver = new RefactorDecisionDeriver();
        var decisions = deriver.Derive(
            rps,
            symbols,
            [],
            [],
            [],
            [],
            complexity);

        var decision = Assert.Single(decisions);
        Assert.Contains("cli_json_output:WriteJson", decision.CandidateSeams);

        FileRecord[] files =
        [
            new FileRecord(fileId, "repo:test", "/workspace/BO.Cli/Program.cs", "BO.Cli/Program.cs", "csharp", false, false, "module:cli")
        ];

        var planner = new SeamExtractionPlanner();
        var (plans, _) = planner.Plan(
            decisions,
            symbols,
            [],
            [],
            complexity,
            files);

        var plan = Assert.Single(plans, candidate => candidate.SeamName == "cli_json_output");
        Assert.Equal("CliJsonWriter", plan.ProposedClassName);
        Assert.Contains("WriteJson", plan.MethodsToExtract);
    }

    [Fact]
    public void Derive_ExtractPolicy_SkipsExplicitInterfaceHelperCandidates()
    {
        var deriver = new RefactorDecisionDeriver();
        var fileId = "file:sample";

        RefactorPressureScoreRecord[] rps =
        [
            new RefactorPressureScoreRecord(
                "rps:file:sample",
                fileId,
                "file",
                75,
                "extract",
                ["complexity"],
                ["pivot"],
                0.9)
        ];

        SymbolRecord[] symbols =
        [
            new SymbolRecord("sym:bridge", "repo:test", fileId, "module:test", "Sample.RunExecutionService.ResolveAiPromptPayloadAsync", "ResolveAiPromptPayloadAsync", "method", "csharp", "Task<AiPromptPayload> IAiAndTranslationRuntime.ResolveAiPromptPayloadAsync(JsonElement config, WorkflowExecutionContext context, ResourceType expectedType, string stepType, CancellationToken cancellationToken)", 20, false),
            new SymbolRecord("sym:helper", "repo:test", fileId, "module:test", "Sample.RunExecutionService.ResolveAiPromptPayloadAsync", "ResolveAiPromptPayloadAsync", "method", "csharp", "private Task<AiPromptPayload> ResolveAiPromptPayloadAsync(JsonElement config, WorkflowExecutionContext context, ResourceType expectedType, string stepType, CancellationToken cancellationToken)", 120, false),
            new SymbolRecord("sym:structured", "repo:test", fileId, "module:test", "Sample.RunExecutionService.ExecuteStructuredStepAsync", "ExecuteStructuredStepAsync", "method", "csharp", "private Task<string> ExecuteStructuredStepAsync()", 40, false)
        ];

        ComplexityProfileRecord[] complexity =
        [
            new ComplexityProfileRecord("complexity:file", fileId, "file", 400, 12, 10, 2, 0, 5, 0, 0, 0, 0.9),
            new ComplexityProfileRecord("complexity:bridge", "sym:bridge", "symbol", 1, 0, 0, 0, 0, 0, 0, 0, 0, 0.9),
            new ComplexityProfileRecord("complexity:helper", "sym:helper", "symbol", 3, 0, 0, 0, 0, 0, 0, 0, 0, 0.9),
            new ComplexityProfileRecord("complexity:structured", "sym:structured", "symbol", 40, 8, 6, 2, 0, 4, 0, 0, 0, 0.9)
        ];

        var decisions = deriver.Derive(
            rps,
            symbols,
            [],
            [],
            [],
            [],
            complexity);

        var decision = Assert.Single(decisions);
        Assert.Single(decision.CandidateSeams, candidate => candidate == "helper:ResolveAiPromptPayloadAsync");
        Assert.Contains("policy[b=4,cc=8]:ExecuteStructuredStepAsync", decision.CandidateSeams);
    }

    [Fact]
    public void Derive_UsesConfiguredMinimumRpsScore()
    {
        var deriver = new RefactorDecisionDeriver();
        var fileId = "file:sample";
        var rules = RefactorDecisionRules.Default with
        {
            DecisionMinimums = new DecisionMinimumRules(80)
        };

        RefactorPressureScoreRecord[] rps =
        [
            new RefactorPressureScoreRecord(
                "rps:file:sample",
                fileId,
                "file",
                75,
                "extract",
                ["complexity"],
                [],
                0.9)
        ];

        SymbolRecord[] symbols =
        [
            new SymbolRecord("sym:helper", "repo:test", fileId, "module:test", "Sample.RunExecutionService.ExecuteStructuredStepAsync", "ExecuteStructuredStepAsync", "method", "csharp", "private Task<string> ExecuteStructuredStepAsync()", 40, false)
        ];

        ComplexityProfileRecord[] complexity =
        [
            new ComplexityProfileRecord("complexity:file", fileId, "file", 400, 12, 10, 2, 0, 5, 0, 0, 0, 0.9),
            new ComplexityProfileRecord("complexity:helper", "sym:helper", "symbol", 40, 8, 6, 2, 0, 4, 0, 0, 0, 0.9)
        ];

        var decisions = deriver.Derive(
            rps,
            symbols,
            [],
            [],
            [],
            [],
            complexity,
            rules);

        Assert.Empty(decisions);
    }
}
