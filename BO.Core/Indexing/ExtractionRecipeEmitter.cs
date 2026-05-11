using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BO.Core.Indexing;

/// <summary>
/// Converts <see cref="SeamExtractionPlanRecord"/>s into <see cref="ExtractionRecipe"/>s
/// that contain all the information needed to execute the extraction.
/// </summary>
public sealed class ExtractionRecipeEmitter
{
    private readonly NamespacePlacementPlanner _placementPlanner;

    public ExtractionRecipeEmitter(ArchitecturePlacementRules? architectureRules = null)
    {
        _placementPlanner = new NamespacePlacementPlanner(architectureRules);
    }

    /// <summary>
    /// Emits extraction recipes from seam extraction plans.
    /// </summary>
    public IReadOnlyList<ExtractionRecipe> Emit(
        IReadOnlyList<SeamExtractionPlanRecord> plans,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<ExtractionPatternRecord> patterns,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyList<ContractRecord>? contracts = null,
        RefactorIntent? refactorIntent = null)
    {
        var filesById = files.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var symbolsById = symbols.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var contractRecords = contracts ?? [];
        var contractsBySymbolId = contractRecords.ToDictionary(contract => contract.SymbolId, StringComparer.Ordinal);
        var effectiveIntent = refactorIntent ?? RefactorIntent.Default;
        var allowedTransformationFamilies = BuildAllowedTransformationFamilies(effectiveIntent);

        var recipes = new List<ExtractionRecipe>();
        var generalizationSurfaceKeys = new List<string?>();
        var namedGeneralizationSurfaceKeys = new List<string?>();

        foreach (var plan in plans)
        {
            if (!filesById.TryGetValue(plan.TargetFileId, out var targetFile))
            {
                continue;
            }

            // Find extraction pattern if available
            ExtractionPatternInfo? patternInfo = null;
            if (plan.ExtractionPatternId is not null)
            {
                var matchedPattern = patterns.FirstOrDefault(p => p.Id == plan.ExtractionPatternId);
                if (matchedPattern is not null)
                {
                    patternInfo = new ExtractionPatternInfo
                    {
                        PatternType = matchedPattern.PatternType,
                        InterfaceName = matchedPattern.InterfaceName,
                        DispatcherName = matchedPattern.DispatcherSymbolId is not null
                            ? symbolsById.TryGetValue(matchedPattern.DispatcherSymbolId, out var dSym)
                                ? dSym.DisplayName
                                : null
                            : null,
                        ExemplarFile = matchedPattern.ExemplarSymbolIds.Count > 0
                            ? symbolsById.TryGetValue(matchedPattern.ExemplarSymbolIds[0], out var eSym)
                                ? filesById.TryGetValue(eSym.FileId, out var eFile)
                                    ? eFile.NormalizedPath
                                    : null
                                : null
                            : null,
                        DiRegistrationPattern = matchedPattern.DiRegistrationPattern
                    };
                }
            }

            // Level 2+ prefers a contract named for the extracted collaborator rather than
            // inheriting a broader repo-level service contract verbatim.
            var contractBoundaryDecision = ResolveContractBoundaryDecision(
                effectiveIntent,
                patternInfo,
                plan,
                symbols,
                files,
                contractsBySymbolId,
                plan.ProposedClassName);
            var interfaceName = contractBoundaryDecision.InterfaceName;
            var placement = _placementPlanner.Resolve(
                plan,
                targetFile,
                files,
                symbols,
                contractRecords,
                interfaceName,
                patternInfo?.InterfaceName);

            // Build step types
            var stepTypes = plan.StepTypesToRoute;

            // Classify dependencies into helpers-that-move and injections
            var helpersThatMove = plan.Dependencies
                .Where(d => d.Classification == "moves")
                .Select(d => new MethodToCopy { Name = d.SymbolDisplayName })
                .ToList();

            var methodsToCopy = plan.MethodsToExtract
                .Select(name =>
                {
                    string? stepType = null;
                    if (name.StartsWith("Execute", StringComparison.Ordinal) &&
                        name.EndsWith("StepAsync", StringComparison.Ordinal))
                    {
                        stepType = name["Execute".Length..^"StepAsync".Length];
                    }
                    else if (name.StartsWith("Execute", StringComparison.Ordinal) &&
                             name.EndsWith("Step", StringComparison.Ordinal))
                    {
                        stepType = name["Execute".Length..^"Step".Length];
                    }

                    return new MethodToCopy { Name = name, StepType = stepType };
                })
                .ToList();

            // Build dispatch rewires
            var stepTypeToMethodName = methodsToCopy
                .Where(method => !string.IsNullOrWhiteSpace(method.StepType))
                .GroupBy(method => method.StepType!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);
            var dispatchRewires = stepTypes
                .Select(st => new DispatchRewire
                {
                    StepType = $"WorkflowStepType.{st}",
                    OldPattern = BuildDispatchOldPattern(stepTypeToMethodName.TryGetValue(st, out var methodName) ? methodName : $"Execute{st}StepAsync"),
                    NewPattern = "await stepDispatcher.ExecuteAsync(step, context, cancellationToken)"
                })
                .ToList();

            // All methods to delete from god class = extracted methods + helpers that move
            var methodsToDelete = plan.MethodsToExtract
                .Concat(helpersThatMove.Select(h => h.Name))
                .ToList();

            // Promote methods
            var promoteMethods = plan.Dependencies
                .Where(d => d.Classification == "needs_promotion")
                .Select(d => new PromoteMethodOperation
                {
                    MethodName = d.SymbolDisplayName,
                    TargetClass = d.PromotionTarget ?? "SharedHelper",
                    Reason = d.Reason
                })
                .ToList();

            recipes.Add(new ExtractionRecipe
            {
                RefactorIntent = effectiveIntent,
                AllowedTransformationFamilies = allowedTransformationFamilies,
                SeamName = plan.SeamName,
                SourceFile = targetFile.NormalizedPath,
                PivotType = plan.PivotType,
                Risk = plan.Risk,
                Confidence = plan.Confidence,
                EstimatedLocReduction = plan.EstimatedLocReduction,
                Pattern = patternInfo,
                ContractBoundaryDecision = contractBoundaryDecision.Decision,
                CreateFile = new CreateFileOperation
                {
                    Path = placement.Implementation.Path,
                    ClassName = plan.ProposedClassName,
                    InterfaceName = interfaceName,
                    Namespace = placement.Implementation.Namespace,
                    PlacementReason = placement.Implementation.Reason,
                    SupportedStepTypes = stepTypes,
                    ConstructorParams = plan.ServiceInjectionsNeeded,
                    MethodsToCopy = methodsToCopy,
                    HelpersThatMove = helpersThatMove,
                    RecordsToMove = plan.RecordsToMove
                },
                InterfaceFile = placement.Interface is null
                    ? null
                    : new InterfaceFileOperation
                    {
                        Name = placement.Interface.Name,
                        Path = placement.Interface.Path,
                        Namespace = placement.Interface.Namespace,
                        PlacementReason = placement.Interface.Reason,
                        ExistingPath = placement.Interface.ExistingPath
                    },
                ModifyGodClass = new ModifyGodClassOperation
                {
                    MethodsToDelete = methodsToDelete,
                    DispatchRewires = dispatchRewires
                },
                RegisterDi = new DiRegistration
                {
                    RegistrationLine = BuildRegistrationLine(effectiveIntent, patternInfo, interfaceName, plan.ProposedClassName)
                },
                PromoteMethods = promoteMethods
            });

            generalizationSurfaceKeys.Add(
                effectiveIntent.Depth >= RefactorDepth.Generalization
                    ? BuildGeneralizationSurfaceKey(plan, targetFile, symbols, symbolsById, contractsBySymbolId)
                    : null);
            namedGeneralizationSurfaceKeys.Add(
                effectiveIntent.Depth >= RefactorDepth.Generalization
                    ? BuildNamedGeneralizationSurfaceKey(plan, targetFile, symbols, contractsBySymbolId)
                    : null);
        }

        if (effectiveIntent.Depth >= RefactorDepth.Generalization)
        {
            AnnotateGeneralizationCandidates(recipes, generalizationSurfaceKeys, namedGeneralizationSurfaceKeys);
        }

        if (effectiveIntent.Depth >= RefactorDepth.ArchitecturalRefactor)
        {
            AnnotateArchitecturalPromotionCandidates(recipes);
        }

        return recipes;
    }

