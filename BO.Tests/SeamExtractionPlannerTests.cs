using BO.Core.Indexing;

namespace BO.Tests;

public sealed class SeamExtractionPlannerTests
{
    [Fact]
    public void Plan_UsesInjectedDomainRules_ForCustomSupportDomains()
    {
        const string fileId = "file:custom";
        var rules = new SeamDomainRules(
            "test",
            [
                new SeamSupportDomainRule(
                    "vision_support",
                    ["VectorSearch"],
                    [],
                    [])
            ],
            [
                new SeamMethodDomainRule("vision", ["VectorSearch"])
            ],
            "core_orchestration",
            "helper_support");
        var planner = new SeamExtractionPlanner(rules);

        FileRecord[] files =
        [
            new FileRecord(fileId, "repo:test", "VisionService.cs", "VisionService.cs", "csharp", false, false, "module:test")
        ];

        SymbolRecord[] symbols =
        [
            CreateClass(fileId, "VisionService", "Sample.VisionService", 1),
            CreateMethod(fileId, "RunVectorSearchAsync", "Sample.VisionService.RunVectorSearchAsync", 10)
        ];

        RefactorDecisionRecord[] decisions =
        [
            new RefactorDecisionRecord(
                "decision:test",
                fileId,
                "extract",
                "extract_policy",
                [],
                [],
                ["helper:RunVectorSearchAsync"],
                0.8,
                0.8)
        ];

        var (plans, _) = planner.Plan(
            decisions,
            symbols,
            [],
            [],
            [],
            files);

        var plan = Assert.Single(plans);
        Assert.Equal("vision_support", plan.SeamName);
        Assert.Contains("RunVectorSearchAsync", plan.MethodsToExtract);
    }

    [Fact]
    public void Plan_SplitsHelperCandidatesIntoSmallerSymbolAwareDomains()
    {
        const string fileId = "file:run-execution";
        var planner = new SeamExtractionPlanner();

        FileRecord[] files =
        [
            new FileRecord(fileId, "repo:test", "RunExecutionService.cs", "RunExecutionService.cs", "csharp", false, false, "module:test")
        ];

        SymbolRecord[] symbols =
        [
            CreateClass(fileId, "RunExecutionService", "FileTransferTool.Infrastructure.Workers.RunExecutionService", 1),
            CreateMethod(fileId, "ExecuteStructuredStepAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ExecuteStructuredStepAsync", 10),
            CreateMethod(fileId, "SetVariable", "FileTransferTool.Infrastructure.Workers.RunExecutionService.SetVariable", 20),
            CreateMethod(fileId, "BuildGraphSettings", "FileTransferTool.Infrastructure.Workers.RunExecutionService.BuildGraphSettings", 30),
            CreateMethod(fileId, "ExecuteResourceOneDriveUploadStepAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ExecuteResourceOneDriveUploadStepAsync", 40),
            CreateMethod(fileId, "CreateSftpClient", "FileTransferTool.Infrastructure.Workers.RunExecutionService.CreateSftpClient", 50),
            CreateMethod(fileId, "CreateSshClient", "FileTransferTool.Infrastructure.Workers.RunExecutionService.CreateSshClient", 60),
            CreateMethod(fileId, "BuildSqlTransformPipeline", "FileTransferTool.Infrastructure.Workers.RunExecutionService.BuildSqlTransformPipeline", 65),
            CreateMethod(fileId, "Apply", "FileTransferTool.Infrastructure.Workers.RunExecutionService.SqlTransformPipeline.Apply", 70),
            CreateMethod(fileId, "Transform", "FileTransferTool.Infrastructure.Workers.RunExecutionService.SqlColumnTransformFilter.Transform", 80),
            CreateMethod(fileId, "WriteJsonExportAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.WriteJsonExportAsync", 90)
        ];

        RefactorDecisionRecord[] decisions =
        [
            new RefactorDecisionRecord(
                "decision:test",
                fileId,
                "extract",
                "extract_policy",
                [],
                [],
                [
                    "helper:ExecuteStructuredStepAsync",
                    "helper:SetVariable",
                    "helper:BuildGraphSettings",
                    "helper:ExecuteResourceOneDriveUploadStepAsync",
                    "helper:CreateSftpClient",
                    "helper:CreateSshClient",
                    "helper:BuildSqlTransformPipeline",
                    "helper:Apply",
                    "helper:Transform",
                    "helper:WriteJsonExportAsync"
                ],
                0.8,
                0.8)
        ];

        var (plans, _) = planner.Plan(
            decisions,
            symbols,
            [],
            [],
            [],
            files);

        Assert.Contains(plans, plan => plan.SeamName == "workflow_support");
        Assert.Contains(plans, plan => plan.SeamName == "graph_drive_support");
        Assert.Contains(plans, plan => plan.SeamName == "ftp_support");
        Assert.Contains(plans, plan => plan.SeamName == "ssh_support");
        Assert.Contains(plans, plan => plan.SeamName == "sql_transform");
        Assert.Contains(plans, plan => plan.SeamName == "json_document_support");
        Assert.DoesNotContain(plans, plan => plan.SeamName == "helper");

        var sqlTransformPlan = Assert.Single(plans, plan => plan.SeamName == "sql_transform");
        Assert.Equal(["BuildSqlTransformPipeline"], sqlTransformPlan.MethodsToExtract);
    }

