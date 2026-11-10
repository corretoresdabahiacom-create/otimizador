using System.Diagnostics;
using Otimizador.Core;

namespace Otimizador.Features;

internal readonly record struct JunkSize(long Bytes, int Files);
internal readonly record struct CleanOutcome(long Bytes, int Files, int Skipped, string? Note = null);

internal sealed class CleanCategory
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public bool RequiresAdmin { get; init; }
    public bool DefaultOn { get; init; } = true;
    /// <summary>Pode rodar sozinha na manutenção automática (sem risco de apagar algo que o usuário queira).</summary>
    public bool SafeForAuto { get; init; }
    public required Func<CancellationToken, JunkSize> Scan { get; init; }
    public required Func<CancellationToken, CleanOutcome> Clean { get; init; }
    public bool Available => !RequiresAdmin || AppInfo.IsAdmin;
}

/// <summary>
/// Limpeza de arquivos desnecessários. Regras de segurança:
///  • nunca segue junções/links (FileWalker);
///  • temporários só são apagados se tiverem mais de 24 h (não quebra instaladores em andamento);
///  • arquivos em uso são simplesmente pulados;
///  • cache de navegador só é limpo com o navegador fechado.
/// O Prefetch NÃO é limpo: o Windows o usa para abrir programas mais rápido.
/// </summary>
internal static class Cleaner
{
    private static readonly TimeSpan TempMinAge = TimeSpan.FromHours(24);
    private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public static IReadOnlyList<CleanCategory> Categories { get; } = Build();

