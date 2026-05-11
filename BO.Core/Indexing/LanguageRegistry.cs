namespace BO.Core.Indexing;

/// <summary>
/// Maps file extensions to language definitions and tree-sitter grammar names.
/// This is the single source of truth for which languages BO supports.
/// To add a new language, add entries here — no other code changes needed for file discovery.
/// </summary>
public static class LanguageRegistry
{
    private static readonly Dictionary<string, LanguageInfo> ExtensionMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // TypeScript
            [".ts"]  = LanguageInfo.SingleArg("typescript", "TypeScript",  "web"),
            [".tsx"] = LanguageInfo.SingleArg("typescript", "TypeScript",  "web"),

            // JavaScript
            [".js"]  = LanguageInfo.SingleArg("javascript", "JavaScript",  "web"),
            [".jsx"] = LanguageInfo.SingleArg("javascript", "JavaScript",  "web"),
            [".mjs"] = LanguageInfo.SingleArg("javascript", "JavaScript",  "web"),
            [".cjs"] = LanguageInfo.SingleArg("javascript", "JavaScript",  "web"),

            // C#  —  native lib: libtree-sitter-c-sharp, function: tree_sitter_c_sharp
            [".cs"]  = new("csharp", "tree-sitter-c-sharp", "tree_sitter_c_sharp", "dotnet"),

            // Future languages — uncomment when extractors are ready:
            // [".java"]  = LanguageInfo.SingleArg("java",    "Java",    "jvm"),
            // [".c"]     = LanguageInfo.SingleArg("c",       "C",       "native"),
            // [".h"]     = LanguageInfo.SingleArg("c",       "C",       "native"),
            // [".cpp"]   = LanguageInfo.SingleArg("cpp",     "Cpp",     "native"),
            // [".cxx"]   = LanguageInfo.SingleArg("cpp",     "Cpp",     "native"),
            // [".cc"]    = LanguageInfo.SingleArg("cpp",     "Cpp",     "native"),
            // [".hpp"]   = LanguageInfo.SingleArg("cpp",     "Cpp",     "native"),
            // [".f"]     = LanguageInfo.SingleArg("fortran", "Fortran", "native"),
            // [".f90"]   = LanguageInfo.SingleArg("fortran", "Fortran", "native"),
            // [".f95"]   = LanguageInfo.SingleArg("fortran", "Fortran", "native"),
            // [".for"]   = LanguageInfo.SingleArg("fortran", "Fortran", "native"),
        };

    /// <summary>All file extensions that BO knows how to index.</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } =
        ExtensionMap.Keys.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Lookup a language by file extension. Returns null for unsupported extensions.</summary>
    public static LanguageInfo? FromExtension(string extension) =>
        ExtensionMap.TryGetValue(extension, out var info) ? info : null;

    /// <summary>Returns all unique language families currently registered.</summary>
    public static IReadOnlyList<string> SupportedLanguages =>
        ExtensionMap.Values
            .Select(v => v.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Returns the LanguageInfo for a file record, or null.</summary>
    public static LanguageInfo? GetLanguageInfo(FileRecord file) =>
        FromExtension(Path.GetExtension(file.Path));
}

/// <summary>Identifies a language, its tree-sitter grammar, and its ecosystem family.</summary>
public sealed record LanguageInfo(
    string Name,           // "csharp", "typescript", "java", etc.
    string LibraryName,    // Passed to Language(library, function) ctor
    string FunctionName,   // Passed to Language(library, function) ctor
    string Family)         // "dotnet", "web", "jvm", "native"
{
    /// <summary>For languages where the library follows the standard tree-sitter naming.</summary>
    public static LanguageInfo SingleArg(string name, string treeSitterId, string family) =>
        new(name,
            $"tree-sitter-{treeSitterId.ToLowerInvariant()}",
            $"tree_sitter_{treeSitterId.ToLowerInvariant()}",
            family);
}
