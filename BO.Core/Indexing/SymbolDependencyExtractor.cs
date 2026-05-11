using System.Text.RegularExpressions;

namespace BO.Core.Indexing;

public sealed class SymbolDependencyExtractor
{
    private static readonly Regex TypeIdentifierPattern = new(
        @"\b[A-Z][A-Za-z0-9_]*\b",
        RegexOptions.Compiled);

    private static readonly HashSet<string> TypeLikeKinds = new(StringComparer.Ordinal)
    {
        "interface",
        "type_alias",
        "class"
    };

    private static readonly Regex ImportNamedPattern = new(
        @"import(?:\s+type)?\s*\{(?<items>[^}]+)\}\s*from\s*[""'](?<path>[^""']+)[""']",
        RegexOptions.Compiled);

    private static readonly Regex CallPattern = new(
        @"(?<expr>[A-Za-z_$][A-Za-z0-9_$]*(?:\.[A-Za-z_$][A-Za-z0-9_$]*)?)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex NewPattern = new(
        @"(?:const|let|var)\s+(?<alias>[A-Za-z_$][A-Za-z0-9_$]*)\s*=\s*new\s+(?<type>[A-Za-z_$][A-Za-z0-9_$]*)\s*\(",
        RegexOptions.Compiled);

    public IReadOnlyList<SymbolDependencyRecord> Extract(
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols,
        IReadOnlyList<ContractRecord> contracts,
        IReadOnlyList<FileDependencyRecord> fileDependencies)
    {
        var symbolsByFile = symbols
            .GroupBy(symbol => symbol.FileId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(symbol => symbol.DeclarationLine)
                    .ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var exportedSymbolsByFile = symbols
            .Where(symbol => symbol.IsExported)
            .GroupBy(symbol => symbol.FileId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.Ordinal);
        var symbolsByQualifiedName = symbols
            .GroupBy(s => s.QualifiedName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var contractsBySymbolId = contracts.ToDictionary(contract => contract.SymbolId, StringComparer.Ordinal);

        var dependencyTargets = fileDependencies
            .GroupBy(dependency => CreateImportBindingKey(dependency.FromFileId, dependency.ImportText), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.ToFileId).Distinct(StringComparer.Ordinal).First(),
                StringComparer.Ordinal);

        var edges = new List<SymbolDependencyRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (!symbolsByFile.TryGetValue(file.Id, out var fileSymbols) || fileSymbols.Length == 0 || file.IsGenerated)
            {
                continue;
            }

            string[] lines;
            string sourceText;
            try
            {
                lines = File.ReadAllLines(file.Path);
                sourceText = string.Join('\n', lines);
            }
            catch
            {
                continue;
            }

            var importBindings = ParseImportBindings(
                file.Id,
                sourceText,
                dependencyTargets,
                exportedSymbolsByFile);

            var localSymbolsByName = fileSymbols
                .Where(symbol => !symbol.QualifiedName.Contains('.', StringComparison.Ordinal))
                .GroupBy(symbol => symbol.DisplayName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var availableTypeSymbols = BuildAvailableTypeSymbols(fileSymbols, localSymbolsByName, importBindings);

            for (var index = 0; index < fileSymbols.Length; index++)
            {
                var symbol = fileSymbols[index];
                if (ShouldAnalyzeRuntimeDependencies(symbol))
                {
                    var nextLine = index + 1 < fileSymbols.Length
                        ? fileSymbols[index + 1].DeclarationLine
                        : lines.Length + 1;

                    var regionText = ExtractRegion(lines, symbol.DeclarationLine, nextLine);
                    ExtractRuntimeEdgesForRegion(
                        symbol,
                        regionText,
                        localSymbolsByName,
                        importBindings,
                        symbolsByQualifiedName,
                        seen,
                        edges);
                }

                if (contractsBySymbolId.TryGetValue(symbol.Id, out var contract))
                {
                    ExtractTypeUsageEdges(symbol, contract, availableTypeSymbols, seen, edges);
                }
            }
        }

        return edges;
    }

    private static Dictionary<string, SymbolRecord> ParseImportBindings(
        string fromFileId,
        string sourceText,
        IReadOnlyDictionary<string, string> dependencyTargets,
        IReadOnlyDictionary<string, SymbolRecord[]> exportedSymbolsByFile)
    {
        var bindings = new Dictionary<string, SymbolRecord>(StringComparer.Ordinal);

        foreach (Match match in ImportNamedPattern.Matches(sourceText))
        {
            var importText = match.Groups["path"].Value;
            if (!dependencyTargets.TryGetValue(CreateImportBindingKey(fromFileId, importText), out var targetFileId) ||
                !exportedSymbolsByFile.TryGetValue(targetFileId, out var exportedSymbols))
            {
                continue;
            }

            foreach (var rawItem in match.Groups["items"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = rawItem.Split(" as ", StringSplitOptions.TrimEntries);
                var sourceName = parts[0].Trim();
                var localName = parts.Length > 1 ? parts[1].Trim() : sourceName;
                var targetSymbol = exportedSymbols.SingleOrDefault(symbol => string.Equals(symbol.DisplayName, sourceName, StringComparison.Ordinal));
                if (targetSymbol is not null)
                {
                    bindings[localName] = targetSymbol;
                }
            }
        }

        return bindings;
    }

    private static void ExtractRuntimeEdgesForRegion(
        SymbolRecord sourceSymbol,
        string regionText,
        IReadOnlyDictionary<string, SymbolRecord[]> localSymbolsByName,
        IReadOnlyDictionary<string, SymbolRecord> importBindings,
        IReadOnlyDictionary<string, SymbolRecord> symbolsByQualifiedName,
        HashSet<string> seen,
        List<SymbolDependencyRecord> edges)
    {
        var instanceBindings = new Dictionary<string, SymbolRecord>(StringComparer.Ordinal);

        foreach (Match match in NewPattern.Matches(regionText))
        {
            var typeName = match.Groups["type"].Value;
            var alias = match.Groups["alias"].Value;
            var targetSymbol = ResolveTopLevelSymbol(typeName, localSymbolsByName, importBindings);
            if (targetSymbol is null)
            {
                continue;
            }

            instanceBindings[alias] = targetSymbol;
            AddEdge(sourceSymbol, targetSymbol, "instantiates", $"new {typeName}", 0.8, seen, edges);
        }

        foreach (Match match in CallPattern.Matches(regionText))
        {
            var expression = match.Groups["expr"].Value;
            if (expression.Length == 0)
            {
                continue;
            }

            var callIndex = match.Index;
            if (IsConstructorMatch(regionText, callIndex))
            {
                continue;
            }

            if (TryResolveCallTarget(expression, localSymbolsByName, importBindings, instanceBindings, symbolsByQualifiedName, out var targetSymbol))
            {
                AddEdge(sourceSymbol, targetSymbol, "calls", expression, 0.78, seen, edges);
            }
        }
    }

    private static void ExtractTypeUsageEdges(
        SymbolRecord sourceSymbol,
        ContractRecord contract,
        IReadOnlyDictionary<string, SymbolRecord> availableTypeSymbols,
        HashSet<string> seen,
        List<SymbolDependencyRecord> edges)
    {
        var typeExpressions = contract.InputTypes
            .Concat(contract.OutputTypes)
            .Concat(contract.GenericConstraints)
            .Distinct(StringComparer.Ordinal);

        foreach (var typeExpression in typeExpressions)
        {
            foreach (var typeName in ExtractTypeIdentifiers(typeExpression))
            {
                if (!availableTypeSymbols.TryGetValue(typeName, out var targetSymbol))
                {
                    continue;
                }

                if (IsConstructorSelfType(sourceSymbol, targetSymbol))
                {
                    continue;
                }

                AddEdge(sourceSymbol, targetSymbol, "uses_type", typeName, 0.8, seen, edges);
            }
        }
    }

    private static bool TryResolveCallTarget(
        string expression,
        IReadOnlyDictionary<string, SymbolRecord[]> localSymbolsByName,
        IReadOnlyDictionary<string, SymbolRecord> importBindings,
        IReadOnlyDictionary<string, SymbolRecord> instanceBindings,
        IReadOnlyDictionary<string, SymbolRecord> symbolsByQualifiedName,
        out SymbolRecord targetSymbol)
    {
        targetSymbol = null!;

        if (!expression.Contains('.', StringComparison.Ordinal))
        {
            var resolved = ResolveTopLevelSymbol(expression, localSymbolsByName, importBindings);
            if (resolved is null)
            {
                return false;
            }

            targetSymbol = resolved;
            return true;
        }

        var parts = expression.Split('.', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        if (string.Equals(parts[0], "this", StringComparison.Ordinal))
        {
            var thisTarget = symbolsByQualifiedName
                .Values
                .SingleOrDefault(symbol => symbol.QualifiedName.EndsWith($".{parts[1]}", StringComparison.Ordinal));
            if (thisTarget is null)
            {
                return false;
            }

            targetSymbol = thisTarget;
            return true;
        }

        if (!instanceBindings.TryGetValue(parts[0], out var classSymbol))
        {
            return false;
        }

        var qualifiedName = $"{classSymbol.DisplayName}.{parts[1]}";
        return symbolsByQualifiedName.TryGetValue(qualifiedName, out targetSymbol!);
    }

    private static SymbolRecord? ResolveTopLevelSymbol(
        string name,
        IReadOnlyDictionary<string, SymbolRecord[]> localSymbolsByName,
        IReadOnlyDictionary<string, SymbolRecord> importBindings)
    {
        if (importBindings.TryGetValue(name, out var importedTarget))
        {
            return importedTarget;
        }

        if (!localSymbolsByName.TryGetValue(name, out var localTargets) || localTargets.Length != 1)
        {
            return null;
        }

        return localTargets[0];
    }

    private static void AddEdge(
        SymbolRecord fromSymbol,
        SymbolRecord toSymbol,
        string relationType,
        string evidence,
        double confidence,
        HashSet<string> seen,
        List<SymbolDependencyRecord> edges)
    {
        if (fromSymbol.Id == toSymbol.Id)
        {
            return;
        }

        var id = $"edge:{fromSymbol.Id}:{relationType}:{toSymbol.Id}";
        if (!seen.Add(id))
        {
            return;
        }

        edges.Add(new SymbolDependencyRecord(
            id,
            fromSymbol.Id,
            toSymbol.Id,
            relationType,
            evidence,
            confidence));
    }

    private static bool ShouldAnalyzeRuntimeDependencies(SymbolRecord symbol) =>
        symbol.Kind is "function" or "method" or "constructor" or "variable";

    private static string ExtractRegion(string[] lines, int declarationLine, int nextDeclarationLine)
    {
        var startIndex = Math.Max(0, declarationLine - 1);
        var endIndex = Math.Max(startIndex, Math.Min(lines.Length, nextDeclarationLine - 1));
        return string.Join('\n', lines[startIndex..endIndex]);
    }

    private static bool IsConstructorMatch(string regionText, int callIndex)
    {
        var prefix = regionText[..callIndex];
        var trimmed = prefix.TrimEnd();
        return trimmed.EndsWith("new", StringComparison.Ordinal);
    }

    private static string CreateImportBindingKey(string fromFileId, string importText) =>
        $"{fromFileId}|{importText}";

    private static Dictionary<string, SymbolRecord> BuildAvailableTypeSymbols(
        IReadOnlyList<SymbolRecord> fileSymbols,
        IReadOnlyDictionary<string, SymbolRecord[]> localSymbolsByName,
        IReadOnlyDictionary<string, SymbolRecord> importBindings)
    {
        var available = new Dictionary<string, SymbolRecord>(StringComparer.Ordinal);

        foreach (var symbol in fileSymbols.Where(symbol => TypeLikeKinds.Contains(symbol.Kind) && !symbol.QualifiedName.Contains('.', StringComparison.Ordinal)))
        {
            available[symbol.DisplayName] = symbol;
        }

        foreach (var pair in importBindings)
        {
            if (TypeLikeKinds.Contains(pair.Value.Kind))
            {
                available[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in localSymbolsByName)
        {
            var typeTarget = pair.Value.SingleOrDefault(symbol => TypeLikeKinds.Contains(symbol.Kind));
            if (typeTarget is not null)
            {
                available[pair.Key] = typeTarget;
            }
        }

        return available;
    }

    private static IReadOnlyList<string> ExtractTypeIdentifiers(string typeExpression)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in TypeIdentifierPattern.Matches(typeExpression))
        {
            identifiers.Add(match.Value);
        }

        return identifiers.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static bool IsConstructorSelfType(SymbolRecord sourceSymbol, SymbolRecord targetSymbol) =>
        sourceSymbol.Kind == "constructor" &&
        sourceSymbol.QualifiedName.StartsWith($"{targetSymbol.DisplayName}.", StringComparison.Ordinal) &&
        targetSymbol.Kind == "class";
}
