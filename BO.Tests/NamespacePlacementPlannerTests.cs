using BO.Core.Indexing;

namespace BO.Tests;

public sealed class NamespacePlacementPlannerTests
{
    [Fact]
    public void Emit_ReusesExistingApplicationAbstraction_WhenInterfaceAlreadyExists()
    {
        var planner = new ExtractionRecipeEmitter();
        var targetFile = CreateFile(
            "file:run-ops",
            "src/FileTransferTool.Infrastructure/Workers/RunOperationsService.cs");
        var abstractionFile = CreateFile(
            "file:run-ops-interface",
            "src/FileTransferTool.Application/Abstractions/IRunOperationsService.cs");

        var plan = new SeamExtractionPlanRecord(
            "plan:run-query",
            targetFile.Id,
            "run_query",
            "extract_boundary_adapter",
            null,
            "RunQueryService",
            [],
            ["QueryRunsAsync"],
            [],
            [],
            [],
            80,
            "medium",
            0.9);

        var recipes = planner.Emit(
            [plan with { ExtractionPatternId = "pattern:run-ops" }],
            [targetFile, abstractionFile],
            [
                new ExtractionPatternRecord(
                    "pattern:run-ops",
                    targetFile.Id,
                    "service_contract",
                    "IRunOperationsService",
                    null,
                    null,
                    "direct",
                    "services.AddScoped<IRunOperationsService, {ClassName}>()",
                    [],
                    [],
                    0.9)
            ],
            [CreateInterfaceSymbol(abstractionFile, "IRunOperationsService")]);

        var recipe = Assert.Single(recipes);
        Assert.Equal(
            "src/FileTransferTool.Infrastructure/Workers/RunQueryService.cs",
            recipe.CreateFile.Path);
        Assert.Equal(
            "FileTransferTool.Infrastructure.Workers",
            recipe.CreateFile.Namespace);
        Assert.Contains("source layer", recipe.CreateFile.PlacementReason, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(recipe.InterfaceFile);
        Assert.Equal(
            "src/FileTransferTool.Application/Abstractions/IRunOperationsService.cs",
            recipe.InterfaceFile!.Path);
        Assert.Equal(
            "FileTransferTool.Application.Abstractions",
            recipe.InterfaceFile.Namespace);
        Assert.Equal(
            "src/FileTransferTool.Application/Abstractions/IRunOperationsService.cs",
            recipe.InterfaceFile.ExistingPath);
    }

    [Fact]
    public void Emit_GeneratesNarrowerApplicationAbstraction_AtDepthTwo_WhenPatternInterfaceIsBroader()
    {
        var planner = new ExtractionRecipeEmitter();
        var targetFile = CreateFile(
            "file:run-ops",
            "src/FileTransferTool.Infrastructure/Workers/RunOperationsService.cs");
        var abstractionFile = CreateFile(
            "file:run-ops-interface",
            "src/FileTransferTool.Application/Abstractions/IRunOperationsService.cs");

        var plan = new SeamExtractionPlanRecord(
            "plan:run-query",
            targetFile.Id,
            "run_query",
            "extract_boundary_adapter",
            null,
            "RunQueryService",
            [],
            ["QueryRunsAsync"],
            [],
            [],
            [],
            80,
            "medium",
            0.9);

        var recipes = planner.Emit(
            [plan with { ExtractionPatternId = "pattern:run-ops" }],
            [targetFile, abstractionFile],
            [
                new ExtractionPatternRecord(
                    "pattern:run-ops",
                    targetFile.Id,
                    "service_contract",
                    "IRunOperationsService",
                    null,
                    null,
                    "direct",
                    "services.AddScoped<IRunOperationsService, {ClassName}>()",
                    [],
                    [],
                    0.9)
            ],
            [CreateInterfaceSymbol(abstractionFile, "IRunOperationsService")],
            refactorIntent: new RefactorIntent(RefactorDepth.ContractShaping, RefactorStyle.Balanced));

        var recipe = Assert.Single(recipes);
        Assert.Equal("IRunQueryService", recipe.CreateFile.InterfaceName);
        Assert.NotNull(recipe.ContractBoundaryDecision);
        Assert.Equal("generate_narrower", recipe.ContractBoundaryDecision!.Outcome);
        Assert.Equal("existing_interface_members_unresolved", recipe.ContractBoundaryDecision.Reason);
        Assert.NotNull(recipe.InterfaceFile);
        Assert.Equal(
            "src/FileTransferTool.Application/Abstractions/IRunQueryService.cs",
            recipe.InterfaceFile!.Path);
        Assert.Equal(
            "FileTransferTool.Application.Abstractions",
            recipe.InterfaceFile.Namespace);
        Assert.Null(recipe.InterfaceFile.ExistingPath);
        Assert.Contains("narrowed the contract", recipe.InterfaceFile.PlacementReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "services.AddScoped<IRunQueryService, RunQueryService>();",
            recipe.RegisterDi.RegistrationLine);
    }

    [Fact]
    public void Emit_ReusesExistingApplicationAbstraction_AtDepthTwo_WhenObservedContractIsAlreadyNarrow()
    {
        var planner = new ExtractionRecipeEmitter();
        var targetFile = CreateFile(
            "file:run-ops",
            "src/FileTransferTool.Infrastructure/Workers/RunOperationsService.cs");
        var abstractionFile = CreateFile(
            "file:run-ops-interface",
            "src/FileTransferTool.Application/Abstractions/IRunOperationsService.cs");

        var plan = new SeamExtractionPlanRecord(
            "plan:run-query",
            targetFile.Id,
            "run_query",
            "extract_boundary_adapter",
            null,
            "RunQueryService",
            [],
            ["QueryRunsAsync"],
            [],
            [],
            [],
            80,
            "medium",
            0.9);

        var interfaceSymbol = CreateInterfaceSymbol(abstractionFile, "IRunOperationsService");
        var narrowMember = new SymbolRecord(
            "symbol:IRunOperationsService.QueryRunsAsync",
            RepoId,
            abstractionFile.Id,
            ModuleId,
            "FileTransferTool.Application.Abstractions.IRunOperationsService.QueryRunsAsync",
            "QueryRunsAsync",
            "method",
            "csharp",
            "Task QueryRunsAsync(CancellationToken cancellationToken = default)",
            2,
            false);
        var sourceMember = new SymbolRecord(
            "symbol:RunOperationsService.QueryRunsAsync",
            RepoId,
            targetFile.Id,
            ModuleId,
            "FileTransferTool.Infrastructure.Workers.RunOperationsService.QueryRunsAsync",
            "QueryRunsAsync",
            "method",
            "csharp",
            "internal Task QueryRunsAsync(CancellationToken cancellationToken = default)",
            20,
            false);

        var recipes = planner.Emit(
            [plan with { ExtractionPatternId = "pattern:run-ops" }],
            [targetFile, abstractionFile],
            [
                new ExtractionPatternRecord(
                    "pattern:run-ops",
                    targetFile.Id,
                    "service_contract",
                    "IRunOperationsService",
                    null,
                    null,
                    "direct",
                    "services.AddScoped<IRunOperationsService, {ClassName}>()",
                    [],
                    [],
                    0.9)
            ],
            [interfaceSymbol, narrowMember, sourceMember],
            refactorIntent: new RefactorIntent(RefactorDepth.ContractShaping, RefactorStyle.Balanced));

        var recipe = Assert.Single(recipes);
        Assert.Equal("IRunOperationsService", recipe.CreateFile.InterfaceName);
        Assert.NotNull(recipe.InterfaceFile);
        Assert.Equal(
            "src/FileTransferTool.Application/Abstractions/IRunOperationsService.cs",
            recipe.InterfaceFile!.Path);
        Assert.Equal(
            "src/FileTransferTool.Application/Abstractions/IRunOperationsService.cs",
            recipe.InterfaceFile.ExistingPath);
        Assert.Equal(
            "services.AddScoped<IRunOperationsService, RunQueryService>();",
            recipe.RegisterDi.RegistrationLine);
    }

    [Fact]
    public void Emit_ReusesExistingApplicationAbstraction_AtDepthTwo_WhenMatchingMethodSurfaceIsInDifferentOrder()
    {
        var planner = new ExtractionRecipeEmitter();
        var targetFile = CreateFile(
            "file:run-ops",
            "src/FileTransferTool.Infrastructure/Workers/RunOperationsService.cs");
        var abstractionFile = CreateFile(
            "file:run-ops-interface",
            "src/FileTransferTool.Application/Abstractions/IRunOperationsService.cs");

        var plan = new SeamExtractionPlanRecord(
            "plan:run-query",
            targetFile.Id,
            "run_query",
            "extract_boundary_adapter",
            null,
            "RunQueryService",
            [],
            ["QueryRunsAsync", "CountRunsAsync"],
            [],
            [],
            [],
            80,
            "medium",
            0.9);

        var interfaceSymbol = CreateInterfaceSymbol(abstractionFile, "IRunOperationsService");
        var interfaceCount = new SymbolRecord(
            "symbol:IRunOperationsService.CountRunsAsync",
            RepoId,
            abstractionFile.Id,
            ModuleId,
            "FileTransferTool.Application.Abstractions.IRunOperationsService.CountRunsAsync",
            "CountRunsAsync",
            "method",
            "csharp",
            "Task<int> CountRunsAsync(Guid runId, CancellationToken cancellationToken = default)",
            2,
            false);
        var interfaceQuery = new SymbolRecord(
            "symbol:IRunOperationsService.QueryRunsAsync",
            RepoId,
            abstractionFile.Id,
            ModuleId,
            "FileTransferTool.Application.Abstractions.IRunOperationsService.QueryRunsAsync",
            "QueryRunsAsync",
            "method",
            "csharp",
            "Task<string> QueryRunsAsync(string query, CancellationToken cancellationToken = default)",
            3,
            false);
        var sourceQuery = new SymbolRecord(
            "symbol:RunOperationsService.QueryRunsAsync",
            RepoId,
            targetFile.Id,
            ModuleId,
            "FileTransferTool.Infrastructure.Workers.RunOperationsService.QueryRunsAsync",
            "QueryRunsAsync",
            "method",
            "csharp",
            "internal async Task<string> QueryRunsAsync(string query, CancellationToken cancellationToken = default)",
            20,
            false);
        var sourceCount = new SymbolRecord(
            "symbol:RunOperationsService.CountRunsAsync",
            RepoId,
            targetFile.Id,
            ModuleId,
            "FileTransferTool.Infrastructure.Workers.RunOperationsService.CountRunsAsync",
            "CountRunsAsync",
            "method",
            "csharp",
            "internal async Task<int> CountRunsAsync(Guid runId, CancellationToken cancellationToken = default)",
            25,
            false);

        var recipes = planner.Emit(
            [plan with { ExtractionPatternId = "pattern:run-ops" }],
            [targetFile, abstractionFile],
            [
                new ExtractionPatternRecord(
                    "pattern:run-ops",
                    targetFile.Id,
                    "service_contract",
                    "IRunOperationsService",
                    null,
                    null,
                    "direct",
                    "services.AddScoped<IRunOperationsService, {ClassName}>()",
                    [],
                    [],
                    0.9)
            ],
            [interfaceSymbol, interfaceCount, interfaceQuery, sourceQuery, sourceCount],
            refactorIntent: new RefactorIntent(RefactorDepth.ContractShaping, RefactorStyle.Balanced));

        var recipe = Assert.Single(recipes);
        Assert.Equal("IRunOperationsService", recipe.CreateFile.InterfaceName);
        Assert.NotNull(recipe.ContractBoundaryDecision);
        Assert.Equal("reuse_existing", recipe.ContractBoundaryDecision!.Outcome);
        Assert.Equal("normalized_member_surface_match", recipe.ContractBoundaryDecision.Reason);
        Assert.Equal(
            "src/FileTransferTool.Application/Abstractions/IRunOperationsService.cs",
            recipe.InterfaceFile!.Path);
        Assert.Equal(
            "services.AddScoped<IRunOperationsService, RunQueryService>();",
            recipe.RegisterDi.RegistrationLine);
    }

    [Fact]
    public void Emit_GeneratesNarrowerApplicationAbstraction_AtDepthTwo_WhenExistingMemberCountMatchesButSignaturesDoNot()
    {
        var planner = new ExtractionRecipeEmitter();
        var targetFile = CreateFile(
            "file:run-ops",
            "src/FileTransferTool.Infrastructure/Workers/RunOperationsService.cs");
        var abstractionFile = CreateFile(
            "file:run-ops-interface",
            "src/FileTransferTool.Application/Abstractions/IRunOperationsService.cs");

        var plan = new SeamExtractionPlanRecord(
            "plan:run-query",
            targetFile.Id,
            "run_query",
            "extract_boundary_adapter",
            null,
            "RunQueryService",
            [],
            ["QueryRunsAsync"],
            [],
            [],
            [],
            80,
            "medium",
            0.9);

        var interfaceSymbol = CreateInterfaceSymbol(abstractionFile, "IRunOperationsService");
        var differentMember = new SymbolRecord(
            "symbol:IRunOperationsService.QueryRunsAsync",
            RepoId,
            abstractionFile.Id,
            ModuleId,
            "FileTransferTool.Application.Abstractions.IRunOperationsService.QueryRunsAsync",
            "QueryRunsAsync",
            "method",
            "csharp",
            "Task<int> QueryRunsAsync(Guid runId, CancellationToken cancellationToken = default)",
            2,
            false);
        var sourceMember = new SymbolRecord(
            "symbol:RunOperationsService.QueryRunsAsync",
            RepoId,
            targetFile.Id,
            ModuleId,
            "FileTransferTool.Infrastructure.Workers.RunOperationsService.QueryRunsAsync",
            "QueryRunsAsync",
            "method",
            "csharp",
            "internal async Task<string> QueryRunsAsync(string query, CancellationToken cancellationToken = default)",
            20,
            false);

        var recipes = planner.Emit(
            [plan with { ExtractionPatternId = "pattern:run-ops" }],
            [targetFile, abstractionFile],
            [
                new ExtractionPatternRecord(
                    "pattern:run-ops",
                    targetFile.Id,
                    "service_contract",
                    "IRunOperationsService",
                    null,
                    null,
                    "direct",
                    "services.AddScoped<IRunOperationsService, {ClassName}>()",
                    [],
                    [],
                    0.9)
            ],
            [interfaceSymbol, differentMember, sourceMember],
            refactorIntent: new RefactorIntent(RefactorDepth.ContractShaping, RefactorStyle.Balanced));

        var recipe = Assert.Single(recipes);
        Assert.Equal("IRunQueryService", recipe.CreateFile.InterfaceName);
        Assert.NotNull(recipe.ContractBoundaryDecision);
        Assert.Equal("generate_narrower", recipe.ContractBoundaryDecision!.Outcome);
        Assert.Equal("normalized_member_surface_mismatch", recipe.ContractBoundaryDecision.Reason);
        Assert.Equal(
            "src/FileTransferTool.Application/Abstractions/IRunQueryService.cs",
            recipe.InterfaceFile!.Path);
        Assert.Equal(
            "services.AddScoped<IRunQueryService, RunQueryService>();",
            recipe.RegisterDi.RegistrationLine);
    }

    [Fact]
    public void Emit_GeneratesNarrowerApplicationAbstraction_AtDepthTwo_WhenContractMetadataDiffersDespiteMatchingSignatureShape()
    {
        var planner = new ExtractionRecipeEmitter();
        var targetFile = CreateFile(
            "file:run-ops",
            "src/FileTransferTool.Infrastructure/Workers/RunOperationsService.cs");
        var abstractionFile = CreateFile(
            "file:run-ops-interface",
            "src/FileTransferTool.Application/Abstractions/IRunOperationsService.cs");

        var plan = new SeamExtractionPlanRecord(
            "plan:run-query",
            targetFile.Id,
            "run_query",
            "extract_boundary_adapter",
            null,
            "RunQueryService",
            [],
            ["QueryRunsAsync"],
            [],
            [],
            [],
            80,
            "medium",
            0.9);

        var interfaceSymbol = CreateInterfaceSymbol(abstractionFile, "IRunOperationsService");
        var interfaceMember = new SymbolRecord(
            "symbol:IRunOperationsService.QueryRunsAsync",
            RepoId,
            abstractionFile.Id,
            ModuleId,
            "FileTransferTool.Application.Abstractions.IRunOperationsService.QueryRunsAsync",
            "QueryRunsAsync",
            "method",
            "csharp",
            "Task<string> QueryRunsAsync(string query, CancellationToken cancellationToken = default)",
            2,
            false);
        var sourceMember = new SymbolRecord(
            "symbol:RunOperationsService.QueryRunsAsync",
            RepoId,
            targetFile.Id,
            ModuleId,
            "FileTransferTool.Infrastructure.Workers.RunOperationsService.QueryRunsAsync",
            "QueryRunsAsync",
            "method",
            "csharp",
            "internal async Task<string> QueryRunsAsync(string query, CancellationToken cancellationToken = default)",
            20,
            false);

        ContractRecord[] contracts =
        [
            new(
                "contract:interface-query",
                interfaceMember.Id,
                ["string", "CancellationToken"],
                ["Task<string>"],
                [],
                [],
                [],
                new ContractNullability(false, false, true),
                "async",
                0.9),
            new(
                "contract:source-query",
                sourceMember.Id,
                ["string", "CancellationToken"],
                ["Task<string?>"],
                [],
                [],
                [],
                new ContractNullability(false, true, true),
                "async",
                0.9)
        ];

        var recipes = planner.Emit(
            [plan with { ExtractionPatternId = "pattern:run-ops" }],
            [targetFile, abstractionFile],
            [
                new ExtractionPatternRecord(
                    "pattern:run-ops",
                    targetFile.Id,
                    "service_contract",
                    "IRunOperationsService",
                    null,
                    null,
                    "direct",
                    "services.AddScoped<IRunOperationsService, {ClassName}>()",
                    [],
                    [],
                    0.9)
            ],
            [interfaceSymbol, interfaceMember, sourceMember],
            contracts,
            refactorIntent: new RefactorIntent(RefactorDepth.ContractShaping, RefactorStyle.Balanced));

        var recipe = Assert.Single(recipes);
        Assert.Equal("IRunQueryService", recipe.CreateFile.InterfaceName);
        Assert.NotNull(recipe.ContractBoundaryDecision);
        Assert.Equal("generate_narrower", recipe.ContractBoundaryDecision!.Outcome);
        Assert.Equal("normalized_member_surface_mismatch", recipe.ContractBoundaryDecision.Reason);
        Assert.Equal(
            "src/FileTransferTool.Application/Abstractions/IRunQueryService.cs",
            recipe.InterfaceFile!.Path);
        Assert.Equal(
            "services.AddScoped<IRunQueryService, RunQueryService>();",
            recipe.RegisterDi.RegistrationLine);
    }

    [Fact]
    public void Emit_FallsBackToLocalInterface_WhenApplicationPlacementWouldDependOnInfrastructure()
    {
        var planner = new ExtractionRecipeEmitter();
        var targetFile = CreateFile(
            "file:run-ops",
            "src/FileTransferTool.Infrastructure/Workers/RunOperationsService.cs");
        var abstractionsAnchor = CreateFile(
            "file:anchor",
            "src/FileTransferTool.Application/Abstractions/IAuditService.cs");
        var dbContextFile = CreateFile(
            "file:dbcontext",
            "src/FileTransferTool.Infrastructure/Data/AppDbContext.cs");

        var methodSymbol = new SymbolRecord(
            "symbol:dangerous",
            RepoId,
            targetFile.Id,
            ModuleId,
            "FileTransferTool.Infrastructure.Workers.RunOperationsService.BuildQueryAsync",
            "BuildQueryAsync",
            "method",
            "csharp",
            "public Task<AppDbContext> BuildQueryAsync(CancellationToken cancellationToken = default)",
            10,
            false);

        var contract = new ContractRecord(
            "contract:dangerous",
            methodSymbol.Id,
            ["CancellationToken"],
            ["Task<AppDbContext>"],
            [],
            [],
            [],
            new ContractNullability(false, false, true),
            "async",
            0.9);

        var plan = new SeamExtractionPlanRecord(
            "plan:dangerous",
            targetFile.Id,
            "run_query",
            "extract_boundary_adapter",
            null,
            "RunQueryService",
            [],
            ["BuildQueryAsync"],
            [],
            [],
            [],
            80,
            "high",
            0.9);

        var recipes = planner.Emit(
            [plan],
            [targetFile, abstractionsAnchor, dbContextFile],
            [],
            [methodSymbol],
            [contract]);

        var recipe = Assert.Single(recipes);
        Assert.NotNull(recipe.InterfaceFile);
        Assert.Equal(
            "src/FileTransferTool.Infrastructure/Workers/IRunQueryService.cs",
            recipe.InterfaceFile!.Path);
        Assert.Equal(
            "FileTransferTool.Infrastructure.Workers",
            recipe.InterfaceFile.Namespace);
        Assert.Contains("beside the implementation", recipe.InterfaceFile.PlacementReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Emit_FallsBackToLocalInterface_WhenRawMethodSignatureReferencesInfrastructureType()
    {
        var planner = new ExtractionRecipeEmitter();
        var targetFile = CreateFile(
            "file:run-execution",
            "src/FileTransferTool.Infrastructure/Workers/RunExecutionService.cs");
        var abstractionsAnchor = CreateFile(
            "file:anchor",
            "src/FileTransferTool.Application/Abstractions/IAuditService.cs");
        var workflowDefinitionFile = CreateFile(
            "file:workflow-definition",
            "src/FileTransferTool.Application/Workflows/WorkflowDefinition.cs");
        var workflowStepDefinitionFile = CreateFile(
            "file:workflow-step-definition",
            "src/FileTransferTool.Application/Workflows/WorkflowStepDefinition.cs");
        var workflowContextFile = CreateFile(
            "file:workflow-context",
            "src/FileTransferTool.Infrastructure/Workers/WorkflowExecutionContext.cs");

        var methodSymbol = new SymbolRecord(
            "symbol:subworkflow",
            RepoId,
            targetFile.Id,
            ModuleId,
            "FileTransferTool.Infrastructure.Workers.RunExecutionService.ExecuteSubWorkflowByNameAsync",
            "ExecuteSubWorkflowByNameAsync",
            "method",
            "csharp",
            "private async Task<string> ExecuteSubWorkflowByNameAsync(string subWorkflowName, WorkflowDefinition workflow, Guid runId, WorkflowStepDefinition parentStep, WorkflowExecutionContext context, Func<int> nextTraceSequence, CancellationToken cancellationToken)",
            10,
            false);

        var incompleteContract = new ContractRecord(
            "contract:subworkflow",
            methodSymbol.Id,
            ["string", "WorkflowDefinition", "Guid", "WorkflowStepDefinition", "Func<int>", "CancellationToken"],
            ["Task<string>"],
            [],
            [],
            [],
            new ContractNullability(false, false, false),
            "async",
            0.7);

        var plan = new SeamExtractionPlanRecord(
            "plan:workflow",
            targetFile.Id,
            "workflow",
            "extract_policy",
            null,
            "RunExecutionWorkflowStepExecutor",
            ["InvokeWorkflow"],
            ["ExecuteSubWorkflowByNameAsync"],
            [],
            [],
            [],
            120,
            "medium",
            0.8);

        var recipes = planner.Emit(
            [plan],
            [targetFile, abstractionsAnchor, workflowDefinitionFile, workflowStepDefinitionFile, workflowContextFile],
            [],
            [methodSymbol],
            [incompleteContract]);

        var recipe = Assert.Single(recipes);
        Assert.NotNull(recipe.InterfaceFile);
        Assert.Equal(
            "src/FileTransferTool.Infrastructure/Workers/IRunExecutionWorkflowStepExecutor.cs",
            recipe.InterfaceFile!.Path);
        Assert.Equal(
            "FileTransferTool.Infrastructure.Workers",
            recipe.InterfaceFile.Namespace);
        Assert.Contains("beside the implementation", recipe.InterfaceFile.PlacementReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Emit_FallsBackToLocalInterface_WhenPlannerCannotResolveAllExtractedMethods()
    {
        var planner = new ExtractionRecipeEmitter();
        var targetFile = CreateFile(
            "file:run-execution",
            "src/FileTransferTool.Infrastructure/Workers/RunExecutionService.cs");
        var abstractionsAnchor = CreateFile(
            "file:anchor",
            "src/FileTransferTool.Application/Abstractions/IAuditService.cs");
        var workflowDefinitionFile = CreateFile(
            "file:workflow-definition",
            "src/FileTransferTool.Application/Workflows/WorkflowDefinition.cs");

        var methodSymbol = new SymbolRecord(
            "symbol:invoke-workflow",
            RepoId,
            targetFile.Id,
            ModuleId,
            "FileTransferTool.Infrastructure.Workers.RunExecutionService.ExecuteInvokeWorkflowStepAsync",
            "ExecuteInvokeWorkflowStepAsync",
            "method",
            "csharp",
            "private async Task<string> ExecuteInvokeWorkflowStepAsync(WorkflowDefinition workflow, CancellationToken cancellationToken)",
            20,
            false);

        var contract = new ContractRecord(
            "contract:invoke-workflow",
            methodSymbol.Id,
            ["WorkflowDefinition", "CancellationToken"],
            ["Task<string>"],
            [],
            [],
            [],
            new ContractNullability(false, false, false),
            "async",
            0.8);

        var plan = new SeamExtractionPlanRecord(
            "plan:workflow",
            targetFile.Id,
            "workflow",
            "extract_policy",
            null,
            "RunExecutionWorkflowStepExecutor",
            ["InvokeWorkflow"],
            ["ExecuteInvokeWorkflowStepAsync", "ExecuteSubWorkflowByNameAsync"],
            [],
            [],
            [],
            120,
            "medium",
            0.8);

        var recipes = planner.Emit(
            [plan],
            [targetFile, abstractionsAnchor, workflowDefinitionFile],
            [],
            [methodSymbol],
            [contract]);

        var recipe = Assert.Single(recipes);
        Assert.NotNull(recipe.InterfaceFile);
        Assert.Equal(
            "src/FileTransferTool.Infrastructure/Workers/IRunExecutionWorkflowStepExecutor.cs",
            recipe.InterfaceFile!.Path);
        Assert.Equal(
            "FileTransferTool.Infrastructure.Workers",
            recipe.InterfaceFile.Namespace);
        Assert.Contains("beside the implementation", recipe.InterfaceFile.PlacementReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Emit_UsesConcreteMethodNameForDispatchRewire_WhenExtractedStepMethodIsSynchronous()
    {
        var emitter = new ExtractionRecipeEmitter();
        var targetFile = CreateFile(
            "file:run-execution",
            "src/FileTransferTool.Infrastructure/Workers/RunExecutionService.cs");

        var plan = new SeamExtractionPlanRecord(
            "plan:email",
            targetFile.Id,
            "email",
            "extract_policy",
            null,
            "RunExecutionEmailStepExecutor",
            ["SaveEmailTriggerAttachments", "ExtractEmailTriggerData"],
            ["ExecuteSaveEmailTriggerAttachmentsStepAsync", "ExecuteExtractEmailTriggerDataStep"],
            [],
            [],
            [],
            80,
            "low",
            0.8);

        var recipe = Assert.Single(emitter.Emit([plan], [targetFile], [], []));

        Assert.Contains(recipe.ModifyGodClass.DispatchRewires, rewire =>
            rewire.StepType == "WorkflowStepType.SaveEmailTriggerAttachments" &&
            rewire.OldPattern == "await ExecuteSaveEmailTriggerAttachmentsStepAsync(");
        Assert.Contains(recipe.ModifyGodClass.DispatchRewires, rewire =>
            rewire.StepType == "WorkflowStepType.ExtractEmailTriggerData" &&
            rewire.OldPattern == "ExecuteExtractEmailTriggerDataStep(");
    }

    [Fact]
    public void Emit_UsesConfiguredArchitecturePlacementRules_ForPortsDirectory()
    {
        var rules = ArchitecturePlacementRules.Default with
        {
            InterfacePlacement = ArchitecturePlacementRules.Default.InterfacePlacement with
            {
                SourceRootDirectoryNames = ["source"],
                AbstractionLayerNames = ["UseCases"],
                AbstractionDirectoryNames = ["Ports"],
                PreferredAbstractionDirectoryNames = ["Ports"],
                PreferredExistingInterfacePathContains = ["UseCases/Ports/"],
                AllowedContractLayers = ["domain", "usecases"],
                Layers =
                [
                    new ArchitectureLayerRule("domain", [".Domain"], ["Domain"]),
                    new ArchitectureLayerRule("usecases", [".UseCases"], ["UseCases"]),
                    new ArchitectureLayerRule("infrastructure", [".Adapters"], ["Adapters"])
                ]
            }
        };
        var emitter = new ExtractionRecipeEmitter(rules);
        var targetFile = CreateFile(
            "file:handler",
            "source/Acme.Adapters/Handlers/RunHandler.cs");
        var portsAnchor = CreateFile(
            "file:ports-anchor",
            "source/Acme.UseCases/Ports/IExistingPort.cs");
        var methodSymbol = new SymbolRecord(
            "symbol:run",
            RepoId,
            targetFile.Id,
            ModuleId,
            "Acme.Adapters.Handlers.RunHandler.RunAsync",
            "RunAsync",
            "method",
            "csharp",
            "public Task RunAsync(CancellationToken cancellationToken)",
            10,
            false);
        var contract = new ContractRecord(
            "contract:run",
            methodSymbol.Id,
            ["CancellationToken"],
            ["Task"],
            [],
            [],
            [],
            new ContractNullability(false, false, false),
            "async",
            0.9);
        var plan = new SeamExtractionPlanRecord(
            "plan:run",
            targetFile.Id,
            "run",
            "extract_policy",
            null,
            "RunPolicy",
            [],
            ["RunAsync"],
            [],
            [],
            [],
            20,
            "low",
            0.8);

        var recipe = Assert.Single(emitter.Emit([plan], [targetFile, portsAnchor], [], [methodSymbol], [contract]));

        Assert.Equal("source/Acme.Adapters/Handlers/RunPolicy.cs", recipe.CreateFile.Path);
        Assert.Equal("Acme.Adapters.Handlers", recipe.CreateFile.Namespace);
        Assert.NotNull(recipe.InterfaceFile);
        Assert.Equal("source/Acme.UseCases/Ports/IRunPolicy.cs", recipe.InterfaceFile!.Path);
        Assert.Equal("Acme.UseCases.Ports", recipe.InterfaceFile.Namespace);
    }

    private const string RepoId = "repo:test";
    private const string ModuleId = "module:test";

    private static FileRecord CreateFile(string id, string normalizedPath)
    {
        return new FileRecord(
            id,
            RepoId,
            normalizedPath,
            normalizedPath,
            "csharp",
            false,
            false,
            ModuleId);
    }

    private static SymbolRecord CreateInterfaceSymbol(FileRecord file, string name)
    {
        return new SymbolRecord(
            "symbol:" + name,
            RepoId,
            file.Id,
            ModuleId,
            $"FileTransferTool.Application.Abstractions.{name}",
            name,
            "interface",
            "csharp",
            $"public interface {name}",
            1,
            true);
    }
}
