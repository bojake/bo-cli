namespace BO.Core.Indexing;

/// <summary>
/// Derives the "context burden" for each file — how many files must be
/// understood to safely edit the target.
///
/// Algorithm:
/// 1. Build bidirectional adjacency (imports + imported-by) from DependencyRecords
/// 2. BFS 2 hops from each file to find the "safe edit neighborhood"
/// 3. Sum LOC from ComplexityProfiles for token cost estimate (~4 chars per token)
/// </summary>
public sealed class ContextBurdenDeriver
{
    private const int MaxHops = 2;
    private const int CharsPerToken = 4;
    private const int AvgCharsPerLine = 40;

    public IReadOnlyList<ContextBurdenRecord> Derive(
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<FileDependencyRecord> dependencies,
        IReadOnlyList<ComplexityProfileRecord> complexityProfiles)
    {
        // Build bidirectional adjacency lists
        var outgoing = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var incoming = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var dep in dependencies)
        {
            if (!outgoing.TryGetValue(dep.FromFileId, out var outSet))
            {
                outSet = new HashSet<string>(StringComparer.Ordinal);
                outgoing[dep.FromFileId] = outSet;
            }
            outSet.Add(dep.ToFileId);

            if (!incoming.TryGetValue(dep.ToFileId, out var inSet))
            {
                inSet = new HashSet<string>(StringComparer.Ordinal);
                incoming[dep.ToFileId] = inSet;
            }
            inSet.Add(dep.FromFileId);
        }

        // LOC lookup from file-level complexity profiles
        var locByFileId = complexityProfiles
            .Where(p => p.TargetKind == "file")
            .ToDictionary(p => p.TargetId, p => p.Loc, StringComparer.Ordinal);

        var results = new List<ContextBurdenRecord>();

        foreach (var file in files)
        {
            if (file.IsGenerated || file.IsTest)
                continue;

            var neighborhood = BfsNeighborhood(file.Id, outgoing, incoming, MaxHops);

            // Estimate token cost from LOC of neighborhood files
            var totalLoc = 0;
            foreach (var neighborId in neighborhood)
            {
                if (locByFileId.TryGetValue(neighborId, out var loc))
                    totalLoc += loc;
            }

            var tokenEstimate = (int)((long)totalLoc * AvgCharsPerLine / CharsPerToken);

            results.Add(new ContextBurdenRecord(
                $"ctx:{file.Id}",
                file.Id,
                "file",
                neighborhood.Count,
                tokenEstimate,
                [.. neighborhood],
                0.8));
        }

        return results;
    }

    private static HashSet<string> BfsNeighborhood(
        string startId,
        Dictionary<string, HashSet<string>> outgoing,
        Dictionary<string, HashSet<string>> incoming,
        int maxHops)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { startId };
        var frontier = new HashSet<string>(StringComparer.Ordinal) { startId };

        for (var hop = 0; hop < maxHops && frontier.Count > 0; hop++)
        {
            var nextFrontier = new HashSet<string>(StringComparer.Ordinal);

            foreach (var nodeId in frontier)
            {
                // Follow outgoing edges (files this file imports)
                if (outgoing.TryGetValue(nodeId, out var outs))
                {
                    foreach (var neighbor in outs)
                    {
                        if (visited.Add(neighbor))
                            nextFrontier.Add(neighbor);
                    }
                }

                // Follow incoming edges (files that import this file)
                if (incoming.TryGetValue(nodeId, out var ins))
                {
                    foreach (var neighbor in ins)
                    {
                        if (visited.Add(neighbor))
                            nextFrontier.Add(neighbor);
                    }
                }
            }

            frontier = nextFrontier;
        }

        // Remove the target file itself — we want neighborhood count, not including self
        visited.Remove(startId);
        return visited;
    }
}
