using System.Text.Json;

namespace BO.Core.Indexing;

public sealed record SeamDomainRules(
    string Version,
    IReadOnlyList<SeamSupportDomainRule> SupportDomains,
    IReadOnlyList<SeamMethodDomainRule> MethodDomains,
    string DefaultMethodDomain,
    string CoreHelperFallbackDomain)
{
    private const string EnvironmentPathKey = "BO_SEAM_DOMAIN_RULES_PATH";
    private const string DefaultFileName = "seam_domain_rules.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static SeamDomainRules LoadDefault()
    {
        var configuredPath = Environment.GetEnvironmentVariable(EnvironmentPathKey);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Load(configuredPath);
        }

        foreach (var candidatePath in EnumerateDefaultRulePaths())
        {
            if (File.Exists(candidatePath))
            {
                return Load(candidatePath);
            }
        }

        throw new FileNotFoundException(
            $"Seam domain rules file was not found. Set {EnvironmentPathKey} or place {DefaultFileName} in the workspace or output directory.");
    }

    public static SeamDomainRules Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Seam domain rules file was not found.", path);
        }

        var json = File.ReadAllText(path);
        var rules = JsonSerializer.Deserialize<SeamDomainRules>(json, JsonOptions);
        if (rules is null)
        {
            throw new InvalidOperationException("Failed to deserialize seam domain rules.");
        }

        return rules;
    }

    private static IEnumerable<string> EnumerateDefaultRulePaths()
    {
        foreach (var root in EnumerateSearchRoots(AppContext.BaseDirectory))
        {
            yield return Path.Combine(root, DefaultFileName);
            yield return Path.Combine(root, "Rules", DefaultFileName);
        }

        foreach (var root in EnumerateSearchRoots(Directory.GetCurrentDirectory()))
        {
            yield return Path.Combine(root, DefaultFileName);
            yield return Path.Combine(root, "Rules", DefaultFileName);
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots(string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }
}

public sealed record SeamSupportDomainRule(
    string Domain,
    IReadOnlyList<string> MethodContains,
    IReadOnlyList<string> QualifiedNameContains,
    IReadOnlyList<string> ExactMethodNames);

public sealed record SeamMethodDomainRule(
    string Domain,
    IReadOnlyList<string> Keywords);
