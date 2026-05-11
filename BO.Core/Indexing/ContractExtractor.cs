namespace BO.Core.Indexing;

public sealed class ContractExtractor
{
    private static readonly HashSet<string> CallableSymbolKinds = new(StringComparer.Ordinal)
    {
        "function",
        "method",
        "constructor"
    };

    public IReadOnlyList<ContractRecord> Extract(
        IReadOnlyList<FileRecord> files,
        IReadOnlyList<SymbolRecord> symbols)
    {
        var filesById = files.ToDictionary(file => file.Id, StringComparer.Ordinal);
        var symbolsByFile = symbols
            .GroupBy(symbol => symbol.FileId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(symbol => symbol.DeclarationLine)
                    .ThenBy(symbol => symbol.QualifiedName, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var contracts = new List<ContractRecord>();

        foreach (var file in files)
        {
            if (!symbolsByFile.TryGetValue(file.Id, out var fileSymbols) || fileSymbols.Length == 0)
            {
                continue;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(file.Path);
            }
            catch
            {
                continue;
            }

            for (var index = 0; index < fileSymbols.Length; index++)
            {
                var symbol = fileSymbols[index];
                if (!ShouldExtractContract(symbol))
                {
                    continue;
                }

                var nextDeclarationLine = index + 1 < fileSymbols.Length
                    ? fileSymbols[index + 1].DeclarationLine
                    : lines.Length + 1;

                var regionText = ExtractRegion(lines, symbol.DeclarationLine, nextDeclarationLine);
                var parsedSignature = ParseSignature(symbol);
                var throwsModes = InferThrowsOrErrorModes(parsedSignature.OutputTypes, regionText);
                var nullability = new ContractNullability(
                    AcceptsNullableInput: parsedSignature.ParameterTypes.Any(IsNullableType),
                    ReturnsNullableOutput: parsedSignature.OutputTypes.Any(IsNullableType),
                    HasOptionalParameters: parsedSignature.HasOptionalParameters);

                contracts.Add(new ContractRecord(
                    $"contract:{symbol.Id}",
                    symbol.Id,
                    parsedSignature.ParameterTypes,
                    parsedSignature.OutputTypes,
                    parsedSignature.GenericConstraints,
                    throwsModes,
                    [],
                    nullability,
                    parsedSignature.AsyncMode,
                    CalculateConfidence(symbol, parsedSignature, throwsModes)));
            }
        }

        return contracts;
    }

    private static bool ShouldExtractContract(SymbolRecord symbol)
    {
        if (CallableSymbolKinds.Contains(symbol.Kind))
        {
            return true;
        }

        return symbol.Kind == "variable" &&
               (symbol.Signature.Contains("=>", StringComparison.Ordinal) ||
                symbol.Signature.Contains("function", StringComparison.Ordinal));
    }

    private static string ExtractRegion(string[] lines, int declarationLine, int nextDeclarationLine)
    {
        var startIndex = Math.Max(0, declarationLine - 1);
        var endIndex = Math.Max(startIndex, Math.Min(lines.Length, nextDeclarationLine - 1));
        return string.Join('\n', lines[startIndex..endIndex]);
    }

    private static ParsedSignature ParseSignature(SymbolRecord symbol)
    {
        if (symbol.Language == "csharp")
        {
            return ParseCSharpSignature(symbol);
        }

        return ParseTypeScriptSignature(symbol);
    }

    // ── TypeScript/JavaScript signature parsing ──────────────────────────────

    private static ParsedSignature ParseTypeScriptSignature(SymbolRecord symbol)
    {
        var signature = symbol.Signature.Trim();
        var parameterTypes = Array.Empty<string>();
        var genericConstraints = Array.Empty<string>();
        var hasOptionalParameters = false;
        var explicitReturnType = string.Empty;

        var parameterRange = FindParameterRange(signature);
        if (parameterRange is not null)
        {
            parameterTypes = ExtractParameterTypes(signature[parameterRange.Value.Start..parameterRange.Value.End], out hasOptionalParameters);
            genericConstraints = ExtractGenericConstraints(signature, parameterRange.Value.Start);
            explicitReturnType = ExtractExplicitReturnType(signature, parameterRange.Value.End);
        }

        var outputTypes = BuildOutputTypes(symbol, explicitReturnType);
        return new ParsedSignature(
            parameterTypes,
            outputTypes,
            genericConstraints,
            hasOptionalParameters,
            InferAsyncMode(signature, explicitReturnType));
    }

    // ── C# signature parsing ────────────────────────────────────────────────

    private static ParsedSignature ParseCSharpSignature(SymbolRecord symbol)
    {
        var signature = symbol.Signature.Trim();
        var parameterRange = FindParameterRange(signature);
        var parameterTypes = Array.Empty<string>();
        var hasOptionalParameters = false;

        if (parameterRange is not null)
        {
            parameterTypes = ExtractCSharpParameterTypes(
                signature[parameterRange.Value.Start..parameterRange.Value.End],
                out hasOptionalParameters);
        }

        var returnType = symbol.Kind == "constructor"
            ? string.Empty
            : ExtractCSharpReturnType(signature);

        var genericConstraints = ExtractCSharpGenericConstraints(signature);
        var outputTypes = BuildOutputTypes(symbol, returnType);
        var asyncMode = InferAsyncMode(signature, returnType);

        return new ParsedSignature(
            parameterTypes,
            outputTypes,
            genericConstraints,
            hasOptionalParameters,
            asyncMode);
    }

    /// <summary>
    /// C# parameters: "Type paramName, Type2 paramName2 = default"
    /// → extracts ["Type", "Type2"]
    /// </summary>
    private static string[] ExtractCSharpParameterTypes(string parameterSection, out bool hasOptionalParameters)
    {
        hasOptionalParameters = false;
        var parameterTypes = new List<string>();

        foreach (var rawParameter in SplitTopLevel(parameterSection, ','))
        {
            var parameter = rawParameter.Trim();
            if (parameter.Length == 0)
            {
                continue;
            }

            // Remove default values: "bool force = false" → "bool force"
            var withoutDefault = TrimAfterTopLevel(parameter, '=').Trim();
            if (withoutDefault != parameter)
            {
                hasOptionalParameters = true;
            }

            // Remove parameter modifiers: ref, out, in, params, this
            var cleaned = StripCSharpParameterModifiers(withoutDefault);

            // C# format: "Type paramName" — take everything except the last token
            var lastSpace = cleaned.LastIndexOf(' ');
            if (lastSpace <= 0)
            {
                continue;
            }

            var typePart = cleaned[..lastSpace].Trim();
            if (typePart.Length == 0)
            {
                continue;
            }

            // Check for nullable marker
            if (typePart.EndsWith("?", StringComparison.Ordinal))
            {
                hasOptionalParameters = true;
            }

            parameterTypes.Add(typePart);
        }

        return [.. parameterTypes];
    }

    /// <summary>
    /// Extracts the return type from a C# method signature.
    /// e.g. "public async Task&lt;string&gt; GetDataAsync(int id)" → "Task&lt;string&gt;"
    /// </summary>
    private static string ExtractCSharpReturnType(string signature)
    {
        // Find the parameter list opening paren (use IndexOf, not FindTopLevelCharacter,
        // because '(' is consumed by the depth-tracking switch before reaching the target check)
        var parenIndex = signature.IndexOf('(');
        if (parenIndex < 0)
        {
            return string.Empty;
        }

        // Everything before '(' is: [modifiers] [return_type] [method_name][<generics>]
        var beforeParen = signature[..parenIndex].Trim();

        // Handle generic method name: "DoStuff<T>" → strip the "<T>" to get method name
        var genericStart = beforeParen.LastIndexOf('<');
        var genericEnd = beforeParen.LastIndexOf('>');
        if (genericStart > 0 && genericEnd > genericStart)
        {
            // Check if this is a generic on the method name (not the return type)
            var afterGeneric = beforeParen[(genericEnd + 1)..].Trim();
            if (afterGeneric.Length == 0)
            {
                beforeParen = beforeParen[..genericStart].Trim();
            }
        }

        // Split by whitespace from right to left
        // Last token = method name, token before that = return type
        var lastSpace = beforeParen.LastIndexOf(' ');
        if (lastSpace <= 0)
        {
            return string.Empty;
        }

        var returnTypeCandidate = beforeParen[..lastSpace].Trim();

        // Strip modifiers: public, private, protected, internal, static, virtual, override,
        // abstract, sealed, async, extern, new, readonly, partial, unsafe, volatile
        var modifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "public", "private", "protected", "internal",
            "static", "virtual", "override", "abstract",
            "sealed", "async", "extern", "new",
            "readonly", "partial", "unsafe", "volatile"
        };

        // Remove modifiers from the left
        while (returnTypeCandidate.Length > 0)
        {
            var spaceIdx = returnTypeCandidate.IndexOf(' ');
            if (spaceIdx < 0)
            {
                break;
            }

            var firstToken = returnTypeCandidate[..spaceIdx].Trim();
            if (modifiers.Contains(firstToken))
            {
                returnTypeCandidate = returnTypeCandidate[(spaceIdx + 1)..].Trim();
            }
            else
            {
                break;
            }
        }

        // Final check: if what's left is a modifier (e.g. "void" caught as the whole thing),
        // but modifiers is not the type itself
        if (modifiers.Contains(returnTypeCandidate) || string.IsNullOrWhiteSpace(returnTypeCandidate))
        {
            return string.Empty;
        }

        return returnTypeCandidate;
    }

    /// <summary>
    /// Extracts C# generic constraints: "where T : class, IDisposable"
    /// </summary>
    private static string[] ExtractCSharpGenericConstraints(string signature)
    {
        var constraints = new List<string>();
        var whereIndex = signature.IndexOf(" where ", StringComparison.Ordinal);
        while (whereIndex >= 0)
        {
            var constraintStart = whereIndex + 7; // length of " where "
            // Find end of this constraint: next "where" or end of pre-body region
            var nextWhere = signature.IndexOf(" where ", constraintStart, StringComparison.Ordinal);
            var bodyStart = FindTopLevelCharacter(signature[constraintStart..], '{');
            var arrowStart = signature.IndexOf("=>", constraintStart, StringComparison.Ordinal);

            var endIndex = signature.Length;
            if (nextWhere >= 0) endIndex = Math.Min(endIndex, nextWhere);
            if (bodyStart >= 0) endIndex = Math.Min(endIndex, constraintStart + bodyStart);
            if (arrowStart >= 0) endIndex = Math.Min(endIndex, arrowStart);

            var constraint = signature[constraintStart..endIndex].Trim();
            if (constraint.Length > 0)
            {
                constraints.Add(constraint);
            }

            whereIndex = nextWhere;
        }

        return [.. constraints];
    }

    private static string StripCSharpParameterModifiers(string parameter)
    {
        var modifiers = new[] { "ref ", "out ", "in ", "params ", "this ", "scoped " };
        var result = parameter;
        foreach (var modifier in modifiers)
        {
            if (result.StartsWith(modifier, StringComparison.Ordinal))
            {
                result = result[modifier.Length..].Trim();
            }
        }
        return result;
    }

    private static (int Start, int End)? FindParameterRange(string signature)
    {
        var openIndex = signature.IndexOf('(');
        if (openIndex < 0)
        {
            return null;
        }

        var depth = 0;
        for (var index = openIndex; index < signature.Length; index++)
        {
            switch (signature[index])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        return (openIndex + 1, index);
                    }
                    break;
            }
        }