    private static IReadOnlyList<RefactorTransformationFamily> BuildAllowedTransformationFamilies(RefactorIntent intent)
    {
        var families = new List<RefactorTransformationFamily>
        {
            RefactorTransformationFamily.StructuralExtraction
        };

        if (intent.Depth >= RefactorDepth.ContractShaping)
        {
            families.Add(RefactorTransformationFamily.ContractShaping);
        }

        if (intent.Depth >= RefactorDepth.Generalization)
        {
            families.Add(RefactorTransformationFamily.Generalization);
        }

        if (intent.Depth >= RefactorDepth.ArchitecturalRefactor)
        {
            families.Add(RefactorTransformationFamily.ArchitecturalRefactor);
        }

        return families;
    }

    private static string BuildDispatchOldPattern(string methodName)
    {
        return methodName.EndsWith("Async", StringComparison.Ordinal)
            ? $"await {methodName}("
            : $"{methodName}(";
    }

    private static string BuildRegistrationLine(
        RefactorIntent intent,
        ExtractionPatternInfo? patternInfo,
        string interfaceName,
        string className)
    {
        if (intent.Depth >= RefactorDepth.ContractShaping &&
            !string.IsNullOrWhiteSpace(patternInfo?.DiRegistrationPattern))
        {
            var pattern = patternInfo.DiRegistrationPattern!;
            if (!string.IsNullOrWhiteSpace(patternInfo.InterfaceName) &&
                !patternInfo.InterfaceName.Equals(interfaceName, StringComparison.Ordinal))
            {
                pattern = pattern.Replace(patternInfo.InterfaceName, interfaceName, StringComparison.Ordinal);
            }

            if (pattern.Contains("{ClassName}", StringComparison.Ordinal))
            {
                return pattern.Replace("{ClassName}", className, StringComparison.Ordinal)
                    .TrimEnd(';') + ";";
            }
        }

        return $"services.AddScoped<{interfaceName}, {className}>();";
    }

