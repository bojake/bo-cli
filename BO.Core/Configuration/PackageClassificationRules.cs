using System.Text.Json.Serialization;

namespace BO.Core.Configuration;

public sealed record PackageClassificationRules(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("boundaries")] IReadOnlyList<BoundaryRule> Boundaries);

public sealed record BoundaryRule(
    [property: JsonPropertyName("boundary_type")] string BoundaryType,
    [property: JsonPropertyName("packages")] IReadOnlyList<string> Packages,
    [property: JsonPropertyName("symbol_patterns")] IReadOnlyList<string> SymbolPatterns,
    [property: JsonPropertyName("operation_overrides")] IReadOnlyDictionary<string, string> OperationOverrides);