        return null;
    }

    private static string[] ExtractParameterTypes(string parameterSection, out bool hasOptionalParameters)
    {
        hasOptionalParameters = false;
        var parameterTypes = new List<string>();

        foreach (var rawParameter in SplitTopLevel(parameterSection, ','))
        {
            var parameter = rawParameter.Trim();
            if (parameter.Length == 0)
            {
                continue;
            }

            var withoutDefault = TrimAfterTopLevel(parameter, '=').Trim();
            var colonIndex = FindTopLevelCharacter(withoutDefault, ':');
            if (colonIndex < 0)
            {
                continue;
            }

            var namePart = withoutDefault[..colonIndex].Trim();
            var typePart = withoutDefault[(colonIndex + 1)..].Trim();
            if (typePart.Length == 0)
            {
                continue;
            }

            if (namePart.EndsWith("?", StringComparison.Ordinal))
            {
                hasOptionalParameters = true;
            }

            parameterTypes.Add(typePart);
        }

        return [.. parameterTypes];
    }

    private static string[] ExtractGenericConstraints(string signature, int parameterStartIndex)
    {
        var prefix = signature[..Math.Max(0, parameterStartIndex - 1)];
        var openIndex = prefix.LastIndexOf('<');
        if (openIndex < 0)
        {
            return [];
        }

        var closeIndex = prefix.LastIndexOf('>');
        if (closeIndex <= openIndex)
        {
            return [];
        }

        var genericSection = prefix[(openIndex + 1)..closeIndex];
        return SplitTopLevel(genericSection, ',')
            .Select(item => item.Trim())
            .Where(item => item.Contains("extends", StringComparison.Ordinal))
            .ToArray();
    }

    private static string ExtractExplicitReturnType(string signature, int parameterEndIndex)
    {
        var remaining = signature[(parameterEndIndex + 1)..].TrimStart();
        if (!remaining.StartsWith(':'))
        {
            return string.Empty;
        }

        remaining = remaining[1..].TrimStart();
        var returnType = TrimAtTopLevelDelimiter(remaining, '{', '=', ';');
        if (returnType.EndsWith(">", StringComparison.Ordinal) && remaining.Contains("=>", StringComparison.Ordinal))
        {
            return returnType;
        }

        return returnType.Trim();
    }

    private static string[] BuildOutputTypes(SymbolRecord symbol, string explicitReturnType)
    {
        if (!string.IsNullOrWhiteSpace(explicitReturnType))
        {
            var trimmed = explicitReturnType.Trim();
            // 'void' produces no output type
            if (string.Equals(trimmed, "void", StringComparison.Ordinal))
            {
                return [];
            }
            return [trimmed];
        }

        if (symbol.Kind == "constructor")
        {
            // For C#, DisplayName is the class name (e.g. "SampleService")
            // For TS/JS, DisplayName is "constructor" — derive class name from QualifiedName
            var className = symbol.DisplayName != "constructor"
                ? symbol.DisplayName
                : DeriveClassNameFromQualifiedName(symbol.QualifiedName);
            return string.IsNullOrWhiteSpace(className) ? [] : [className];
        }

        return [];
    }

    private static string DeriveClassNameFromQualifiedName(string qualifiedName)
    {
        // "FriendlyGreeter.constructor" → "FriendlyGreeter"
        // "BO.Core.Services.SampleService.SampleService" → "SampleService" (second-to-last)
        var parts = qualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return parts[^2]; // second-to-last is the class name
        }
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    private static string InferAsyncMode(string signature, string explicitReturnType)
    {
        if (signature.Contains("async ", StringComparison.Ordinal))
        {
            return "async";
        }

        // TS: Promise<T>
        if (explicitReturnType.Contains("Promise<", StringComparison.Ordinal) || string.Equals(explicitReturnType, "Promise", StringComparison.Ordinal))
        {
            return "promise";
        }

        // C#: Task<T>, ValueTask<T>, Task, ValueTask
        if (explicitReturnType.Contains("Task<", StringComparison.Ordinal)
            || string.Equals(explicitReturnType, "Task", StringComparison.Ordinal)
            || explicitReturnType.Contains("ValueTask<", StringComparison.Ordinal)
            || string.Equals(explicitReturnType, "ValueTask", StringComparison.Ordinal))
        {
            return "async";
        }

        return "sync";
    }

    private static string[] InferThrowsOrErrorModes(IReadOnlyList<string> outputTypes, string regionText)
    {
        var modes = new HashSet<string>(StringComparer.Ordinal);
        if (regionText.Contains("throw ", StringComparison.Ordinal) || regionText.Contains("throw new", StringComparison.Ordinal))
        {
            modes.Add("throw");
        }

        foreach (var outputType in outputTypes)
        {
            if (outputType.Contains("Result<", StringComparison.Ordinal) || outputType.Contains("Either<", StringComparison.Ordinal))
            {
                modes.Add("result_return");
            }
            else if (outputType.Contains("Error", StringComparison.Ordinal))
            {
                modes.Add("error_return");
            }
        }

        return modes.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static double CalculateConfidence(
        SymbolRecord symbol,
        ParsedSignature parsedSignature,
        IReadOnlyList<string> throwsModes)
    {
        var hasExplicitTypes = parsedSignature.ParameterTypes.Count > 0 || parsedSignature.OutputTypes.Count > 0 || parsedSignature.GenericConstraints.Count > 0;
        if (hasExplicitTypes)
        {
            return throwsModes.Count > 0 ? 0.8 : 0.78;
        }

        if (symbol.Kind == "constructor" && parsedSignature.OutputTypes.Count > 0)
        {
            return 0.76;
        }

        return 0.7;
    }

    private static bool IsNullableType(string typeText)
    {
        var value = typeText.Trim();
        return value.Contains("null", StringComparison.Ordinal) ||
               value.Contains("undefined", StringComparison.Ordinal) ||
               value.Contains("?", StringComparison.Ordinal);
    }

    private static string TrimAfterTopLevel(string value, char delimiter)
    {
        var index = FindTopLevelCharacter(value, delimiter);
        return index < 0 ? value : value[..index];
    }

    private static string TrimAtTopLevelDelimiter(string value, params char[] delimiters)
    {
        var depthAngles = 0;
        var depthParens = 0;
        var depthBrackets = 0;
        var depthBraces = 0;

        for (var index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '<':
                    depthAngles++;
                    break;
                case '>':
                    depthAngles = Math.Max(0, depthAngles - 1);
                    break;
                case '(':
                    depthParens++;
                    break;
                case ')':
                    depthParens = Math.Max(0, depthParens - 1);
                    break;
                case '[':
                    depthBrackets++;
                    break;
                case ']':
                    depthBrackets = Math.Max(0, depthBrackets - 1);
                    break;
                case '{':
                    if (depthAngles == 0 && depthParens == 0 && depthBrackets == 0 && depthBraces == 0 && delimiters.Contains('{'))
                    {
                        return value[..index].Trim();
                    }

                    depthBraces++;
                    break;
                case '}':
                    depthBraces = Math.Max(0, depthBraces - 1);
                    break;
                case '=':
                case ';':
                    if (depthAngles == 0 && depthParens == 0 && depthBrackets == 0 && depthBraces == 0 && delimiters.Contains(value[index]))
                    {
                        return value[..index].Trim();
                    }
                    break;
            }
        }

        return value.Trim();
    }

    private static int FindTopLevelCharacter(string value, char target)
    {
        var depthAngles = 0;
        var depthParens = 0;
        var depthBrackets = 0;
        var depthBraces = 0;

        for (var index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '<':
                    depthAngles++;
                    break;
                case '>':
                    depthAngles = Math.Max(0, depthAngles - 1);
                    break;
                case '(':
                    depthParens++;
                    break;
                case ')':
                    depthParens = Math.Max(0, depthParens - 1);
                    break;
                case '[':
                    depthBrackets++;
                    break;
                case ']':
                    depthBrackets = Math.Max(0, depthBrackets - 1);
                    break;
                case '{':
                    depthBraces++;
                    break;
                case '}':
                    depthBraces = Math.Max(0, depthBraces - 1);
                    break;
                default:
                    if (value[index] == target && depthAngles == 0 && depthParens == 0 && depthBrackets == 0 && depthBraces == 0)
                    {
                        return index;
                    }
                    break;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> SplitTopLevel(string value, char separator)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var depthAngles = 0;
        var depthParens = 0;
        var depthBrackets = 0;
        var depthBraces = 0;

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '<':
                    depthAngles++;
                    current.Append(ch);
                    continue;
                case '>':
                    depthAngles = Math.Max(0, depthAngles - 1);
                    current.Append(ch);
                    continue;
                case '(':
                    depthParens++;
                    current.Append(ch);
                    continue;
                case ')':
                    depthParens = Math.Max(0, depthParens - 1);
                    current.Append(ch);
                    continue;
                case '[':
                    depthBrackets++;
                    current.Append(ch);
                    continue;
                case ']':
                    depthBrackets = Math.Max(0, depthBrackets - 1);
                    current.Append(ch);
                    continue;
                case '{':
                    depthBraces++;
                    current.Append(ch);
                    continue;
                case '}':
                    depthBraces = Math.Max(0, depthBraces - 1);
                    current.Append(ch);
                    continue;
            }

            if (ch == separator && depthAngles == 0 && depthParens == 0 && depthBrackets == 0 && depthBraces == 0)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private sealed record ParsedSignature(
        IReadOnlyList<string> ParameterTypes,
        IReadOnlyList<string> OutputTypes,
        IReadOnlyList<string> GenericConstraints,
        bool HasOptionalParameters,
        string AsyncMode);
}
