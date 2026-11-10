using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using Otimizador.Core;

namespace Otimizador.Features;

internal sealed class InstalledProgram
{
    public required string Name { get; init; }
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public long SizeBytes { get; init; }
    public DateTime? InstallDate { get; init; }
    public string UninstallString { get; init; } = "";
    public string QuietUninstallString { get; init; } = "";
}

/// <summary>Lista e desinstala programas usando o desinstalador oficial de cada um.</summary>
internal static class ProgramsManager
{
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static Task<List<InstalledProgram>> LoadAsync() => Task.Run(() =>
    {
        var map = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        Read(map, RegistryHive.LocalMachine, RegistryView.Registry64);
        Read(map, RegistryHive.LocalMachine, RegistryView.Registry32);
        Read(map, RegistryHive.CurrentUser, RegistryView.Registry64);
        return map.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    });

    private static void Read(Dictionary<string, InstalledProgram> map, RegistryHive hive, RegistryView view)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(UninstallPath);
            if (key == null) return;
            foreach (var sub in key.GetSubKeyNames())
            {
                try
                {
                    using var k = key.OpenSubKey(sub);
                    if (k == null) continue;
                    var name = (k.GetValue("DisplayName") as string)?.Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    if (k.GetValue("ParentKeyName") != null) continue; // atualizações de outro produto
                    var rel = k.GetValue("ReleaseType") as string ?? "";
                    if (rel.Contains("Update", StringComparison.OrdinalIgnoreCase) || rel.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)) continue;
                    var uninst = k.GetValue("UninstallString") as string ?? "";
                    if (string.IsNullOrWhiteSpace(uninst)) continue;

                    var version = k.GetValue("DisplayVersion") as string ?? "";
                    var p = new InstalledProgram
                    {
                        Name = name,
                        Publisher = (k.GetValue("Publisher") as string ?? "").Trim(),
                        Version = version,
                        SizeBytes = k.GetValue("EstimatedSize") is int kb ? kb * 1024L : 0,
                        InstallDate = ParseDate(k.GetValue("InstallDate") as string),
                        UninstallString = uninst,
                        QuietUninstallString = k.GetValue("QuietUninstallString") as string ?? "",
                    };
                    map.TryAdd(name + "|" + version, p);
                }
                catch { }
            }
        }
        catch (Exception ex) { Log.Warn($"Programas ({hive}/{view}): {ex.Message}"); }
    }

    private static DateTime? ParseDate(string? s) =>
        s != null && DateTime.TryParseExact(s.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Inicia o desinstalador do programa e aguarda terminar.</summary>
    public static async Task<bool> UninstallAsync(InstalledProgram p)
    {
        var (file, args) = SplitCommand(p.UninstallString);
        if (string.IsNullOrEmpty(file)) return false;
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
            if (proc != null) await proc.WaitForExitAsync();
            Log.Info($"Desinstalador executado: {p.Name}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Desinstalar {p.Name}", ex);
            return false;
        }
    }

    /// <summary>Separa executável e argumentos, tratando caminhos sem aspas e o MsiExec (/I → /X).</summary>
    public static (string file, string args) SplitCommand(string cmd)
    {
        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (cmd.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            int space = cmd.IndexOf(' ');
            var rest = space > 0 ? cmd[space..].Trim() : "";
            rest = System.Text.RegularExpressions.Regex.Replace(rest, @"(?i)/I\s*(\{)", "/X$1");
            return (Shell.Sys("msiexec.exe"), rest);
        }
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            if (end > 1) return (cmd[1..end], cmd[(end + 1)..].Trim());
        }
        // Caminho sem aspas e com espaços: procura o maior prefixo que existe como arquivo.
        int pos = 0;
        while ((pos = cmd.IndexOf(' ', pos + 1)) > 0)
        {
            var candidate = cmd[..pos];
            if (File.Exists(candidate)) return (candidate, cmd[pos..].Trim());
        }
        if (File.Exists(cmd)) return (cmd, "");
        var exe = StartupManager.ExtractExe(cmd);
        return (exe, cmd.Length > exe.Length ? cmd[exe.Length..].Trim() : "");
    }
}
