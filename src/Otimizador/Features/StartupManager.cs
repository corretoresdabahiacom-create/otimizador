using System.Diagnostics;
using Microsoft.Win32;
using Otimizador.Core;

namespace Otimizador.Features;

internal enum StartupSource { UserRun, MachineRun, MachineRun32, UserFolder, CommonFolder }

internal sealed class StartupEntry
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required StartupSource Source { get; init; }
    public bool Enabled { get; set; }
    public string Publisher { get; init; } = "";
    public string ExePath { get; init; } = "";

    public string SourceText => Source switch
    {
        StartupSource.UserRun => "Registro (usuário)",
        StartupSource.MachineRun => "Registro (todos)",
        StartupSource.MachineRun32 => "Registro 32 bits (todos)",
        StartupSource.UserFolder => "Pasta Inicializar (usuário)",
        _ => "Pasta Inicializar (todos)",
    };

    public bool NeedsAdmin => Source is StartupSource.MachineRun or StartupSource.MachineRun32 or StartupSource.CommonFolder;
}

/// <summary>
/// Gerenciador de inicialização compatível com o Gerenciador de Tarefas do Windows:
/// ativa/desativa pela chave "StartupApproved" (totalmente reversível, nada é apagado).
/// </summary>
internal static class StartupManager
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static Task<List<StartupEntry>> LoadAsync() => Task.Run(() =>
    {
        var list = new List<StartupEntry>();
        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var hklm32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);

        ReadRun(list, hkcu, StartupSource.UserRun, hkcu, "Run");
        ReadRun(list, hklm, StartupSource.MachineRun, hklm, "Run");
        ReadRun(list, hklm32, StartupSource.MachineRun32, hklm, "Run32");
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.UserFolder, hkcu);
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.CommonFolder, hklm);
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    });

    private static void ReadRun(List<StartupEntry> list, RegistryKey root, StartupSource src, RegistryKey approvedRoot, string approvedSub)
    {
        try
        {
            using var key = root.OpenSubKey(RunPath);
            if (key == null) return;
            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(name)) continue;
                var cmd = key.GetValue(name)?.ToString() ?? "";
                var exe = ExtractExe(cmd);
                list.Add(new StartupEntry
                {
                    Name = name, Command = cmd, Source = src, ExePath = exe, Publisher = Describe(exe),
                    Enabled = IsApproved(approvedRoot, approvedSub, name),
                });
            }
        }
        catch (Exception ex) { Log.Warn($"Leitura de {src}: {ex.Message}"); }
    }

    private static void ReadFolder(List<StartupEntry> list, string folder, StartupSource src, RegistryKey approvedRoot)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var f in Directory.GetFiles(folder))
            {
                var file = Path.GetFileName(f);
                if (file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(f), Command = f, Source = src, ExePath = f, Publisher = Describe(f),
                    Enabled = IsApproved(approvedRoot, "StartupFolder", file),
                });
            }
        }
        catch (Exception ex) { Log.Warn($"Leitura de {src}: {ex.Message}"); }
    }

    private static bool IsApproved(RegistryKey root, string sub, string valueName)
    {
        try
        {
            using var k = root.OpenSubKey(ApprovedBase + sub);
            if (k?.GetValue(valueName) is byte[] b && b.Length > 0) return (b[0] & 1) == 0; // ímpar = desativado
        }
        catch { }
        return true;
    }

    public static bool SetEnabled(StartupEntry e, bool enable)
    {
        if (e.NeedsAdmin && !AppInfo.IsAdmin) return false;
        try
        {
            using var root = RegistryKey.OpenBaseKey(e.NeedsAdmin ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
            string sub = e.Source switch
            {
                StartupSource.MachineRun32 => "Run32",
                StartupSource.UserFolder or StartupSource.CommonFolder => "StartupFolder",
                _ => "Run",
            };
            string valueName = sub == "StartupFolder" ? Path.GetFileName(e.Command) : e.Name;
            using var k = root.CreateSubKey(ApprovedBase + sub, true);
            var data = new byte[12];
            data[0] = enable ? (byte)2 : (byte)3;
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            k.SetValue(valueName, data, RegistryValueKind.Binary);
            e.Enabled = enable;
            Log.Info($"Inicialização: '{e.Name}' {(enable ? "ativado" : "desativado")}.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Inicialização: falha ao alterar '{e.Name}'", ex);
            return false;
        }
    }

    /// <summary>Extrai o executável de uma linha de comando ("C:\x y\a.exe" -arg, C:\x\a.exe /b, rundll32 ...).</summary>
    public static string ExtractExe(string cmd)
    {
        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            return end > 1 ? cmd[1..end] : cmd.Trim('"');
        }
        int idx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (idx > 0) return cmd[..(idx + 4)];
        int sp = cmd.IndexOf(' ');
        return sp > 0 ? cmd[..sp] : cmd;
    }

    private static string Describe(string path)
    {
        try
        {
            if (!File.Exists(path)) return "";
            var vi = FileVersionInfo.GetVersionInfo(path);
            return !string.IsNullOrWhiteSpace(vi.CompanyName) ? vi.CompanyName.Trim() : (vi.FileDescription ?? "").Trim();
        }
        catch { return ""; }
    }
}
