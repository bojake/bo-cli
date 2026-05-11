namespace BO.Core.Persistence;

public interface IBoGraphStore
{
    ValueTask EnsureSchemaAsync(GraphSchemaDefinition schema, CancellationToken cancellationToken = default);

    ValueTask ApplyWriteBatchAsync(GraphWriteBatch batch, CancellationToken cancellationToken = default);

    ValueTask<GraphNodeRecord?> GetNodeByIdAsync(string id, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<GraphEdgeRecord>> GetOutgoingEdgesAsync(
        string fromId,
        string? label = null,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<GraphEdgeRecord>> GetIncomingEdgesAsync(
        string toId,
        string? label = null,
        CancellationToken cancellationToken = default);
}
