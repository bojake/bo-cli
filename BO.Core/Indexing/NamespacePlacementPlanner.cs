using System.Text.RegularExpressions;

namespace BO.Core.Indexing;

/// <summary>
/// Decides where extracted classes and contracts should live before scaffolding.
/// This keeps namespace/layer policy in planning rather than ad hoc file rewrites.
/// </summary>
public sealed class NamespacePlacementPlanner
{
    private static readonly Regex TypeTokenPattern = new(@"\b[A-Z][A-Za-z0-9_]*\b", RegexOptions.Compiled);
    private static readonly HashSet<string> IgnoredSignatureTokens = new(StringComparer.Ordinal)
    {
        "Async",
        "CancellationToken",
        "Func",
        "Guid",
        "Task",
        "ValueTask"
    };

    private static readonly HashSet<string> IgnoredTypeTokens = new(StringComparer.Ordinal)
    {
        "Action",
        "Array",
        "CancellationToken",
        "DateOnly",
        "DateTime",
        "DateTimeOffset",
        "Decimal",
        "Dictionary",
        "Enumerable",
        "Exception",
        "Func",
        "Guid",
        "HashSet",
        "IAsyncEnumerable",
        "ICollection",
        "IDictionary",
        "IEnumerable",
        "IList",
        "IQueryable",
        "IReadOnlyCollection",
        "IReadOnlyDictionary",
        "IReadOnlyList",
        "JsonDocument",
        "JsonElement",
        "JsonSerializer",
        "List",
        "Nullable",
        "Object",
        "Result",
        "String",
        "StringBuilder",
        "Task",
        "TimeOnly",
        "TimeSpan",
        "Tuple",
        "ValueTask"
    };

    private readonly ArchitecturePlacementRules _rules;

    public NamespacePlacementPlanner(ArchitecturePlacementRules? rules = null)
    {
        _rules = rules ?? ArchitecturePlacementRules.Default;
    }

    public PlacementResolution Resolve(
        SeamExtractionPlanRecord plan,
        FileRecord targetFile,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyList<ContractRecord> contracts,
        string interfaceName,
        string? boundaryInterfaceName = null)
    {
        var implementation = ResolveImplementationPlacement(targetFile, plan.ProposedClassName, _rules.InterfacePlacement);
        var interfacePlacement = string.IsNullOrWhiteSpace(interfaceName)
            ? null
            : ResolveInterfacePlacement(plan, targetFile, files, symbols, contracts, interfaceName, boundaryInterfaceName, implementation);

        return new PlacementResolution(implementation, interfacePlacement);
    }

    public sealed record PlacementResolution(
        FilePlacement Implementation,
        InterfacePlacement? Interface);

    public sealed record FilePlacement(
        string Path,
        string Namespace,
        string Reason);

    public sealed record InterfacePlacement(
        string Name,
        string Path,
        string Namespace,
        string Reason,
        string? ExistingPath);

    private static FilePlacement ResolveImplementationPlacement(
        FileRecord targetFile,
        string className,
        InterfacePlacementRules rules)
    {
        var sourceDir = Path.GetDirectoryName(targetFile.NormalizedPath)?.Replace('\\', '/')
            ?? string.Empty;
        var newPath = CombineNormalized(sourceDir, $"{className}.cs");
        var ns = InferNamespaceFromNormalizedPath(newPath, rules);
        var reason = "Kept extracted implementation in the source layer to preserve existing dependency direction.";

        return new FilePlacement(newPath, ns, reason);
    }