    private static ResolvedContractBoundary ResolveContractBoundaryDecision(
        RefactorIntent intent,
        ExtractionPatternInfo? patternInfo,
        SeamExtractionPlanRecord plan,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyList<FileRecord> files,
        IReadOnlyDictionary<string, ContractRecord> contractsBySymbolId,
        string proposedClassName)
    {
        var conventionalName = $"I{proposedClassName}";

        if (string.IsNullOrWhiteSpace(patternInfo?.InterfaceName))
        {
            return new ResolvedContractBoundary(
                conventionalName,
                intent.Depth >= RefactorDepth.ContractShaping
                    ? new ContractBoundaryDecisionInfo
                    {
                        Outcome = "generate_new",
                        Reason = "no_existing_boundary_pattern",
                        ComparisonMode = "conventional_name"
                    }
                    : null);
        }

        if (intent.Depth >= RefactorDepth.ContractShaping &&
            !patternInfo.InterfaceName.Equals(conventionalName, StringComparison.Ordinal))
        {
            var comparison = CompareInterfaceContract(patternInfo.InterfaceName, plan, symbols, files, contractsBySymbolId);
            if (comparison.ShouldNarrow)
            {
                return new ResolvedContractBoundary(
                    conventionalName,
                    new ContractBoundaryDecisionInfo
                    {
                        Outcome = "generate_narrower",
                        Reason = comparison.Reason,
                        ExistingInterfaceName = patternInfo.InterfaceName,
                        ComparisonMode = comparison.ComparisonMode
                    });
            }

            return new ResolvedContractBoundary(
                patternInfo.InterfaceName,
                new ContractBoundaryDecisionInfo
                {
                    Outcome = "reuse_existing",
                    Reason = comparison.Reason,
                    ExistingInterfaceName = patternInfo.InterfaceName,
                    ComparisonMode = comparison.ComparisonMode
                });
        }

        return new ResolvedContractBoundary(
            patternInfo.InterfaceName,
            intent.Depth >= RefactorDepth.ContractShaping
                ? new ContractBoundaryDecisionInfo
                {
                    Outcome = "reuse_existing",
                    Reason = "pattern_interface_matches_conventional_name",
                    ExistingInterfaceName = patternInfo.InterfaceName,
                    ComparisonMode = "name_match"
                }
                : null);
    }

