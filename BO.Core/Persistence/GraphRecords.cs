namespace BO.Core.Persistence;

public sealed record GraphNodeRecord(
    string Id,
    string Label,
    IReadOnlyDictionary<string, object?> Properties);

public sealed record GraphEdgeRecord(
    string Id,
    string Label,
    string FromId,
    string ToId,
    IReadOnlyDictionary<string, object?> Properties);

public sealed record GraphNodeSchema(
    string Label,
    IReadOnlyList<string> RequiredProperties);

public sealed record GraphEdgeSchema(
    string Label,
    string FromLabel,
    string ToLabel,
    IReadOnlyList<string> RequiredProperties);

public sealed record GraphSchemaDefinition(
    IReadOnlyList<GraphNodeSchema> Nodes,
    IReadOnlyList<GraphEdgeSchema> Edges);

public sealed record GraphWriteBatch(
    IReadOnlyList<GraphNodeRecord> Nodes,
    IReadOnlyList<GraphEdgeRecord> Edges);
