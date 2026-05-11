namespace BO.Core.Persistence.InMemory;

public sealed class InMemoryGraphStore : IBoGraphStore
{
    private readonly Dictionary<string, GraphNodeRecord> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GraphEdgeRecord> _edges = new(StringComparer.Ordinal);
    private GraphSchemaDefinition? _schema;

    public ValueTask EnsureSchemaAsync(GraphSchemaDefinition schema, CancellationToken cancellationToken = default)
    {
        _schema = schema;
        return ValueTask.CompletedTask;
    }

    public ValueTask ApplyWriteBatchAsync(GraphWriteBatch batch, CancellationToken cancellationToken = default)
    {
        foreach (var node in batch.Nodes)
        {
            _nodes[node.Id] = node;
        }

        foreach (var edge in batch.Edges)
        {
            _edges[edge.Id] = edge;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<GraphNodeRecord?> GetNodeByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _nodes.TryGetValue(id, out var node);
        return ValueTask.FromResult(node);
    }

    public ValueTask<IReadOnlyList<GraphEdgeRecord>> GetOutgoingEdgesAsync(
        string fromId,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GraphEdgeRecord> edges = _edges.Values
            .Where(edge => edge.FromId == fromId && (label is null || edge.Label == label))
            .OrderBy(edge => edge.Id, StringComparer.Ordinal)
            .ToArray();

        return ValueTask.FromResult(edges);
    }

    public ValueTask<IReadOnlyList<GraphEdgeRecord>> GetIncomingEdgesAsync(
        string toId,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GraphEdgeRecord> edges = _edges.Values
            .Where(edge => edge.ToId == toId && (label is null || edge.Label == label))
            .OrderBy(edge => edge.Id, StringComparer.Ordinal)
            .ToArray();

        return ValueTask.FromResult(edges);
    }

    public int NodeCount => _nodes.Count;

    public int EdgeCount => _edges.Count;

    public GraphSchemaDefinition? Schema => _schema;
}