    private static List<CleanCategory> Build()
    {
        var userTemp = new[] { Path.GetTempPath() };
        var winTemp = new[] { Path.Combine(Win, "Temp") };
        var crash = new[]
        {
            Path.Combine(Local, "CrashDumps"),
            Path.Combine(Local, @"Microsoft\Windows\WER\ReportArchive"),
            Path.Combine(Local, @"Microsoft\Windows\WER\ReportQueue"),
            Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive"),
            Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue"),
            Path.Combine(Win, "Minidump"),
        };
        var wu = new[]
        {
            Path.Combine(Win, @"SoftwareDistribution\Download"),
            Path.Combine(Win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"),
        };
        var inet = new[] { Path.Combine(Local, @"Microsoft\Windows\INetCache") };

        return new List<CleanCategory>
        {
            new()
            {
                Id = "temp_user", Title = "Arquivos temporários do usuário", Glyph = Icons.Document,
                Description = "Pasta %TEMP%. Só remove arquivos com mais de 24 horas.", SafeForAuto = true,
                Scan = ct => ScanDirs(userTemp, ct, TempMinAge), Clean = ct => CleanDirs(userTemp, ct, TempMinAge, removeEmptyDirs: true),
            },
            new()
            {
                Id = "temp_windows", Title = "Arquivos temporários do Windows", Glyph = Icons.Windows, RequiresAdmin = true,
                Description = "Pasta Windows\\Temp. Só remove arquivos com mais de 24 horas.", SafeForAuto = true,
                Scan = ct => ScanDirs(winTemp, ct, TempMinAge), Clean = ct => CleanDirs(winTemp, ct, TempMinAge, removeEmptyDirs: true),
            },
            new()
            {
                Id = "crash", Title = "Relatórios de erro e despejos de memória", Glyph = Icons.Warning, RequiresAdmin = true,
                Description = "Relatórios do Windows (WER), CrashDumps e Minidump.", SafeForAuto = true,
                Scan = ct => ScanDirs(crash, ct), Clean = ct => CleanDirs(crash, ct, removeEmptyDirs: true),
            },
            new()
            {
                Id = "wu", Title = "Cache do Windows Update", Glyph = Icons.Sync, RequiresAdmin = true,
                Description = "Instaladores já baixados e cache de Otimização de Entrega. O Windows baixa de novo se precisar.",
                Scan = ct => ScanDirs(wu, ct), Clean = ct => CleanDirs(wu, ct, removeEmptyDirs: true),
            },
            new()
            {
                Id = "logs", Title = "Logs antigos do Windows", Glyph = Icons.List, RequiresAdmin = true,
                Description = "Arquivos .log e .old com mais de 30 dias em Windows\\Logs.", SafeForAuto = true,
                Scan = ct => ScanOldLogs(ct, false), Clean = ct => ToOutcome(ScanOldLogs(ct, true)),
            },
            new()
            {
                Id = "thumbs", Title = "Cache de miniaturas", Glyph = Icons.Photo, DefaultOn = false,
                Description = "O Explorador recria as miniaturas aos poucos. Arquivos em uso são pulados.",
                Scan = ct => ScanThumbs(ct, false), Clean = ct => ToOutcome(ScanThumbs(ct, true)),
            },
            new()
            {
                Id = "inet", Title = "Cache de internet do Windows", Glyph = Icons.Globe,
                Description = "Cache usado por apps do Windows e componentes antigos do Internet Explorer.",
                Scan = ct => ScanDirs(inet, ct), Clean = ct => CleanDirs(inet, ct),
            },
            new()
            {
                Id = "browsers", Title = "Cache dos navegadores", Glyph = Icons.Globe,
                Description = "Chrome, Edge, Brave, Opera e Firefox. Não apaga histórico, senhas nem cookies. Feche o navegador antes.",
                Scan = ct => ScanBrowsers(ct, false).size, Clean = ct => { var r = ScanBrowsers(ct, true); return ToOutcome(r.size, r.note); },
            },
            new()
            {
                Id = "recycle", Title = "Lixeira", Glyph = Icons.Delete, DefaultOn = false,
                Description = "Esvazia a Lixeira de todas as unidades. Não é possível recuperar depois.",
                Scan = _ => RecycleBinSize(), Clean = _ => EmptyRecycleBin(),
            },
        };
    }

    public static IEnumerable<CleanCategory> ForAuto => Categories.Where(c => c.SafeForAuto && c.Available);

    // ── Pastas genéricas ──────────────────────────────────────────────────
    private static JunkSize ScanDirs(IEnumerable<string> dirs, CancellationToken ct, TimeSpan? minAge = null)
    {
        long bytes = 0; int files = 0;
        var limit = DateTime.UtcNow - (minAge ?? TimeSpan.Zero);
        foreach (var d in dirs)
            foreach (var f in FileWalker.Files(d, ct: ct))
            {
                try
                {
                    if (minAge != null && f.LastWriteTimeUtc > limit) continue;
                    bytes += f.Length; files++;
                }
                catch { }
            }
        return new(bytes, files);
    }

    private static CleanOutcome CleanDirs(IEnumerable<string> dirs, CancellationToken ct, TimeSpan? minAge = null, bool removeEmptyDirs = false)
    {
        long bytes = 0; int files = 0, skipped = 0;
        var limit = DateTime.UtcNow - (minAge ?? TimeSpan.Zero);
        foreach (var d in dirs)
        {
            foreach (var f in FileWalker.Files(d, ct: ct).ToList())
            {
                long len;
                try
                {
                    if (minAge != null && f.LastWriteTimeUtc > limit) continue;
                    len = f.Length;
                }
                catch { continue; }
                if (FileWalker.TryDelete(f)) { bytes += len; files++; } else skipped++;
            }
            if (removeEmptyDirs) FileWalker.RemoveEmptyDirs(d, limit);
        }
        return new(bytes, files, skipped);
    }

    private static CleanOutcome ToOutcome(JunkSize s, string? note = null) => new(s.Bytes, s.Files, 0, note);

    // ── Logs antigos ──────────────────────────────────────────────────────
    private static JunkSize ScanOldLogs(CancellationToken ct, bool delete)
    {
        long bytes = 0; int files = 0;
        var limit = DateTime.UtcNow.AddDays(-30);
        foreach (var f in FileWalker.Files(Path.Combine(Win, "Logs"), ct: ct).ToList())
        {
            try
            {
                var ext = f.Extension.ToLowerInvariant();
                if (ext is not (".log" or ".old") || f.LastWriteTimeUtc > limit) continue;
                long len = f.Length;
                if (!delete || FileWalker.TryDelete(f)) { bytes += len; files++; }
            }
            catch { }
        }
        return new(bytes, files);
    }

    // ── Miniaturas ────────────────────────────────────────────────────────
    private static JunkSize ScanThumbs(CancellationToken ct, bool delete)
    {
        long bytes = 0; int files = 0;
        var dir = Path.Combine(Local, @"Microsoft\Windows\Explorer");
        foreach (var f in FileWalker.Files(dir, recursive: false, pattern: "thumbcache_*.db", ct: ct).ToList())
        {
            try
            {
                long len = f.Length;
                if (!delete || FileWalker.TryDelete(f)) { bytes += len; files++; }
            }
            catch { }
        }
        return new(bytes, files);
    }

    // ── Navegadores ───────────────────────────────────────────────────────
    private sealed record Browser(string Name, string Process, string UserData, bool Firefox = false);

    private static IEnumerable<Browser> Browsers() => new[]
    {
        new Browser("Google Chrome", "chrome", Path.Combine(Local, @"Google\Chrome\User Data")),
        new Browser("Microsoft Edge", "msedge", Path.Combine(Local, @"Microsoft\Edge\User Data")),
        new Browser("Brave", "brave", Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data")),
        new Browser("Opera", "opera", Path.Combine(Roaming, @"Opera Software\Opera Stable")),
        new Browser("Firefox", "firefox", Path.Combine(Local, @"Mozilla\Firefox\Profiles"), Firefox: true),
    };

    private static IEnumerable<string> CacheDirs(Browser b)
    {
        if (!Directory.Exists(b.UserData)) yield break;
        if (b.Firefox)
        {
            foreach (var p in SafeDirs(b.UserData)) yield return Path.Combine(p, "cache2");
            yield break;
        }
        // Chromium: perfis "Default", "Profile 1"... (no Opera a própria pasta é o perfil)
        var profiles = SafeDirs(b.UserData).Where(p =>
        {
            var n = Path.GetFileName(p);
            return n == "Default" || n.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
        }).Append(b.UserData);
        foreach (var p in profiles)
            foreach (var sub in new[] { @"Cache\Cache_Data", "Code Cache", "GPUCache", @"Service Worker\CacheStorage" })
                yield return Path.Combine(p, sub);
    }

    private static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.GetDirectories(path); } catch { return Array.Empty<string>(); }
    }