    private InterfacePlacement ResolveInterfacePlacement(
        SeamExtractionPlanRecord plan,
        FileRecord targetFile,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyList<ContractRecord> contracts,
        string interfaceName,
        string? boundaryInterfaceName,
        FilePlacement implementation)
    {
        var rootNamespace = GetRootNamespace(implementation.Namespace);
        var filesById = files.ToDictionary(file => file.Id, StringComparer.Ordinal);

        var existingInterface = symbols
            .Where(symbol => symbol.Kind == "interface" &&
                             symbol.DisplayName.Equals(interfaceName, StringComparison.Ordinal))
            .Select(symbol =>
            {
                filesById.TryGetValue(symbol.FileId, out var file);
                return new
                {
                    Symbol = symbol,
                    File = file
                };
            })
            .Where(match => match.File is not null)
            .OrderByDescending(match => IsPreferredExistingInterface(match.File!, rootNamespace, _rules.InterfacePlacement))
            .FirstOrDefault();

        if (existingInterface is not null)
        {
            return new InterfacePlacement(
                interfaceName,
                existingInterface.File!.NormalizedPath,
                ExtractNamespace(existingInterface.Symbol),
                "Reused the existing interface so the refactor stays aligned with the repo's current abstraction boundary.",
                existingInterface.File.NormalizedPath);
        }

        if (!string.IsNullOrWhiteSpace(boundaryInterfaceName) &&
            !boundaryInterfaceName.Equals(interfaceName, StringComparison.Ordinal))
        {
            var boundaryInterface = symbols
                .Where(symbol => symbol.Kind == "interface" &&
                                 symbol.DisplayName.Equals(boundaryInterfaceName, StringComparison.Ordinal))
                .Select(symbol =>
                {
                    filesById.TryGetValue(symbol.FileId, out var file);
                    return new
                    {
                        Symbol = symbol,
                        File = file
                    };
                })
                .Where(match => match.File is not null)
                .OrderByDescending(match => IsPreferredExistingInterface(match.File!, rootNamespace, _rules.InterfacePlacement))
                .FirstOrDefault();

            if (boundaryInterface is not null)
            {
                var boundaryDir = Path.GetDirectoryName(boundaryInterface.File!.NormalizedPath)?.Replace('\\', '/')
                    ?? string.Empty;
                var path = CombineNormalized(boundaryDir, $"{interfaceName}.cs");
                return new InterfacePlacement(
                    interfaceName,
                    path,
                    ExtractNamespace(boundaryInterface.Symbol),
                    "Placed the generated interface beside an existing abstraction boundary, but narrowed the contract to fit the extracted collaborator.",
                    null);
            }
        }

        var abstractionsDir = FindAbstractionsDirectory(files, rootNamespace, _rules.InterfacePlacement);
        if (abstractionsDir is not null &&
            CanPlaceInterfaceInAbstractionLayer(plan, targetFile, files, symbols, contracts, rootNamespace, _rules.InterfacePlacement))
        {
            var path = CombineNormalized(abstractionsDir, $"{interfaceName}.cs");
            return new InterfacePlacement(
                interfaceName,
                path,
                InferNamespaceFromNormalizedPath(path, _rules.InterfacePlacement),
                "Placed the generated interface in the configured abstraction layer because its contract depends only on allowed contract-layer types.",
                null);
        }

        var localPath = CombineNormalized(
            Path.GetDirectoryName(implementation.Path)?.Replace('\\', '/') ?? string.Empty,
            $"{interfaceName}.cs");
        return new InterfacePlacement(
            interfaceName,
            localPath,
            implementation.Namespace,
            "Kept the generated interface beside the implementation because promoting it would introduce an upward dependency on infrastructure-only types.",
            null);
    }

