namespace Otimizador.Core;

/// <summary>
/// Percorre pastas com segurança: nunca segue junções/links simbólicos (evita apagar fora da pasta alvo)
/// e ignora o que não tem permissão de leitura.
/// </summary>
internal static class FileWalker
{
    public static IEnumerable<FileInfo> Files(string root, bool recursive = true, string pattern = "*",
        CancellationToken ct = default)
    {
        DirectoryInfo start;
        try
        {
            start = new DirectoryInfo(root);
            if (!start.Exists || start.Attributes.HasFlag(FileAttributes.ReparsePoint)) yield break;
        }
        catch { yield break; }

        var stack = new Stack<DirectoryInfo>();
        stack.Push(start);
        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            FileSystemInfo[] entries;
            try { entries = dir.GetFileSystemInfos("*", opts); }
            catch { continue; }

            foreach (var e in entries)
            {
                FileAttributes attr;
                try { attr = e.Attributes; } catch { continue; }
                if (attr.HasFlag(FileAttributes.ReparsePoint)) continue;

                if (e is DirectoryInfo sub)
                {
                    if (recursive) stack.Push(sub);
                }
                else if (e is FileInfo fi && (pattern == "*" || MatchesPattern(fi.Name, pattern)))
                {
                    yield return fi;
                }
            }
        }
    }

    private static bool MatchesPattern(string name, string pattern) =>
        System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true);

    /// <summary>Remove subpastas vazias (de baixo para cima), mantendo a raiz. Só apaga pastas mais antigas que o limite.</summary>
    public static void RemoveEmptyDirs(string root, DateTime olderThanUtc)
    {
        try
        {
            var rootInfo = new DirectoryInfo(root);
            if (!rootInfo.Exists) return;
            foreach (var d in rootInfo.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                RemoveIfEmpty(d, olderThanUtc);
        }
        catch { }
    }

    private static void RemoveIfEmpty(DirectoryInfo d, DateTime olderThanUtc)
    {
        try
        {
            if (d.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
            foreach (var sub in d.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                RemoveIfEmpty(sub, olderThanUtc);
            d.Refresh();
            if (d.CreationTimeUtc < olderThanUtc && !d.EnumerateFileSystemInfos().Any()) d.Delete();
        }
        catch { }
    }

    /// <summary>Tenta apagar um arquivo (remove "somente leitura" se preciso).</summary>
    public static bool TryDelete(FileInfo f)
    {
        try
        {
            if (f.IsReadOnly) f.IsReadOnly = false;
            f.Delete();
            return true;
        }
        catch { return false; }
    }
}