    private static bool IsRunning(string process)
    {
        try
        {
            var ps = Process.GetProcessesByName(process);
            foreach (var p in ps) p.Dispose();
            return ps.Length > 0;
        }
        catch { return false; }
    }

    private static (JunkSize size, string? note) ScanBrowsers(CancellationToken ct, bool delete)
    {
        long bytes = 0; int files = 0;
        var skipped = new List<string>();
        foreach (var b in Browsers())
        {
            var dirs = CacheDirs(b).Where(Directory.Exists).ToList();
            if (dirs.Count == 0) continue;
            if (delete && IsRunning(b.Process)) { skipped.Add(b.Name); continue; }
            if (delete)
            {
                var r = CleanDirs(dirs, ct);
                bytes += r.Bytes; files += r.Files;
            }
            else
            {
                var r = ScanDirs(dirs, ct);
                bytes += r.Bytes; files += r.Files;
            }
        }
        string? note = skipped.Count > 0 ? $"Feche {string.Join(", ", skipped)} para limpar o cache." : null;
        return (new JunkSize(bytes, files), note);
    }

    // ── Lixeira ───────────────────────────────────────────────────────────
    private static JunkSize RecycleBinSize()
    {
        var info = new Native.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.SHQUERYRBINFO>() };
        return Native.SHQueryRecycleBin(null, ref info) == 0 ? new(info.i64Size, (int)Math.Min(int.MaxValue, info.i64NumItems)) : new(0, 0);
    }

    private static CleanOutcome EmptyRecycleBin()
    {
        var before = RecycleBinSize();
        if (before.Files == 0) return new(0, 0, 0);
        int hr = Native.SHEmptyRecycleBin(IntPtr.Zero, null, Native.SHERB_NOCONFIRMATION | Native.SHERB_NOPROGRESSUI | Native.SHERB_NOSOUND);
        return hr == 0 ? new(before.Bytes, before.Files, 0) : new(0, 0, before.Files, "Não foi possível esvaziar a Lixeira.");
    }
}