    private static bool IsPreferredExistingInterface(
        FileRecord file,
        string rootNamespace,
        InterfacePlacementRules rules)
    {
        return rules.PreferredExistingInterfacePathContains.Any(pattern =>
            file.NormalizedPath.Contains(pattern.Replace("{rootNamespace}", rootNamespace), StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindAbstractionsDirectory(
        IReadOnlyList<FileRecord> files,
        string rootNamespace,
        InterfacePlacementRules rules)
    {
        return files
            .Select(file => Path.GetDirectoryName(file.NormalizedPath)?.Replace('\\', '/'))
            .Where(dir => !string.IsNullOrWhiteSpace(dir))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(dir =>
                rules.AbstractionLayerNames.Any(layer => dir!.Contains(layer, StringComparison.OrdinalIgnoreCase)) &&
                rules.AbstractionDirectoryNames.Any(name => dir!.EndsWith(name, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(dir => dir!.Contains(rootNamespace, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(dir => rules.PreferredAbstractionDirectoryNames.Any(name => dir!.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault();
    }

    private static bool CanPlaceInterfaceInAbstractionLayer(
        SeamExtractionPlanRecord plan,
        FileRecord targetFile,
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyList<ContractRecord> contracts,
        string rootNamespace,
        InterfacePlacementRules rules)
    {
        var methodNames = plan.MethodsToExtract.ToHashSet(StringComparer.Ordinal);
        var targetMethods = symbols
            .Where(symbol => symbol.FileId == targetFile.Id &&
                             methodNames.Contains(symbol.DisplayName) &&
                             (symbol.Kind == "method" || symbol.Kind == "function"))
            .ToArray();
        var targetMethodIds = targetMethods
            .Select(symbol => symbol.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (targetMethodIds.Count == 0 || targetMethodIds.Count != methodNames.Count)
        {
            return false;
        }

        var typeNamespaces = BuildTypeNamespaceLookup(files, symbols, rules);
        var extractedContracts = contracts
            .Where(contract => targetMethodIds.Contains(contract.SymbolId))
            .ToArray();

        if (extractedContracts.Length == 0 || extractedContracts.Length != targetMethodIds.Count)
        {
            return false;
        }

        foreach (var token in ExtractContractTypeTokens(extractedContracts))
        {
            if (!typeNamespaces.TryGetValue(token, out var namespaces))
            {
                return false;
            }

            var hasDisallowedNamespace = namespaces.Any(ns =>
                IsDisallowedAbstractionContractNamespace(ns, rootNamespace, rules));

            if (hasDisallowedNamespace)
            {
                return false;
            }
        }

        foreach (var token in ExtractSignatureTypeTokens(targetMethods))
        {
            if (!typeNamespaces.TryGetValue(token, out var namespaces))
            {
                continue;
            }

            var hasDisallowedNamespace = namespaces.Any(ns =>
                IsDisallowedAbstractionContractNamespace(ns, rootNamespace, rules));

            if (hasDisallowedNamespace)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, HashSet<string>> BuildTypeNamespaceLookup(
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        InterfacePlacementRules rules)
    {
        var lookup = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var typeName = Path.GetFileNameWithoutExtension(file.NormalizedPath);
            if (string.IsNullOrWhiteSpace(typeName))
            {
                continue;
            }

            AddLookupValue(lookup, typeName, InferNamespaceFromNormalizedPath(file.NormalizedPath, rules));
        }

        foreach (var symbol in symbols.Where(symbol => symbol.Kind is "class" or "interface" or "enum" or "type_alias"))
        {
            AddLookupValue(lookup, symbol.DisplayName, ExtractNamespace(symbol));
        }

        return lookup;
    }

    private static void AddLookupValue(
        Dictionary<string, HashSet<string>> lookup,
        string key,
        string value)
    {
        if (!lookup.TryGetValue(key, out var values))
        {
            values = new HashSet<string>(StringComparer.Ordinal);
            lookup[key] = values;
        }

        values.Add(value);
    }

    private static IEnumerable<string> ExtractContractTypeTokens(IReadOnlyList<ContractRecord> contracts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var contract in contracts)
        {
            foreach (var section in contract.InputTypes
                .Concat(contract.OutputTypes)
                .Concat(contract.GenericConstraints))
            {
                foreach (Match match in TypeTokenPattern.Matches(section))
                {
                    var token = match.Value;
                    if (IgnoredTypeTokens.Contains(token))
                    {
                        continue;
                    }

                    if (seen.Add(token))
                    {
                        yield return token;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> ExtractSignatureTypeTokens(IReadOnlyList<SymbolRecord> symbols)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var symbol in symbols)
        {
            foreach (Match match in TypeTokenPattern.Matches(symbol.Signature))
            {
                var token = match.Value;
                if (token.Equals(symbol.DisplayName, StringComparison.Ordinal) ||
                    IgnoredTypeTokens.Contains(token) ||
                    IgnoredSignatureTokens.Contains(token))
                {
                    continue;
                }

                if (seen.Add(token))
                {
                    yield return token;
                }
            }
        }
    }

    private static string ExtractNamespace(SymbolRecord symbol)
    {
        var suffix = "." + symbol.DisplayName;
        return symbol.QualifiedName.EndsWith(suffix, StringComparison.Ordinal)
            ? symbol.QualifiedName[..^suffix.Length]
            : symbol.QualifiedName;
    }

    private static string GetRootNamespace(string ns)
    {
        var index = ns.IndexOf('.');
        return index < 0 ? ns : ns[..index];
    }

    private static string InferNamespaceFromNormalizedPath(string normalizedPath, InterfacePlacementRules rules)
    {
        var dir = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/') ?? string.Empty;
        var parts = dir.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !rules.SourceRootDirectoryNames.Contains(part, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return parts.Length == 0 ? rules.FallbackNamespace : string.Join(".", parts);
    }

    private static string CombineNormalized(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return right.Replace('\\', '/');
        }

        return $"{left.TrimEnd('/', '\\')}/{right.TrimStart('/', '\\')}".Replace('\\', '/');
    }

    private static bool IsDisallowedAbstractionContractNamespace(
        string ns,
        string rootNamespace,
        InterfacePlacementRules rules)
    {
        var layer = DetectLayer(ns, rules);
        if (layer is not null &&
            rules.DisallowedContractLayers.Contains(layer, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return rootNamespace.Length > 0 &&
               !ns.StartsWith(rootNamespace, StringComparison.OrdinalIgnoreCase) &&
               (layer is null || !rules.AllowedContractLayers.Contains(layer, StringComparer.OrdinalIgnoreCase));
    }

    private static string? DetectLayer(string ns, InterfacePlacementRules rules)
    {
        foreach (var layer in rules.Layers)
        {
            if (layer.NamespaceMarkers.Any(marker => ns.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
                layer.NamespacePrefixes.Any(prefix => ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return layer.Name;
            }
        }

        return null;
    }
}
