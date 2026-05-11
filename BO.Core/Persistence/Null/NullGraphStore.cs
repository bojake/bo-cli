namespace BO.Core.Persistence.Null;

public sealed class NullGraphStore : IBoGraphStore
{
    public ValueTask EnsureSchemaAsync(GraphSchemaDefinition schema, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask ApplyWriteBatchAsync(GraphWriteBatch batch, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask<GraphNodeRecord?> GetNodeByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<GraphNodeRecord?>(null);
    }

    public ValueTask<IReadOnlyList<GraphEdgeRecord>> GetOutgoingEdgesAsync(
        string fromId,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IReadOnlyList<GraphEdgeRecord>>([]);
    }

    public ValueTask<IReadOnlyList<GraphEdgeRecord>> GetIncomingEdgesAsync(
        string toId,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IReadOnlyList<GraphEdgeRecord>>([]);
    }
}
