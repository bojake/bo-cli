using BO.Core.Indexing;
using System.Text.Json;

namespace BO.Core.Persistence;

public static class GraphRecordFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static GraphWriteBatch CreateIndexBatch(IndexResult result)
    {
        var moduleNodes = result.Files
            .Select(file => new { file.ModuleId, file.RepoId })
            .Distinct()
            .Select(module => CreateModuleNode(module.ModuleId, module.RepoId))
            .ToArray();

        var fileNodes = result.Files.Select(CreateFileNode).ToArray();
        var symbolNodes = result.Symbols.Select(CreateSymbolNode).ToArray();
        var contractNodes = result.Contracts.Select(CreateContractNode).ToArray();
        var boundaryNodes = result.BoundaryInteractions.Select(CreateBoundaryInteractionNode).ToArray();
        var effectProfileNodes = result.EffectProfiles.Select(CreateEffectProfileNode).ToArray();
        var complexityProfileNodes = result.ComplexityProfiles.Select(CreateComplexityProfileNode).ToArray();
        var responsibilityProfileNodes = result.ResponsibilityProfiles.Select(CreateResponsibilityProfileNode).ToArray();
        var rpsNodes = result.RefactorPressureScores.Select(CreateRefactorPressureScoreNode).ToArray();
        var decisionNodes = result.RefactorDecisions.Select(CreateRefactorDecisionNode).ToArray();
        var extractionPlanNodes = result.SeamExtractionPlans.Select(CreateSeamExtractionPlanNode).ToArray();
        var repoNode = CreateRepoNode(result.Repo);

        var repoToModules = moduleNodes
            .Select(node => new GraphEdgeRecord(
                $"edge:{result.Repo.Id}:contains_module:{node.Id}",
                "CONTAINS_MODULE",
                result.Repo.Id,
                node.Id,
                new Dictionary<string, object?> { ["id"] = $"edge:{result.Repo.Id}:contains_module:{node.Id}" }))
            .ToArray();

        var repoToFiles = result.Files
            .Select(file => new GraphEdgeRecord(
                $"edge:{result.Repo.Id}:contains_file:{file.Id}",
                "CONTAINS_FILE",
                result.Repo.Id,
                file.Id,
                new Dictionary<string, object?> { ["id"] = $"edge:{result.Repo.Id}:contains_file:{file.Id}" }))
            .ToArray();

        var moduleToFiles = result.Files
            .Select(file => new GraphEdgeRecord(
                $"edge:{file.ModuleId}:contains_file:{file.Id}",
                "MODULE_CONTAINS_FILE",
                file.ModuleId,
                file.Id,
                new Dictionary<string, object?> { ["id"] = $"edge:{file.ModuleId}:contains_file:{file.Id}" }))
            .ToArray();

        var fileToSymbols = result.Symbols
            .Select(symbol => new GraphEdgeRecord(
                $"edge:{symbol.FileId}:defines_symbol:{symbol.Id}",
                "DEFINES_SYMBOL",
                symbol.FileId,
                symbol.Id,
                new Dictionary<string, object?> { ["id"] = $"edge:{symbol.FileId}:defines_symbol:{symbol.Id}" }))
            .ToArray();

        var moduleToSymbols = result.Symbols
            .Select(symbol => new GraphEdgeRecord(
                $"edge:{symbol.ModuleId}:contains_symbol:{symbol.Id}",
                "CONTAINS_SYMBOL",
                symbol.ModuleId,
                symbol.Id,
                new Dictionary<string, object?> { ["id"] = $"edge:{symbol.ModuleId}:contains_symbol:{symbol.Id}" }))
            .ToArray();

        var symbolContracts = result.Contracts
            .Select(contract => new GraphEdgeRecord(
                $"edge:{contract.SymbolId}:has_contract:{contract.Id}",
                "HAS_CONTRACT",
                contract.SymbolId,
                contract.Id,
                new Dictionary<string, object?>
                {
                    ["id"] = $"edge:{contract.SymbolId}:has_contract:{contract.Id}",
                    ["confidence"] = contract.Confidence
                }))
            .ToArray();

        var fileImports = result.Dependencies
            .Select(dependency => new GraphEdgeRecord(
                dependency.Id,
                "IMPORTS",
                dependency.FromFileId,
                dependency.ToFileId,
                new Dictionary<string, object?>
                {
                    ["id"] = dependency.Id,
                    ["import_text"] = dependency.ImportText,
                    ["is_runtime"] = dependency.IsRuntime,
                    ["is_compile_time"] = dependency.IsCompileTime
                }))
            .ToArray();

        var symbolDependencyEdges = result.SymbolDependencies
            .Select(dependency => new GraphEdgeRecord(
                dependency.Id,
                dependency.RelationType switch
                {
                    "instantiates" => "INSTANTIATES",
                    "uses_type" => "USES_TYPE",
                    _ => "CALLS"
                },
                dependency.FromSymbolId,
                dependency.ToSymbolId,
                new Dictionary<string, object?>
                {
                    ["id"] = dependency.Id,
                    ["relation_type"] = dependency.RelationType,
                    ["evidence"] = dependency.Evidence,
                    ["confidence"] = dependency.Confidence
                }))
            .ToArray();

        var boundaryEdges = result.BoundaryInteractions
            .Select(interaction => new GraphEdgeRecord(
                $"edge:{interaction.FileId}:crosses_boundary:{interaction.Id}",
                "CROSSES_BOUNDARY",
                interaction.FileId,
                interaction.Id,
                new Dictionary<string, object?>
                {
                    ["id"] = $"edge:{interaction.FileId}:crosses_boundary:{interaction.Id}",
                    ["boundary_type"] = interaction.BoundaryType,
                    ["operation_type"] = interaction.OperationType,
                    ["effect_mode"] = interaction.EffectMode,
                    ["confidence"] = interaction.Confidence
                }))
            .ToArray();

        var effectProfileEdges = result.EffectProfiles
            .Select(profile => new GraphEdgeRecord(
                $"edge:{profile.TargetId}:has_effect_profile:{profile.Id}",
                "HAS_EFFECT_PROFILE",
                profile.TargetId,
                profile.Id,
                new Dictionary<string, object?>
                {
                    ["id"] = $"edge:{profile.TargetId}:has_effect_profile:{profile.Id}",
                    ["target_kind"] = profile.TargetKind,
                    ["confidence"] = profile.Confidence
                }))
            .ToArray();

        var complexityProfileEdges = result.ComplexityProfiles
            .Select(profile => new GraphEdgeRecord(
                $"edge:{profile.TargetId}:has_complexity_profile:{profile.Id}",
                "HAS_COMPLEXITY_PROFILE",
                profile.TargetId,
                profile.Id,
                new Dictionary<string, object?>
                {
                    ["id"] = $"edge:{profile.TargetId}:has_complexity_profile:{profile.Id}",
                    ["target_kind"] = profile.TargetKind,
                    ["confidence"] = profile.Confidence
                }))
            .ToArray();

        var responsibilityProfileEdges = result.ResponsibilityProfiles
            .Select(profile => new GraphEdgeRecord(
                $"edge:{profile.TargetId}:has_responsibility_profile:{profile.Id}",
                "HAS_RESPONSIBILITY_PROFILE",
                profile.TargetId,
                profile.Id,
                new Dictionary<string, object?>
                {
                    ["id"] = $"edge:{profile.TargetId}:has_responsibility_profile:{profile.Id}",
                    ["target_kind"] = profile.TargetKind,
                    ["confidence"] = profile.Confidence
                }))
            .ToArray();

        var rpsEdges = result.RefactorPressureScores
            .Select(rps => new GraphEdgeRecord(
                $"edge:{rps.TargetId}:has_rps:{rps.Id}",
                "HAS_RPS",
                rps.TargetId,
                rps.Id,
                new Dictionary<string, object?>
                {
                    ["id"] = $"edge:{rps.TargetId}:has_rps:{rps.Id}",
                    ["target_kind"] = rps.TargetKind,
                    ["confidence"] = rps.Confidence
                }))
            .ToArray();

        var decisionEdges = result.RefactorDecisions
            .Select(decision => new GraphEdgeRecord(
                $"edge:{decision.TargetId}:has_refactor_decision:{decision.Id}",
                "HAS_REFACTOR_DECISION",
                decision.TargetId,
                decision.Id,
                new Dictionary<string, object?>
                {
                    ["id"] = $"edge:{decision.TargetId}:has_refactor_decision:{decision.Id}",
                    ["recommendation"] = decision.Recommendation,
                    ["confidence"] = decision.Confidence
                }))
            .ToArray();

        var extractionPlanEdges = result.SeamExtractionPlans
            .Select(plan => new GraphEdgeRecord(
                $"edge:{plan.TargetFileId}:has_extraction_plan:{plan.Id}",
                "HAS_EXTRACTION_PLAN",
                plan.TargetFileId,
                plan.Id,
                new Dictionary<string, object?>
                {
                    ["id"] = $"edge:{plan.TargetFileId}:has_extraction_plan:{plan.Id}",
                    ["seam_name"] = plan.SeamName,
                    ["confidence"] = plan.Confidence
                }))
            .ToArray();

        return new GraphWriteBatch(
            [repoNode, .. moduleNodes, .. fileNodes, .. symbolNodes, .. contractNodes, .. boundaryNodes, .. effectProfileNodes, .. complexityProfileNodes, .. responsibilityProfileNodes, .. rpsNodes, .. decisionNodes, .. extractionPlanNodes],
            [.. repoToModules, .. repoToFiles, .. moduleToFiles, .. fileToSymbols, .. moduleToSymbols, .. symbolContracts, .. fileImports, .. symbolDependencyEdges, .. boundaryEdges, .. effectProfileEdges, .. complexityProfileEdges, .. responsibilityProfileEdges, .. rpsEdges, .. decisionEdges, .. extractionPlanEdges]);
    }

    private static GraphNodeRecord CreateRepoNode(RepoRecord repo)
    {
        return new GraphNodeRecord(
            repo.Id,
            "Repo",
            new Dictionary<string, object?>
            {
                ["id"] = repo.Id,
                ["name"] = repo.Name,
                ["root_path"] = repo.RootPath,
                ["languages_json"] = string.Join(",", repo.Languages),
                ["source_version"] = repo.SourceVersion
            });
    }

    private static GraphNodeRecord CreateModuleNode(string moduleId, string repoId)
    {
        return new GraphNodeRecord(
            moduleId,
            "Module",
            new Dictionary<string, object?>
            {
                ["id"] = moduleId,
                ["repo_id"] = repoId,
                ["qualified_name"] = moduleId
            });
    }

    private static GraphNodeRecord CreateFileNode(FileRecord file)
    {
        return new GraphNodeRecord(
            file.Id,
            "File",
            new Dictionary<string, object?>
            {
                ["id"] = file.Id,
                ["repo_id"] = file.RepoId,
                ["path"] = file.Path,
                ["normalized_path"] = file.NormalizedPath,
                ["language"] = file.Language,
                ["is_test"] = file.IsTest,
                ["is_generated"] = file.IsGenerated,
                ["module_id"] = file.ModuleId
            });
    }

    private static GraphNodeRecord CreateSymbolNode(SymbolRecord symbol)
    {
        return new GraphNodeRecord(
            symbol.Id,
            "Symbol",
            new Dictionary<string, object?>
            {
                ["id"] = symbol.Id,
                ["repo_id"] = symbol.RepoId,
                ["file_id"] = symbol.FileId,
                ["module_id"] = symbol.ModuleId,
                ["qualified_name"] = symbol.QualifiedName,
                ["display_name"] = symbol.DisplayName,
                ["kind"] = symbol.Kind,
                ["language"] = symbol.Language,
                ["signature"] = symbol.Signature,
                ["declaration_line"] = symbol.DeclarationLine,
                ["is_exported"] = symbol.IsExported
            });
    }

    private static GraphNodeRecord CreateContractNode(ContractRecord contract)
    {
        return new GraphNodeRecord(
            contract.Id,
            "Contract",
            new Dictionary<string, object?>
            {
                ["id"] = contract.Id,
                ["symbol_id"] = contract.SymbolId,
                ["input_types_json"] = string.Join(",", contract.InputTypes),
                ["output_types_json"] = string.Join(",", contract.OutputTypes),
                ["generic_constraints_json"] = string.Join(",", contract.GenericConstraints),
                ["throws_or_error_modes_json"] = string.Join(",", contract.ThrowsOrErrorModes),
                ["schema_shapes_json"] = JsonSerializer.Serialize(contract.SchemaShapes, JsonOptions),
                ["nullability_json"] = JsonSerializer.Serialize(contract.Nullability, JsonOptions),
                ["async_mode"] = contract.AsyncMode,
                ["confidence"] = contract.Confidence
            });
    }

    private static GraphNodeRecord CreateBoundaryInteractionNode(BoundaryInteractionRecord interaction)
    {
        return new GraphNodeRecord(
            interaction.Id,
            "BoundaryInteraction",
            new Dictionary<string, object?>
            {
                ["id"] = interaction.Id,
                ["file_id"] = interaction.FileId,
                ["boundary_type"] = interaction.BoundaryType,
                ["operation_type"] = interaction.OperationType,
                ["target_name"] = interaction.TargetName,
                ["effect_mode"] = interaction.EffectMode,
                ["confidence"] = interaction.Confidence
            });
    }

    private static GraphNodeRecord CreateEffectProfileNode(EffectProfileRecord profile)
    {
        return new GraphNodeRecord(
            profile.Id,
            "EffectProfile",
            new Dictionary<string, object?>
            {
                ["id"] = profile.Id,
                ["target_id"] = profile.TargetId,
                ["target_kind"] = profile.TargetKind,
                ["reads_state"] = profile.ReadsState,
                ["writes_state"] = profile.WritesState,
                ["emits_events"] = profile.EmitsEvents,
                ["calls_external_service"] = profile.CallsExternalService,
                ["mutates_input"] = profile.MutatesInput,
                ["has_retry_logic"] = profile.HasRetryLogic,
                ["has_transaction_logic"] = profile.HasTransactionLogic,
                ["has_auth_logic"] = profile.HasAuthLogic,
                ["has_validation_logic"] = profile.HasValidationLogic,
                ["has_caching_logic"] = profile.HasCachingLogic,
                ["has_logging_logic"] = profile.HasLoggingLogic,
                ["side_effect_classes_json"] = string.Join(",", profile.SideEffectClasses),
                ["confidence"] = profile.Confidence
            });
    }

    private static GraphNodeRecord CreateComplexityProfileNode(ComplexityProfileRecord profile)
    {
        return new GraphNodeRecord(
            profile.Id,
            "ComplexityProfile",
            new Dictionary<string, object?>
            {
                ["id"] = profile.Id,
                ["target_id"] = profile.TargetId,
                ["target_kind"] = profile.TargetKind,
                ["loc"] = profile.Loc,
                ["cognitive_complexity"] = profile.CognitiveComplexity,
                ["cyclomatic_complexity"] = profile.CyclomaticComplexity,
                ["nesting_depth"] = profile.NestingDepth,
                ["parameter_count"] = profile.ParameterCount,
                ["branch_count"] = profile.BranchCount,
                ["side_effect_count"] = profile.SideEffectCount,
                ["fan_in"] = profile.FanIn,
                ["fan_out"] = profile.FanOut,
                ["confidence"] = profile.Confidence
            });
    }

    private static GraphNodeRecord CreateResponsibilityProfileNode(ResponsibilityProfileRecord profile)
    {
        return new GraphNodeRecord(
            profile.Id,
            "ResponsibilityProfile",
            new Dictionary<string, object?>
            {
                ["id"] = profile.Id,
                ["target_id"] = profile.TargetId,
                ["target_kind"] = profile.TargetKind,
                ["boundary_type_count"] = profile.BoundaryTypeCount,
                ["dependency_category_count"] = profile.DependencyCategoryCount,
                ["capability_cluster_count"] = profile.CapabilityClusterCount,
                ["side_effect_class_count"] = profile.SideEffectClassCount,
                ["responsibility_spread_score"] = profile.ResponsibilitySpreadScore,
                ["dominant_responsibilities_json"] = string.Join(",", profile.DominantResponsibilities),
                ["confidence"] = profile.Confidence
            });
    }

    private static GraphNodeRecord CreateRefactorPressureScoreNode(RefactorPressureScoreRecord rps)
    {
        return new GraphNodeRecord(
            rps.Id,
            "RefactorPressureScore",
            new Dictionary<string, object?>
            {
                ["id"] = rps.Id,
                ["target_id"] = rps.TargetId,
                ["target_kind"] = rps.TargetKind,
                ["score"] = rps.Score,
                ["recommendation"] = rps.Recommendation,
                ["drivers_json"] = string.Join(",", rps.Drivers),
                ["fired_gates_json"] = string.Join(",", rps.FiredGates),
                ["confidence"] = rps.Confidence
            });
    }

    private static GraphNodeRecord CreateRefactorDecisionNode(RefactorDecisionRecord decision)
    {
        return new GraphNodeRecord(
            decision.Id,
            "RefactorDecision",
            new Dictionary<string, object?>
            {
                ["id"] = decision.Id,
                ["target_id"] = decision.TargetId,
                ["recommendation"] = decision.Recommendation,
                ["pivot_type"] = decision.PivotType,
                ["drivers_json"] = string.Join(",", decision.Drivers),
                ["fired_gates_json"] = string.Join(",", decision.FiredGates),
                ["candidate_seams_json"] = string.Join(",", decision.CandidateSeams),
                ["rps_before"] = decision.RpsBefore,
                ["confidence"] = decision.Confidence
            });
    }
    private static GraphNodeRecord CreateSeamExtractionPlanNode(SeamExtractionPlanRecord plan)
    {
        return new GraphNodeRecord(
            plan.Id,
            "SeamExtractionPlan",
            new Dictionary<string, object?>
            {
                ["id"] = plan.Id,
                ["target_file_id"] = plan.TargetFileId,
                ["seam_name"] = plan.SeamName,
                ["pivot_type"] = plan.PivotType,
                ["proposed_class_name"] = plan.ProposedClassName,
                ["step_types_json"] = string.Join(",", plan.StepTypesToRoute),
                ["methods_to_extract_json"] = string.Join(",", plan.MethodsToExtract),
                ["service_injections_json"] = string.Join(",", plan.ServiceInjectionsNeeded),
                ["records_to_move_json"] = string.Join(",", plan.RecordsToMove),
                ["dependency_count"] = plan.Dependencies.Count,
                ["moves_count"] = plan.Dependencies.Count(d => d.Classification == "moves"),
                ["stays_count"] = plan.Dependencies.Count(d => d.Classification == "stays_injected"),
                ["promotion_count"] = plan.Dependencies.Count(d => d.Classification == "needs_promotion"),
                ["estimated_loc_reduction"] = plan.EstimatedLocReduction,
                ["risk"] = plan.Risk,
                ["confidence"] = plan.Confidence
            });
    }
}
