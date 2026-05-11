using System.Text.Json.Serialization;

namespace BO.Core.Indexing;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RefactorDepth
{
    StructuralExtraction = 1,
    ContractShaping = 2,
    Generalization = 3,
    ArchitecturalRefactor = 4
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RefactorStyle
{
    Balanced,
    Domain,
    Reuse,
    Testability,
    Cleanup,
    Performance
}

public sealed record RefactorIntent(
    RefactorDepth Depth,
    RefactorStyle Style,
    bool PreservePublicSurface = true,
    string MaxRisk = "medium",
    string ValidationStrength = "balanced")
{
    public static RefactorIntent Default { get; } = new(RefactorDepth.StructuralExtraction, RefactorStyle.Balanced);
}

public enum RefactorTransformationFamily
{
    StructuralExtraction = 1,
    ContractShaping = 2,
    Generalization = 3,
    ArchitecturalRefactor = 4
}

public static class RefactorIntentParser
{
    public static bool TryParseDepth(string? value, out RefactorDepth depth)
    {
        depth = RefactorDepth.StructuralExtraction;
        return value switch
        {
            "1" => Assign(RefactorDepth.StructuralExtraction, out depth),
            "2" => Assign(RefactorDepth.ContractShaping, out depth),
            "3" => Assign(RefactorDepth.Generalization, out depth),
            "4" => Assign(RefactorDepth.ArchitecturalRefactor, out depth),
            _ => false
        };
    }

    public static bool TryParseStyle(string? value, out RefactorStyle style)
    {
        style = RefactorStyle.Balanced;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "balanced" => Assign(RefactorStyle.Balanced, out style),
            "domain" => Assign(RefactorStyle.Domain, out style),
            "reuse" => Assign(RefactorStyle.Reuse, out style),
            "testability" => Assign(RefactorStyle.Testability, out style),
            "cleanup" => Assign(RefactorStyle.Cleanup, out style),
            "performance" => Assign(RefactorStyle.Performance, out style),
            _ => false
        };
    }

    public static string ToCliValue(this RefactorDepth depth)
    {
        return ((int)depth).ToString();
    }

    public static string ToCliValue(this RefactorStyle style)
    {
        return style switch
        {
            RefactorStyle.Balanced => "balanced",
            RefactorStyle.Domain => "domain",
            RefactorStyle.Reuse => "reuse",
            RefactorStyle.Testability => "testability",
            RefactorStyle.Cleanup => "cleanup",
            RefactorStyle.Performance => "performance",
            _ => "balanced"
        };
    }

    public static RefactorDepth MinimumDepth(this RefactorTransformationFamily family)
    {
        return family switch
        {
            RefactorTransformationFamily.StructuralExtraction => RefactorDepth.StructuralExtraction,
            RefactorTransformationFamily.ContractShaping => RefactorDepth.ContractShaping,
            RefactorTransformationFamily.Generalization => RefactorDepth.Generalization,
            RefactorTransformationFamily.ArchitecturalRefactor => RefactorDepth.ArchitecturalRefactor,
            _ => RefactorDepth.StructuralExtraction
        };
    }

    private static bool Assign<T>(T value, out T destination)
    {
        destination = value;
        return true;
    }
}
