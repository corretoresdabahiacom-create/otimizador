using System.Buffers;
using System.Security.Cryptography;
using Otimizador.Core;

namespace Otimizador.Features;

internal sealed class DuplicateGroup
{
    public long Size { get; init; }
    public List<FileInfo> Files { get; init; } = new();
    public long Wasted => Size * (Files.Count - 1);
}

/// <summary>
/// Localizador de duplicados real: agrupa por tamanho, depois compara um "hash rápido"
/// (início + fim do arquivo) e confirma com SHA-256 completo. Nada é apagado aqui.
/// </summary>
internal static class DuplicateFinder
{
    private const int Chunk = 64 * 1024;

    public static Task<List<DuplicateGroup>> FindAsync(string root, long minSize, IProgress<string> progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var bySize = new Dictionary<long, List<FileInfo>>();
            int scanned = 0;
            foreach (var f in FileWalker.Files(root, ct: ct))
            {
                try
                {
                    if (f.Attributes.HasFlag(FileAttributes.System) || IsProtected(f.FullName)) continue;
                    long len = f.Length;
                    if (len < minSize) continue;
                    if (!bySize.TryGetValue(len, out var l)) bySize[len] = l = new List<FileInfo>();
                    l.Add(f);
                }
                catch { continue; }
                if (++scanned % 500 == 0) progress.Report($"Lendo arquivos… {Fmt.Num(scanned)}");
            }

            var candidates = bySize.Where(kv => kv.Value.Count > 1).ToList();
            int total = candidates.Sum(c => c.Value.Count), done = 0;
            var result = new List<DuplicateGroup>();

            foreach (var (size, files) in candidates)
            {
                ct.ThrowIfCancellationRequested();
                // 1ª passada: hash rápido
                var quick = new Dictionary<string, List<FileInfo>>();
                foreach (var f in files)
                {
                    var h = Hash(f, quickOnly: true, ct);
                    done++;
                    if (done % 50 == 0) progress.Report($"Comparando conteúdo… {done * 100 / Math.Max(1, total)}%");
                    if (h == null) continue;
                    if (!quick.TryGetValue(h, out var l)) quick[h] = l = new List<FileInfo>();
                    l.Add(f);
                }
                // 2ª passada: SHA-256 completo (só quando o arquivo é maior que o trecho já lido)
                foreach (var q in quick.Values.Where(v => v.Count > 1))
                {
                    if (size <= Chunk * 2)
                    {
                        result.Add(new DuplicateGroup { Size = size, Files = q });
                        continue;
                    }
                    var full = new Dictionary<string, List<FileInfo>>();
                    foreach (var f in q)
                    {
                        var h = Hash(f, quickOnly: false, ct);
                        if (h == null) continue;
                        if (!full.TryGetValue(h, out var l)) full[h] = l = new List<FileInfo>();
                        l.Add(f);
                    }
                    result.AddRange(full.Values.Where(v => v.Count > 1).Select(v => new DuplicateGroup { Size = size, Files = v }));
                }
            }
            progress.Report($"Concluído: {Fmt.Num(scanned)} arquivos analisados.");
            return result.OrderByDescending(g => g.Wasted).ToList();
        }, ct);

    // Pastas de sistema e de programas: arquivos repetidos ali são normais e necessários.
    private static readonly string[] ProtectedRoots = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    }.Where(p => !string.IsNullOrEmpty(p)).Select(p => p.TrimEnd('\\') + "\\").ToArray();

    private static bool IsProtected(string path) =>
        ProtectedRoots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)) ||
        path.Contains("\\AppData\\", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("\\$Recycle.Bin\\", StringComparison.OrdinalIgnoreCase);

    private static string? Hash(FileInfo f, bool quickOnly, CancellationToken ct)
    {
        try
        {
            using var s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Chunk, FileOptions.SequentialScan);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf = ArrayPool<byte>.Shared.Rent(Chunk);
            try
            {
                if (quickOnly)
                {
                    // Arquivos até 128 KB são lidos por inteiro aqui (o hash rápido já é o definitivo).
                    int n = s.ReadAtLeast(buf.AsSpan(0, Chunk), Chunk, throwOnEndOfStream: false);
                    sha.AppendData(buf, 0, n);
                    if (s.Length > Chunk * 2) s.Seek(-Chunk, SeekOrigin.End);
                    n = s.ReadAtLeast(buf.AsSpan(0, Chunk), Chunk, throwOnEndOfStream: false);
                    sha.AppendData(buf, 0, n);
                }
                else
                {
                    int n;
                    while ((n = s.Read(buf, 0, Chunk)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        sha.AppendData(buf, 0, n);
                    }
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buf); }
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }
}
