using System.Security.Cryptography;
using System.Text;

namespace BO.Core.Ids;

public sealed class BoIdGenerator
{
    public string CreateRepoId(string workspaceRoot)
    {
        var normalizedRoot = NormalizePath(workspaceRoot);
        var repoName = Path.GetFileName(normalizedRoot.TrimEnd('/'));
        var hash = ShortHash(normalizedRoot);
        return $"repo:{repoName}:{hash}";
    }

    public string CreateFileId(string repoId, string workspaceRoot, string filePath)
    {
        var relativePath = Path.GetRelativePath(workspaceRoot, filePath);
        return $"file:{repoId}:{NormalizePath(relativePath)}";
    }

    public string CreateModuleId(string repoId, string moduleQualifiedName)
    {
        return $"module:{repoId}:{moduleQualifiedName.Replace('\\', '/').Trim()}";
    }

    public string CreateSymbolId(
        string repoId,
        string qualifiedSymbolName,
        string fileId,
        string symbolKind,
        string signature,
        int declarationLine)
    {
        var shapeHash = ShortHash($"{fileId}|{symbolKind}|{signature}|{declarationLine}");
        return $"symbol:{repoId}:{qualifiedSymbolName}:{shapeHash}";
    }

    public static string NormalizePath(string path)
    {
        var full = path.Replace('\\', '/');

        if (full.Length >= 2 && full[1] == ':')
        {
            full = char.ToLowerInvariant(full[0]) + full[1..];
        }

        while (full.Contains("//", StringComparison.Ordinal))
        {
            full = full.Replace("//", "/", StringComparison.Ordinal);
        }

        return full.Trim();
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes[..6]);
    }
}