    private static ContractBoundaryComparison CompareInterfaceContract(
        string interfaceName,
        SeamExtractionPlanRecord plan,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyList<FileRecord> files,
        IReadOnlyDictionary<string, ContractRecord> contractsBySymbolId)
    {
        var existingInterface = symbols.FirstOrDefault(symbol =>
            symbol.Kind == "interface" &&
            symbol.DisplayName.Equals(interfaceName, StringComparison.Ordinal));

        if (existingInterface is null)
        {
            return new ContractBoundaryComparison(true, "existing_interface_not_found", "name_lookup");
        }

        var interfaceFileExists = files.Any(file => file.Id == existingInterface.FileId);
        if (!interfaceFileExists)
        {
            return new ContractBoundaryComparison(true, "existing_interface_file_not_found", "name_lookup");
        }

        var interfaceMembers = symbols
            .Where(symbol => symbol.FileId == existingInterface.FileId &&
                             !symbol.Id.Equals(existingInterface.Id, StringComparison.Ordinal) &&
                             IsInterfaceMemberSymbol(symbol.Kind))
            .ToArray();

        if (interfaceMembers.Length == 0)
        {
            return new ContractBoundaryComparison(true, "existing_interface_members_unresolved", "name_lookup");
        }

        var targetMethodShapes = ResolvePlanMethodSymbols(plan, symbols)
            .Where(symbol => IsInterfaceMemberSymbol(symbol.Kind))
            .Select(symbol => TryBuildMemberShapeKey(symbol, contractsBySymbolId))
            .Where(shape => shape is not null)
            .Cast<string>()
            .ToArray();

        var interfaceMethodShapes = interfaceMembers
            .Select(symbol => TryBuildMemberShapeKey(symbol, contractsBySymbolId))
            .Where(shape => shape is not null)
            .Cast<string>()
            .ToArray();

        if (targetMethodShapes.Length == plan.MethodsToExtract.Count &&
            interfaceMethodShapes.Length == interfaceMembers.Length &&
            interfaceMethodShapes.Length == targetMethodShapes.Length)
        {
            var existingShapeSet = interfaceMethodShapes.ToHashSet();
            if (targetMethodShapes.All(existingShapeSet.Contains))
            {
                return new ContractBoundaryComparison(false, "normalized_member_surface_match", "signature_plus_contract");
            }

            return new ContractBoundaryComparison(true, "normalized_member_surface_mismatch", "signature_plus_contract");
        }

        var existingMemberCount = interfaceMembers
            .Select(symbol => symbol.DisplayName)
            .Distinct(StringComparer.Ordinal)
            .Count();

        return existingMemberCount > plan.MethodsToExtract.Count
            ? new ContractBoundaryComparison(true, "existing_contract_broader_than_extracted_seam", "member_count")
            : new ContractBoundaryComparison(true, "unable_to_confirm_equivalent_member_surface", "member_count");
    }

    private static bool IsInterfaceMemberSymbol(string kind)
    {
        return kind is "method" or "function" or "property";
    }

    private static string? TryBuildMemberShapeKey(
        SymbolRecord symbol,
        IReadOnlyDictionary<string, ContractRecord> contractsBySymbolId)
    {
        if (string.IsNullOrWhiteSpace(symbol.Signature))
        {
            return null;
        }

        var method = ParseMemberDeclaration(symbol.Signature) as MethodDeclarationSyntax;
        if (method is null)
        {
            return null;
        }

        var returnType = NormalizeSyntax(method.ReturnType);
        var parameters = method.ParameterList.Parameters
            .Select(parameter =>
            {
                var flags = string.Concat(
                    parameter.Modifiers.Any(token => token.IsKind(SyntaxKind.ParamsKeyword)) ? "params|" : string.Empty,
                    parameter.Default is not null ? "optional" : string.Empty);
                return $"{NormalizeSyntax(parameter.Type)}:{flags}";
            })
            .ToArray();

        var methodKey = $"{method.Identifier.ValueText}`{method.TypeParameterList?.Parameters.Count ?? 0}:{returnType}({string.Join(",", parameters)})";

        if (!contractsBySymbolId.TryGetValue(symbol.Id, out var contract))
        {
            return methodKey;
        }

        return methodKey + "|" + BuildContractShapeKey(contract);
    }

