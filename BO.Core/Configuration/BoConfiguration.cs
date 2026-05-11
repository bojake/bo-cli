using System.Text.Json.Serialization;

namespace BO.Core.Configuration;

public sealed record BoConfiguration(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("boundaries")] IReadOnlyList<BoBoundaryConfiguration> Boundaries,
    [property: JsonPropertyName("package_classification")] BoPackageClassificationConfiguration PackageClassification,
    [property: JsonPropertyName("indexing")] BoIndexingConfiguration Indexing,
    [property: JsonPropertyName("refactor_pressure")] BoRefactorPressureConfiguration RefactorPressure)
{
    public static BoConfiguration Empty { get; } = new(
        "0.1.0",
        [],
        new BoPackageClassificationConfiguration([], []),
        new BoIndexingConfiguration([], true),
        new BoRefactorPressureConfiguration([]));
}

public sealed record BoBoundaryConfiguration(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("path_patterns")] IReadOnlyList<string> PathPatterns,
    [property: JsonPropertyName("generated")] bool Generated = false);

public sealed record BoPackageClassificationConfiguration(
    [property: JsonPropertyName("internal_patterns")] IReadOnlyList<string> InternalPatterns,
    [property: JsonPropertyName("external_patterns")] IReadOnlyList<string> ExternalPatterns);

public sealed record BoIndexingConfiguration(
    [property: JsonPropertyName("exclude_path_patterns")] IReadOnlyList<string> ExcludePathPatterns,
    [property: JsonPropertyName("treat_generated_as_low_signal")] bool TreatGeneratedAsLowSignal);

public sealed record BoRefactorPressureConfiguration(
    [property: JsonPropertyName("ignore_boundaries")] IReadOnlyList<string> IgnoreBoundaries);