    [Fact]
    public void Plan_SplitsCoreOrchestrationHelpersIntoSmallerSupportDomains()
    {
        const string fileId = "file:run-execution";
        var planner = new SeamExtractionPlanner();

        FileRecord[] files =
        [
            new FileRecord(fileId, "repo:test", "RunExecutionService.cs", "RunExecutionService.cs", "csharp", false, false, "module:test")
        ];

        SymbolRecord[] symbols =
        [
            CreateClass(fileId, "RunExecutionService", "FileTransferTool.Infrastructure.Workers.RunExecutionService", 1),
            CreateMethod(fileId, "GetConfigString", "FileTransferTool.Infrastructure.Workers.RunExecutionService.GetConfigString", 10),
            CreateMethod(fileId, "WriteCsvExportAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.WriteCsvExportAsync", 20),
            CreateMethod(fileId, "ApplyCipherPolicy", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ApplyCipherPolicy", 30),
            CreateMethod(fileId, "BuildFeedAggregateText", "FileTransferTool.Infrastructure.Workers.RunExecutionService.BuildFeedAggregateText", 40),
            CreateMethod(fileId, "NormalizeAttachmentPath", "FileTransferTool.Infrastructure.Workers.RunExecutionService.NormalizeAttachmentPath", 50),
            CreateMethod(fileId, "ScheduleRetryOrDeadLetterAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ScheduleRetryOrDeadLetterAsync", 60),
            CreateMethod(fileId, "ExecuteStepCoreAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ExecuteStepCoreAsync", 70)
        ];

        RefactorDecisionRecord[] decisions =
        [
            new RefactorDecisionRecord(
                "decision:test",
                fileId,
                "extract",
                "extract_policy",
                [],
                [],
                [
                    "helper:GetConfigString",
                    "helper:WriteCsvExportAsync",
                    "helper:ApplyCipherPolicy",
                    "helper:BuildFeedAggregateText",
                    "helper:NormalizeAttachmentPath",
                    "helper:ScheduleRetryOrDeadLetterAsync",
                    "helper:ExecuteStepCoreAsync"
                ],
                0.8,
                0.8)
        ];

        var (plans, _) = planner.Plan(
            decisions,
            symbols,
            [],
            [],
            [],
            files);

        Assert.Contains(plans, plan => plan.SeamName == "config_support");
        Assert.Contains(plans, plan => plan.SeamName == "export_support");
        Assert.Contains(plans, plan => plan.SeamName == "crypto_support");
        Assert.Contains(plans, plan => plan.SeamName == "content_support");
        Assert.Contains(plans, plan => plan.SeamName == "media_document_support");
        Assert.Contains(plans, plan => plan.SeamName == "orchestration_runtime_support");
        Assert.Contains(plans, plan => plan.SeamName == "helper_support");
    }

    [Fact]
    public void Plan_UsesSupportDomainInference_ForPolicySeams()
    {
        const string fileId = "file:run-execution";
        var planner = new SeamExtractionPlanner();

        FileRecord[] files =
        [
            new FileRecord(fileId, "repo:test", "RunExecutionService.cs", "RunExecutionService.cs", "csharp", false, false, "module:test")
        ];

        SymbolRecord[] symbols =
        [
            CreateClass(fileId, "RunExecutionService", "FileTransferTool.Infrastructure.Workers.RunExecutionService", 1),
            CreateMethod(fileId, "GetConfigString", "FileTransferTool.Infrastructure.Workers.RunExecutionService.GetConfigString", 10),
            CreateMethod(fileId, "WriteCsvExportAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.WriteCsvExportAsync", 20),
            CreateMethod(fileId, "ApplyCipherPolicy", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ApplyCipherPolicy", 30),
            CreateMethod(fileId, "BuildFeedAggregateText", "FileTransferTool.Infrastructure.Workers.RunExecutionService.BuildFeedAggregateText", 40),
            CreateMethod(fileId, "NormalizeAttachmentPath", "FileTransferTool.Infrastructure.Workers.RunExecutionService.NormalizeAttachmentPath", 50),
            CreateMethod(fileId, "ScheduleRetryOrDeadLetterAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ScheduleRetryOrDeadLetterAsync", 60),
            CreateMethod(fileId, "ExecuteSendRunAlertStepAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ExecuteSendRunAlertStepAsync", 70),
            CreateMethod(fileId, "ExecuteStepCoreAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ExecuteStepCoreAsync", 80)
        ];

        RefactorDecisionRecord[] decisions =
        [
            new RefactorDecisionRecord(
                "decision:test",
                fileId,
                "extract",
                "extract_policy",
                [],
                [],
                [
                    "policy[b=4,cc=8]:GetConfigString",
                    "policy[b=5,cc=9]:WriteCsvExportAsync",
                    "policy[b=5,cc=9]:ApplyCipherPolicy",
                    "policy[b=5,cc=9]:BuildFeedAggregateText",
                    "policy[b=5,cc=9]:NormalizeAttachmentPath",
                    "policy[b=5,cc=9]:ScheduleRetryOrDeadLetterAsync",
                    "policy[b=5,cc=9]:ExecuteSendRunAlertStepAsync",
                    "policy[b=8,cc=20]:ExecuteStepCoreAsync"
                ],
                0.8,
                0.8)
        ];

        var (plans, _) = planner.Plan(
            decisions,
            symbols,
            [],
            [],
            [],
            files);

        Assert.Contains(plans, plan => plan.SeamName == "config_support");
        Assert.Contains(plans, plan => plan.SeamName == "export_support");
        Assert.Contains(plans, plan => plan.SeamName == "crypto_support");
        Assert.Contains(plans, plan => plan.SeamName == "content_support");
        Assert.Contains(plans, plan => plan.SeamName == "media_document_support");
        Assert.Contains(plans, plan => plan.SeamName == "orchestration_runtime_support");
        Assert.Contains(plans, plan => plan.SeamName == "notification_support");
        Assert.DoesNotContain(plans, plan => plan.SeamName == "core_orchestration");
    }

    [Fact]
    public void Plan_SplitsDocumentAndGraphSupportIntoSmallerSubdomains()
    {
        const string fileId = "file:run-execution";
        var planner = new SeamExtractionPlanner();

        FileRecord[] files =
        [
            new FileRecord(fileId, "repo:test", "RunExecutionService.cs", "RunExecutionService.cs", "csharp", false, false, "module:test")
        ];

        SymbolRecord[] symbols =
        [
            CreateClass(fileId, "RunExecutionService", "FileTransferTool.Infrastructure.Workers.RunExecutionService", 1),
            CreateMethod(fileId, "ExecuteFileSplitPdfStepAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ExecuteFileSplitPdfStepAsync", 10),
            CreateMethod(fileId, "ApplyJsonExtractionRules", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ApplyJsonExtractionRules", 20),
            CreateMethod(fileId, "ResolveAttachmentPaths", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ResolveAttachmentPaths", 30),
            CreateMethod(fileId, "CreateHashAlgorithm", "FileTransferTool.Infrastructure.Workers.RunExecutionService.CreateHashAlgorithm", 40),
            CreateMethod(fileId, "ExecuteResourceGraphDownloadStepAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ExecuteResourceGraphDownloadStepAsync", 50),
            CreateMethod(fileId, "ExecuteResourceOneDriveUploadStepAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ExecuteResourceOneDriveUploadStepAsync", 60),
            CreateMethod(fileId, "BuildGraphSettings", "FileTransferTool.Infrastructure.Workers.RunExecutionService.BuildGraphSettings", 70)
        ];

        RefactorDecisionRecord[] decisions =
        [
            new RefactorDecisionRecord(
                "decision:test",
                fileId,
                "extract",
                "extract_policy",
                [],
                [],
                [
                    "policy[b=5,cc=9]:ExecuteFileSplitPdfStepAsync",
                    "policy[b=5,cc=9]:ApplyJsonExtractionRules",
                    "policy[b=5,cc=9]:ResolveAttachmentPaths",
                    "policy[b=5,cc=9]:CreateHashAlgorithm",
                    "policy[b=5,cc=9]:ExecuteResourceGraphDownloadStepAsync",
                    "policy[b=5,cc=9]:ExecuteResourceOneDriveUploadStepAsync",
                    "policy[b=5,cc=9]:BuildGraphSettings"
                ],
                0.8,
                0.8)
        ];

        var (plans, _) = planner.Plan(
            decisions,
            symbols,
            [],
            [],
            [],
            files);

        Assert.Contains(plans, plan => plan.SeamName == "pdf_support");
        Assert.Contains(plans, plan => plan.SeamName == "json_document_support");
        Assert.Contains(plans, plan => plan.SeamName == "media_document_support");
        Assert.Contains(plans, plan => plan.SeamName == "document_support");
        Assert.Contains(plans, plan => plan.SeamName == "graph_api_support");
        Assert.Contains(plans, plan => plan.SeamName == "graph_drive_support");
    }

    [Fact]
    public void Plan_KeepsDispatcherRootsOnSourceOwner_ForCoreOrchestrationSeam()
    {
        const string fileId = "file:run-execution";
        var planner = new SeamExtractionPlanner();

        FileRecord[] files =
        [
            new FileRecord(fileId, "repo:test", "RunExecutionService.cs", "RunExecutionService.cs", "csharp", false, false, "module:test")
        ];

        SymbolRecord[] symbols =
        [
            CreateClass(fileId, "RunExecutionService", "FileTransferTool.Infrastructure.Workers.RunExecutionService", 1),
            CreateMethod(fileId, "ExecuteStepCoreAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ExecuteStepCoreAsync", 10),
            CreateMethod(fileId, "ExecuteAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.ExecuteAsync", 20),
            CreateMethod(fileId, "RunProcessAsync", "FileTransferTool.Infrastructure.Workers.RunExecutionService.RunProcessAsync", 30)
        ];

        RefactorDecisionRecord[] decisions =
        [
            new RefactorDecisionRecord(
                "decision:test",
                fileId,
                "extract",
                "extract_policy",
                [],
                [],
                [
                    "policy[b=10,cc=20]:ExecuteStepCoreAsync",
                    "policy[b=9,cc=18]:ExecuteAsync",
                    "policy[b=4,cc=7]:RunProcessAsync"
                ],
                0.8,
                0.8)
        ];

        var (plans, _) = planner.Plan(
            decisions,
            symbols,
            [],
            [],
            [],
            files);

        var corePlan = Assert.Single(plans, plan => plan.SeamName == "core_orchestration");
        Assert.DoesNotContain("ExecuteStepCoreAsync", corePlan.MethodsToExtract);
        Assert.DoesNotContain("ExecuteAsync", corePlan.MethodsToExtract);
        Assert.Contains("RunProcessAsync", corePlan.MethodsToExtract);
    }

    [Fact]
    public void Plan_PrefersConcreteHelperOverExplicitInterfaceBridge_WhenMethodNamesCollide()
    {
        const string fileId = "file:run-execution";
        var planner = new SeamExtractionPlanner();

        FileRecord[] files =
        [
            new FileRecord(fileId, "repo:test", "RunExecutionService.cs", "RunExecutionService.cs", "csharp", false, false, "module:test")
        ];

        SymbolRecord[] symbols =
        [
            CreateClass(fileId, "RunExecutionService", "Sample.RunExecutionService", 1),
            new SymbolRecord(
                "sym:bridge",
                "repo:test",
                fileId,
                "module:test",
                "Sample.RunExecutionService.ResolveAiPromptPayloadAsync",
                "ResolveAiPromptPayloadAsync",
                "method",
                "csharp",
                "Task<AiPromptPayload> IAiAndTranslationRuntime.ResolveAiPromptPayloadAsync(JsonElement config, WorkflowExecutionContext context, ResourceType expectedType, string stepType, CancellationToken cancellationToken)",
                20,
                IsExported: false),
            new SymbolRecord(
                "sym:helper",
                "repo:test",
                fileId,
                "module:test",
                "Sample.RunExecutionService.ResolveAiPromptPayloadAsync",
                "ResolveAiPromptPayloadAsync",
                "method",
                "csharp",
                "private Task<AiPromptPayload> ResolveAiPromptPayloadAsync(JsonElement config, WorkflowExecutionContext context, ResourceType expectedType, string stepType, CancellationToken cancellationToken)",
                120,
                IsExported: false)
        ];

        RefactorDecisionRecord[] decisions =
        [
            new RefactorDecisionRecord(
                "decision:test",
                fileId,
                "extract",
                "extract_policy",
                [],
                [],
                ["helper:ResolveAiPromptPayloadAsync"],
                0.8,
                0.8)
        ];

        var (plans, _) = planner.Plan(
            decisions,
            symbols,
            [],
            [],
            [],
            files);

        var selectedPlan = Assert.Single(plans);
        Assert.Contains("ResolveAiPromptPayloadAsync", selectedPlan.MethodsToExtract);
        Assert.DoesNotContain(selectedPlan.MethodsToExtract, _ => false);
    }

    private static SymbolRecord CreateClass(string fileId, string displayName, string qualifiedName, int declarationLine)
    {
        return new SymbolRecord(
            $"sym:{displayName}:{declarationLine}",
            "repo:test",
            fileId,
            "module:test",
            qualifiedName,
            displayName,
            "class",
            "csharp",
            $"public sealed class {displayName}",
            declarationLine,
            IsExported: false);
    }

    private static SymbolRecord CreateMethod(string fileId, string displayName, string qualifiedName, int declarationLine)
    {
        return new SymbolRecord(
            $"sym:{displayName}:{declarationLine}",
            "repo:test",
            fileId,
            "module:test",
            qualifiedName,
            displayName,
            "method",
            "csharp",
            $"private void {displayName}()",
            declarationLine,
            IsExported: false);
    }
}