    private static MemberDeclarationSyntax? ParseMemberDeclaration(string signature)
    {
        var memberText = signature.Trim().TrimEnd(';');
        if (memberText.Contains('(') &&
            !memberText.Contains("=>", StringComparison.Ordinal) &&
            !memberText.Contains('{'))
        {
            memberText += " { throw new global::System.NotImplementedException(); }";
        }

        return SyntaxFactory.ParseMemberDeclaration(memberText);
    }

    private static string NormalizeSyntax(TypeSyntax? typeSyntax)
    {
        if (typeSyntax is null)
        {
            return string.Empty;
        }

        return typeSyntax
            .WithoutTrivia()
            .NormalizeWhitespace(elasticTrivia: false)
            .ToFullString();
    }

    private static string BuildContractShapeKey(ContractRecord contract)
    {
        return string.Join(
            "|",
            "in:" + string.Join(",", contract.InputTypes),
            "out:" + string.Join(",", contract.OutputTypes),
            "constraints:" + string.Join(",", contract.GenericConstraints),
            "async:" + contract.AsyncMode,
            "nullable_in:" + contract.Nullability.AcceptsNullableInput,
            "nullable_out:" + contract.Nullability.ReturnsNullableOutput,
            "optional:" + contract.Nullability.HasOptionalParameters);
    }

    private static string? BuildGeneralizationSurfaceKey(
        SeamExtractionPlanRecord plan,
        FileRecord targetFile,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyDictionary<string, SymbolRecord> symbolsById,
        IReadOnlyDictionary<string, ContractRecord> contractsBySymbolId)
    {
        var methodSymbols = ResolvePlanMethodSymbols(plan, symbols)
            .Where(symbol => symbol.FileId == targetFile.Id &&
                             IsInterfaceMemberSymbol(symbol.Kind))
            .ToArray();

        if (methodSymbols.Length != plan.MethodsToExtract.Count)
        {
            return null;
        }

        var methodSurfaceKeys = methodSymbols
            .Select(symbol => TryBuildAnonymousMemberShapeKey(symbol, contractsBySymbolId))
            .Where(key => key is not null)
            .Cast<string>()
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        if (methodSurfaceKeys.Length != plan.MethodsToExtract.Count)
        {
            return null;
        }

        var dependencySurface = plan.ServiceInjectionsNeeded
            .OrderBy(parameter => parameter, StringComparer.Ordinal)
            .ToArray();
        var stepSurface = plan.StepTypesToRoute
            .SelectMany(TokenizeOperationName)
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();
        var collaboratorSurface = plan.Dependencies
            .Select(dependency => TryBuildAnonymousDependencyShapeKey(dependency, symbolsById, contractsBySymbolId))
            .Where(key => key is not null)
            .Cast<string>()
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        if (collaboratorSurface.Length != plan.Dependencies.Count)
        {
            return null;
        }

        return "methods=" + string.Join(";", methodSurfaceKeys) +
               "|ctor=" + string.Join(";", dependencySurface) +
               "|steps=" + string.Join(";", stepSurface) +
               "|deps=" + string.Join(";", collaboratorSurface);
    }

