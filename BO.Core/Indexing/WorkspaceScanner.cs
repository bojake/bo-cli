using BO.Core.Ids;

namespace BO.Core.Indexing;

public sealed class WorkspaceScanner
{
    private readonly BoIdGenerator _idGenerator;
    private readonly WorkspaceScanRules _defaultRules;

    public WorkspaceScanner(BoIdGenerator idGenerator, WorkspaceScanRules? defaultRules = null)
    {
        _idGenerator = idGenerator;
        _defaultRules = defaultRules ?? WorkspaceScanRules.Default;
    }

    public IndexResult Scan(
        string workspaceRoot,
        string packageRulesVersion,
        WorkspaceScanRules? scanRules = null)
    {
        var effectiveRules = scanRules ?? _defaultRules;
        var repoId = _idGenerator.CreateRepoId(workspaceRoot);
        var repoName = Path.GetFileName(Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var files = new List<FileRecord>();
        var warnings = new List<string>();
        var detectedLanguages = new HashSet<string>(StringComparer.Ordinal);

        foreach (var filePath in EnumerateCandidateFiles(workspaceRoot, effectiveRules))
        {
            var extension = Path.GetExtension(filePath);
            var langInfo = LanguageRegistry.FromExtension(extension);
            if (langInfo is null)
            {
                continue;
            }

            detectedLanguages.Add(langInfo.Name);

            var relativePath = Path.GetRelativePath(workspaceRoot, filePath);
            var normalizedPath = BoIdGenerator.NormalizePath(relativePath);
            var moduleName = ResolveModuleName(normalizedPath, langInfo, effectiveRules);
            var moduleId = _idGenerator.CreateModuleId(repoId, moduleName);
            var isTest = IsTestPath(normalizedPath, langInfo, effectiveRules);
            var isGenerated = IsGeneratedPath(normalizedPath, langInfo, effectiveRules);

            files.Add(new FileRecord(
                _idGenerator.CreateFileId(repoId, workspaceRoot, filePath),
                repoId,
                filePath,
                normalizedPath,
                langInfo.Name,
                isTest,
                isGenerated,
                moduleId));
        }

        if (files.Count == 0)
        {
            warnings.Add("No supported source files were found in the workspace.");
        }

        return new IndexResult(
            new RepoRecord(repoId, repoName, Path.GetFullPath(workspaceRoot), detectedLanguages.OrderBy(l => l).ToList(), "0.1.0"),
            files,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            0,
            packageRulesVersion,
            warnings);
    }

    private static IEnumerable<string> EnumerateCandidateFiles(
        string workspaceRoot,
        WorkspaceScanRules rules)
    {
        var excludedDirectories = new HashSet<string>(rules.ExcludedDirectories, StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(workspaceRoot));

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(directory);
                if (excludedDirectories.Contains(name))
                {
                    continue;
                }

                pending.Push(directory);
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                yield return file;
            }
        }
    }

    private static string ResolveModuleName(
        string normalizedPath,
        LanguageInfo langInfo,
        WorkspaceScanRules rules)
    {
        var moduleRule = ResolveModuleRule(langInfo, rules);
        if (moduleRule.Mode.Equals("first_path_segment", StringComparison.OrdinalIgnoreCase))
        {
            var parts = normalizedPath.Split('/');
            return parts.Length > 1 ? parts[0] : moduleRule.RootModuleName;
        }

        var directory = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/') ?? string.Empty;
        return string.IsNullOrWhiteSpace(directory) ? moduleRule.RootModuleName : directory;
    }

    private static bool IsTestPath(
        string normalizedPath,
        LanguageInfo langInfo,
        WorkspaceScanRules rules)
    {
        return ResolvePathRules(langInfo, rules.TestPathRules)
            .Any(rule => MatchesPathRule(normalizedPath, rule));
    }

    private static bool IsGeneratedPath(
        string normalizedPath,
        LanguageInfo langInfo,
        WorkspaceScanRules rules)
    {
        return ResolvePathRules(langInfo, rules.GeneratedPathRules)
            .Any(rule => MatchesPathRule(normalizedPath, rule));
    }

    private static WorkspaceModuleRule ResolveModuleRule(LanguageInfo langInfo, WorkspaceScanRules rules)
    {
        if (rules.ModuleRules.TryGetValue(langInfo.Family, out var languageRule))
        {
            return languageRule;
        }

        return rules.ModuleRules.TryGetValue("default", out var defaultRule)
            ? defaultRule
            : WorkspaceScanRules.Default.ModuleRules["default"];
    }

    private static IEnumerable<WorkspacePathRule> ResolvePathRules(
        LanguageInfo langInfo,
        IReadOnlyList<WorkspacePathRule> pathRules)
    {
        var exactRules = pathRules
            .Where(rule => rule.LanguageFamily.Equals(langInfo.Family, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return exactRules.Length > 0
            ? exactRules
            : pathRules.Where(rule => rule.LanguageFamily.Equals("default", StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesPathRule(string normalizedPath, WorkspacePathRule rule)
    {
        return rule.Contains.Any(part => normalizedPath.Contains(part, StringComparison.OrdinalIgnoreCase))
            || rule.EndsWith.Any(suffix => normalizedPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
