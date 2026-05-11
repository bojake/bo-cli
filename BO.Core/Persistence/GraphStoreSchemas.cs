namespace BO.Core.Persistence;

public static class GraphStoreSchemas
{
    public static GraphSchemaDefinition BoV01 { get; } = new(
        Nodes:
        [
            new GraphNodeSchema("Repo", ["id", "name", "root_path"]),
            new GraphNodeSchema("Module", ["id", "repo_id", "qualified_name"]),
            new GraphNodeSchema("File", ["id", "repo_id", "normalized_path"]),
            new GraphNodeSchema("Symbol", ["id", "repo_id", "file_id", "qualified_name", "kind"]),
            new GraphNodeSchema("Contract", ["id", "symbol_id", "async_mode"]),
            new GraphNodeSchema("BoundaryInteraction", ["id", "file_id", "boundary_type", "target_name"]),
            new GraphNodeSchema("EffectProfile", ["id", "target_id", "target_kind"]),
            new GraphNodeSchema("ComplexityProfile", ["id", "target_id", "target_kind"]),
            new GraphNodeSchema("ResponsibilityProfile", ["id", "target_id", "target_kind"]),
            new GraphNodeSchema("RefactorPressureScore", ["id", "target_id", "target_kind", "score", "recommendation"]),
            new GraphNodeSchema("RefactorDecision", ["id", "target_id", "recommendation", "pivot_type"]),
            new GraphNodeSchema("SeamExtractionPlan", ["id", "target_file_id", "seam_name", "proposed_class_name", "risk"])
        ],
        Edges:
        [
            new GraphEdgeSchema("CONTAINS_MODULE", "Repo", "Module", ["id"]),
            new GraphEdgeSchema("CONTAINS_FILE", "Repo", "File", ["id"]),
            new GraphEdgeSchema("MODULE_CONTAINS_FILE", "Module", "File", ["id"]),
            new GraphEdgeSchema("DEFINES_SYMBOL", "File", "Symbol", ["id"]),
            new GraphEdgeSchema("CONTAINS_SYMBOL", "Module", "Symbol", ["id"]),
            new GraphEdgeSchema("HAS_CONTRACT", "Symbol", "Contract", ["id"]),
            new GraphEdgeSchema("IMPORTS", "File", "File", ["id"]),
            new GraphEdgeSchema("CALLS", "Symbol", "Symbol", ["id"]),
            new GraphEdgeSchema("INSTANTIATES", "Symbol", "Symbol", ["id"]),
            new GraphEdgeSchema("USES_TYPE", "Symbol", "Symbol", ["id"]),
            new GraphEdgeSchema("CROSSES_BOUNDARY", "File", "BoundaryInteraction", ["id"]),
            new GraphEdgeSchema("HAS_EFFECT_PROFILE", "File", "EffectProfile", ["id"]),
            new GraphEdgeSchema("HAS_COMPLEXITY_PROFILE", "File", "ComplexityProfile", ["id"]),
            new GraphEdgeSchema("HAS_RESPONSIBILITY_PROFILE", "File", "ResponsibilityProfile", ["id"]),
            new GraphEdgeSchema("HAS_RPS", "File", "RefactorPressureScore", ["id"]),
            new GraphEdgeSchema("HAS_REFACTOR_DECISION", "File", "RefactorDecision", ["id"]),
            new GraphEdgeSchema("HAS_EXTRACTION_PLAN", "File", "SeamExtractionPlan", ["id"])
        ]);
}
