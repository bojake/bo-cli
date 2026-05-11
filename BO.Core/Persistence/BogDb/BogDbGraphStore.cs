using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BO.Core.Persistence.BogDb;

/// <summary>
/// Implements BO graph persistence using the public BogDB NuGet package.
/// </summary>
public sealed class BogDbGraphStore : IBoGraphStore, IDisposable
{
    private readonly BogDatabase _database;
    private readonly BogConnection _connection;
    private bool _disposed;

    public BogDbGraphStore(BogDbStorageOptions options)
    {
        Directory.CreateDirectory(options.DatabasePath);
        _database = BogDatabase.Open(options.DatabasePath);
        _connection = new BogConnection(_database);
    }

    public ValueTask EnsureSchemaAsync(
        GraphSchemaDefinition schema,
        CancellationToken cancellationToken = default)
    {
        var stringProp = new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.STRING
        };

        foreach (var node in schema.Nodes)
        {
            if (_connection.HasTable(node.Label))
                continue;

            _connection.BeginWriteTransaction();
            _connection.EnsureNodeTable(node.Label, stringProp);
            _connection.CommitSchemaOnly();
        }

        foreach (var edge in schema.Edges)
        {
            if (_connection.HasTable(edge.Label))
                continue;

            _connection.BeginWriteTransaction();
            _connection.EnsureRelTable(edge.Label, edge.FromLabel, edge.ToLabel, stringProp);
            _connection.CommitSchemaOnly();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ApplyWriteBatchAsync(
        GraphWriteBatch batch,
        CancellationToken cancellationToken = default)
    {
        _connection.ExecuteWriteTransaction(() =>
        {
            foreach (var node in batch.Nodes)
                _connection.UpsertNodeById(node.Label, node.Id, ToStringProperties(node.Properties));

            foreach (var edge in batch.Edges)
                _connection.UpsertRelationshipById(edge.Label, edge.FromId, edge.ToId, ToStringProperties(edge.Properties));
        });

        return ValueTask.CompletedTask;
    }

    public ValueTask<GraphNodeRecord?> GetNodeByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var props = _connection.GetNodeById(id, out var tableName);
        if (props is null || tableName is null)
            return ValueTask.FromResult<GraphNodeRecord?>(null);

        var record = new GraphNodeRecord(
            id,
            tableName,
            props.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value));

        return ValueTask.FromResult<GraphNodeRecord?>(record);
    }

    public ValueTask<IReadOnlyList<GraphEdgeRecord>> GetOutgoingEdgesAsync(
        string fromId,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        var rawEdges = _connection.GetOutgoingEdges(fromId, label);
        var results = rawEdges
            .Select(edge => ToGraphEdgeRecord(edge.TableName, edge.FromId, edge.ToId, edge.Properties))
            .OrderBy(edge => edge.Id, StringComparer.Ordinal)
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<GraphEdgeRecord>>(results);
    }

    public ValueTask<IReadOnlyList<GraphEdgeRecord>> GetIncomingEdgesAsync(
        string toId,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        var rawEdges = _connection.GetIncomingEdges(toId, label);
        var results = rawEdges
            .Select(edge => ToGraphEdgeRecord(edge.TableName, edge.FromId, edge.ToId, edge.Properties))
            .OrderBy(edge => edge.Id, StringComparer.Ordinal)
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<GraphEdgeRecord>>(results);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _connection.Dispose();
        _database.Dispose();
    }

    private static GraphEdgeRecord ToGraphEdgeRecord(
        string tableName,
        string fromId,
        string toId,
        Dictionary<string, object> props)
    {
        var edgeId = props.TryGetValue("id", out var storedId)
            ? storedId?.ToString() ?? $"edge:{fromId}:{tableName}:{toId}"
            : $"edge:{fromId}:{tableName}:{toId}";

        return new GraphEdgeRecord(
            edgeId,
            tableName,
            fromId,
            toId,
            props.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value));
    }

    private static Dictionary<string, object> ToStringProperties(
        IReadOnlyDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object>(source.Count, StringComparer.Ordinal);
        foreach (var (key, value) in source)
            result[key] = value?.ToString() ?? "null";

        return result;
    }
}

