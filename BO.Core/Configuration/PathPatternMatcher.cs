using System.Text.RegularExpressions;

namespace BO.Core.Configuration;

public static class PathPatternMatcher
{
    public static bool MatchesAny(string normalizedPath, IEnumerable<string> patterns) =>
        patterns.Any(pattern => Matches(normalizedPath, pattern));

    public static bool Matches(string normalizedPath, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var normalizedPattern = Normalize(pattern);
        var normalizedValue = Normalize(normalizedPath);
        var regex = "^" + Regex.Escape(normalizedPattern)
            .Replace("\\*\\*", ".*", StringComparison.Ordinal)
            .Replace("\\*", "[^/]*", StringComparison.Ordinal) + "$";

        return Regex.IsMatch(normalizedValue, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}