    private static void AnnotateGeneralizationCandidates(
        List<ExtractionRecipe> recipes,
        IReadOnlyList<string?> generalizationSurfaceKeys,
        IReadOnlyList<string?> namedGeneralizationSurfaceKeys)
    {
        var candidateGroups = generalizationSurfaceKeys
            .Select((key, index) => new { Key = key, Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToArray();

        foreach (var group in candidateGroups)
        {
            var indexes = group.Select(item => item.Index).ToArray();
            var seamNames = indexes
                .Select(index => recipes[index].SeamName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var suggestedSharedAbstractionName = TrySuggestSharedAbstractionName(indexes, namedGeneralizationSurfaceKeys);
            var suggestedSharedImplementationBaseName = TrySuggestSharedImplementationBaseName(suggestedSharedAbstractionName);

            foreach (var index in indexes)
            {
                var peers = seamNames
                    .Where(name => !name.Equals(recipes[index].SeamName, StringComparison.Ordinal))
                    .ToArray();
                recipes[index] = recipes[index] with
                {
                    CreateFile = suggestedSharedAbstractionName is null
                        ? recipes[index].CreateFile
                        : recipes[index].CreateFile with
                        {
                            InterfaceName = suggestedSharedAbstractionName
                        },
                    InterfaceFile = suggestedSharedAbstractionName is null
                        ? recipes[index].InterfaceFile
                        : BuildSharedInterfaceFile(recipes[index].InterfaceFile, suggestedSharedAbstractionName),
                    RegisterDi = suggestedSharedAbstractionName is null
                        ? recipes[index].RegisterDi
                        : RewriteRegistrationInterface(recipes[index], suggestedSharedAbstractionName),
                    GeneralizationCandidate = new GeneralizationCandidateInfo
                    {
                        Outcome = "shared_surface_candidate",
                        Reason = "normalized_collaborator_surface_match",
                        ComparisonMode = "anonymous_signature_plus_contract_plus_dependencies",
                        PeerSeams = peers,
                        CandidateGroupSize = seamNames.Length,
                        SuggestedSharedAbstractionName = suggestedSharedAbstractionName,
                        SuggestedSharedImplementationBaseName = suggestedSharedImplementationBaseName
                    }
                };
            }
        }
    }

    private static void AnnotateArchitecturalPromotionCandidates(List<ExtractionRecipe> recipes)
    {
        var promotionGroups = recipes
            .Select((recipe, index) => new { Recipe = recipe, Index = index })
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Recipe.GeneralizationCandidate?.SuggestedSharedAbstractionName) &&
                !string.IsNullOrWhiteSpace(item.Recipe.GeneralizationCandidate?.SuggestedSharedImplementationBaseName))
            .GroupBy(
                item => (
                    Abstraction: item.Recipe.GeneralizationCandidate!.SuggestedSharedAbstractionName!,
                    Base: item.Recipe.GeneralizationCandidate!.SuggestedSharedImplementationBaseName!),
                item => item,
                EqualityComparer<(string Abstraction, string Base)>.Default)
            .Where(group => group.Count() > 1)
            .ToArray();

        foreach (var group in promotionGroups)
        {
            var indexes = group.Select(item => item.Index).ToArray();
            var seamNames = indexes
                .Select(index => recipes[index].SeamName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var suggestedSubsystemName = TrySuggestSubsystemName(group.Key.Abstraction);
            var suggestedFacadeName = TrySuggestFacadeName(group.Key.Abstraction);
            var suggestedFacadeRegistrationLine = TrySuggestFacadeRegistrationLine(suggestedFacadeName);

            foreach (var index in indexes)
            {
                var peers = seamNames
                    .Where(name => !name.Equals(recipes[index].SeamName, StringComparison.Ordinal))
                    .ToArray();
                recipes[index] = recipes[index] with
                {
                    RegisterDi = suggestedFacadeRegistrationLine is null
                        ? recipes[index].RegisterDi
                        : recipes[index].RegisterDi with
                        {
                            AdditionalRegistrationLines = AppendAdditionalRegistrationLine(
                                recipes[index].RegisterDi.AdditionalRegistrationLines,
                                suggestedFacadeRegistrationLine)
                        },
                    ArchitecturalPromotionCandidate = new ArchitecturalPromotionCandidateInfo
                    {
                        Outcome = "subsystem_candidate",
                        Reason = "strict_shared_contract_and_base_available",
                        PromotionMode = "shared_contract_plus_base_cluster",
                        SuggestedSubsystemName = suggestedSubsystemName,
                        SuggestedFacadeName = suggestedFacadeName,
                        SuggestedFacadeRegistrationLine = suggestedFacadeRegistrationLine,
                        PeerSeams = peers,
                        CandidateGroupSize = seamNames.Length
                    }
                };
            }
        }
    }

    private static string? TrySuggestSharedAbstractionName(
        IReadOnlyList<int> indexes,
        IReadOnlyList<string?> namedGeneralizationSurfaceKeys)
    {
        var namedKeys = indexes
            .Select(index => namedGeneralizationSurfaceKeys[index])
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (namedKeys.Length != 1)
        {
            return null;
        }

        var namedKey = namedKeys[0]!;
        const string prefix = "name=";
        const string separator = "|";
        if (!namedKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var separatorIndex = namedKey.IndexOf(separator, StringComparison.Ordinal);
        var methodName = separatorIndex > prefix.Length
            ? namedKey[prefix.Length..separatorIndex]
            : namedKey[prefix.Length..];
        if (string.IsNullOrWhiteSpace(methodName))
        {
            return null;
        }

        return $"I{methodName}Contract";
    }

    private static string? TrySuggestSharedImplementationBaseName(string? suggestedSharedAbstractionName)
    {
        if (string.IsNullOrWhiteSpace(suggestedSharedAbstractionName))
        {
            return null;
        }

        return suggestedSharedAbstractionName.StartsWith('I') && suggestedSharedAbstractionName.Length > 1
            ? suggestedSharedAbstractionName[1..] + "Base"
            : suggestedSharedAbstractionName + "Base";
    }

    private static string? TrySuggestSubsystemName(string? suggestedSharedAbstractionName)
    {
        var stem = TryExtractArchitecturalStem(suggestedSharedAbstractionName);
        return string.IsNullOrWhiteSpace(stem)
            ? null
            : $"{stem}Subsystem";
    }

    private static string? TrySuggestFacadeName(string? suggestedSharedAbstractionName)
    {
        var stem = TryExtractArchitecturalStem(suggestedSharedAbstractionName);
        return string.IsNullOrWhiteSpace(stem)
            ? null
            : $"{stem}Facade";
    }

    private static string? TrySuggestFacadeRegistrationLine(string? suggestedFacadeName)
    {
        return string.IsNullOrWhiteSpace(suggestedFacadeName)
            ? null
            : $"services.AddScoped<{suggestedFacadeName}>();";
    }

    private static string? TryExtractArchitecturalStem(string? suggestedSharedAbstractionName)
    {
        if (string.IsNullOrWhiteSpace(suggestedSharedAbstractionName))
        {
            return null;
        }

        var stem = suggestedSharedAbstractionName!;
        if (stem.StartsWith('I') && stem.Length > 1 && char.IsUpper(stem[1]))
        {
            stem = stem[1..];
        }

        const string contractSuffix = "Contract";
        if (stem.EndsWith(contractSuffix, StringComparison.Ordinal) && stem.Length > contractSuffix.Length)
        {
            stem = stem[..^contractSuffix.Length];
        }

        return string.IsNullOrWhiteSpace(stem)
            ? null
            : stem;
    }

    private static string? TryBuildAnonymousMemberShapeKey(
        SymbolRecord symbol,
        IReadOnlyDictionary<string, ContractRecord> contractsBySymbolId)
    {
        var namedShapeKey = TryBuildMemberShapeKey(symbol, contractsBySymbolId);
        if (namedShapeKey is null)
        {
            return null;
        }

        var separatorIndex = namedShapeKey.IndexOf(':');
        return separatorIndex >= 0
            ? namedShapeKey[(separatorIndex + 1)..]
            : namedShapeKey;
    }

    private static string? TryBuildAnonymousDependencyShapeKey(
        DependencyClassificationRecord dependency,
        IReadOnlyDictionary<string, SymbolRecord> symbolsById,
        IReadOnlyDictionary<string, ContractRecord> contractsBySymbolId)
    {
        if (!symbolsById.TryGetValue(dependency.SymbolId, out var symbol))
        {
            return null;
        }

        var anonymousShape = IsInterfaceMemberSymbol(symbol.Kind)
            ? TryBuildAnonymousMemberShapeKey(symbol, contractsBySymbolId)
            : NormalizeSymbolKind(symbol.Kind);
        if (anonymousShape is null)
        {
            return null;
        }

        return $"{dependency.Classification}:{anonymousShape}";
    }

    private static string NormalizeSymbolKind(string kind)
    {
        return kind.Trim().ToLowerInvariant();
    }

    private static IEnumerable<string> TokenizeOperationName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (var token in SplitPascalCase(value))
        {
            if (IsBoilerplateOperationToken(token))
            {
                continue;
            }

            yield return token.ToLowerInvariant();
        }
    }

    private static IEnumerable<string> SplitPascalCase(string value)
    {
        var start = 0;
        for (var i = 1; i < value.Length; i++)
        {
            if (char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
            {
                yield return value[start..i];
                start = i;
            }
        }

        yield return value[start..];
    }

    private static bool IsBoilerplateOperationToken(string token)
    {
        return token is "Execute" or "Resource" or "Step" or "Async";
    }

    private static string? BuildNamedGeneralizationSurfaceKey(
        SeamExtractionPlanRecord plan,
        FileRecord targetFile,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyDictionary<string, ContractRecord> contractsBySymbolId)
    {
        if (plan.MethodsToExtract.Count != 1)
        {
            return null;
        }

        var methodSymbol = ResolvePlanMethodSymbols(plan, symbols).FirstOrDefault(symbol =>
            symbol.FileId == targetFile.Id &&
            IsInterfaceMemberSymbol(symbol.Kind));
        if (methodSymbol is null)
        {
            return null;
        }

        var namedKey = TryBuildMemberShapeKey(methodSymbol, contractsBySymbolId);
        return namedKey is null
            ? null
            : $"name={methodSymbol.DisplayName}|{namedKey}";
    }

    private static InterfaceFileOperation? BuildSharedInterfaceFile(
        InterfaceFileOperation? existingInterfaceFile,
        string suggestedSharedAbstractionName)
    {
        if (existingInterfaceFile is null)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(existingInterfaceFile.Path)?.Replace('\\', '/')
            ?? string.Empty;
        var sharedPath = string.IsNullOrWhiteSpace(directory)
            ? $"{suggestedSharedAbstractionName}.cs"
            : $"{directory}/{suggestedSharedAbstractionName}.cs";

        return existingInterfaceFile with
        {
            Name = suggestedSharedAbstractionName,
            Path = sharedPath,
            ExistingPath = null,
            PlacementReason = existingInterfaceFile.PlacementReason +
                              " Level 3 converged this recipe onto a shared generated contract candidate."
        };
    }

    private static DiRegistration RewriteRegistrationInterface(
        ExtractionRecipe recipe,
        string suggestedSharedAbstractionName)
    {
        var rewrittenLine = recipe.RegisterDi.RegistrationLine.Replace(
            recipe.CreateFile.InterfaceName,
            suggestedSharedAbstractionName,
            StringComparison.Ordinal);
        return recipe.RegisterDi with
        {
            RegistrationLine = rewrittenLine
        };
    }

    private static IReadOnlyList<string> AppendAdditionalRegistrationLine(
        IReadOnlyList<string> existingLines,
        string additionalLine)
    {
        if (existingLines.Contains(additionalLine, StringComparer.Ordinal))
        {
            return existingLines;
        }

        return existingLines.Concat([additionalLine]).ToArray();
    }

    private static SymbolRecord[] ResolvePlanMethodSymbols(
        SeamExtractionPlanRecord plan,
        IReadOnlyList<SymbolRecord> symbols)
    {
        if (plan.MethodSymbolIds is { Count: > 0 })
        {
            var ids = plan.MethodSymbolIds.ToHashSet(StringComparer.Ordinal);
            return symbols
                .Where(symbol => symbol.FileId == plan.TargetFileId && ids.Contains(symbol.Id))
                .ToArray();
        }

        return symbols
            .Where(symbol => symbol.FileId == plan.TargetFileId &&
                             plan.MethodsToExtract.Contains(symbol.DisplayName, StringComparer.Ordinal))
            .ToArray();
    }

    private sealed record ResolvedContractBoundary(
        string InterfaceName,
        ContractBoundaryDecisionInfo? Decision);

    private sealed record ContractBoundaryComparison(
        bool ShouldNarrow,
        string Reason,
        string ComparisonMode);
}
