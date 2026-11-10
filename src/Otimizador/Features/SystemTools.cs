using Microsoft.Win32;
using Otimizador.Core;

namespace Otimizador.Features;

internal sealed record SecurityReport(bool? Firewall, bool? Defender, bool Uac, bool RebootPending)
{
    public int Score => (Firewall == true ? 35 : 0) + (Defender != false ? 35 : 0) + (Uac ? 20 : 0) + (RebootPending ? 0 : 10);
}

/// <summary>Ferramentas do Windows executadas com saída em tempo real.</summary>
internal static class SystemTools
{
    public static async Task<bool> OptimizeDrivesAsync(Action<string> log, CancellationToken ct)
    {
        bool ok = true;
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            log($"Otimizando {d.Name} (SSD recebe TRIM, HD é desfragmentado)…");
            var r = await Shell.RunAsync(Shell.Sys("defrag.exe"), $"{d.Name.TrimEnd('\\')} /O", 2 * 60 * 60 * 1000, log, ct);
            ok &= r.Ok;
        }
        return ok;
    }

    public static async Task<bool> CheckDiskAsync(Action<string> log, CancellationToken ct)
    {
        var drive = (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").TrimEnd('\\');
        log($"Verificando {drive} sem desmontar a unidade (chkdsk /scan)…");
        var r = await Shell.RunAsync(Shell.Sys("chkdsk.exe"), $"{drive} /scan", 60 * 60 * 1000, log, ct);
        return r.ExitCode is 0 or 1; // 1 = erros corrigidos
    }

    public static async Task<bool> FlushDnsAsync(Action<string>? log = null) =>
        (await Shell.RunAsync(Shell.Sys("ipconfig.exe"), "/flushdns", 20_000, log)).Ok;

    public static async Task<bool> ResetNetworkAsync(Action<string> log)
    {
        bool ok = await FlushDnsAsync(log);
        ok &= (await Shell.RunAsync(Shell.Sys("netsh.exe"), "winsock reset", 30_000, log)).Ok;
        ok &= (await Shell.RunAsync(Shell.Sys("netsh.exe"), "int ip reset", 30_000, log)).Ok;
        return ok;
    }

    public static async Task<bool> CreateRestorePointAsync(Action<string> log)
    {
        const string ps = "Checkpoint-Computer -Description 'Otimizador PC PRO' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop";
        var r = await Shell.RunAsync(
            Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{ps}\"", 5 * 60 * 1000, log);
        return r.Ok;
    }

    /// <summary>SFC e DISM demoram muito: abrem em uma janela própria para o usuário acompanhar.</summary>
    public static bool OpenSystemRepair() =>
        Shell.OpenConsole("\"title Reparo do Windows - Otimizador PC PRO & DISM /Online /Cleanup-Image /RestoreHealth & sfc /scannow & echo. & echo Concluido. Pode fechar esta janela.\"");

    public static async Task<bool> EnableFirewallAsync() =>
        (await Shell.RunAsync(Shell.Sys("netsh.exe"), "advfirewall set allprofiles state on", 20_000)).Ok;

    public static Task<SecurityReport> SecurityAsync() => Task.Run(async () =>
    {
        bool? fw = null;
        try
        {
            using var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            const string basePath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\";
            var states = new[] { "DomainProfile", "StandardProfile", "PublicProfile" }
                .Select(p => { using var k = lm.OpenSubKey(basePath + p); return k?.GetValue("EnableFirewall") as int?; })
                .ToList();
            if (states.All(s => s != null)) fw = states.All(s => s == 1);
        }
        catch { }

        bool? defender = null;
        var sc = await Shell.RunAsync(Shell.Sys("sc.exe"), "query WinDefend", 10_000);
        if (sc.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) defender = true;
        else if (sc.Output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) defender = false;

        bool uac = true;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            uac = k?.GetValue("EnableLUA") is not int v || v != 0;
        }
        catch { }

        bool reboot = false;
        try
        {
            using var k1 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            using var k2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            reboot = k1 != null || k2 != null;
        }
        catch { }

        return new SecurityReport(fw, defender, uac, reboot);
    });
}
