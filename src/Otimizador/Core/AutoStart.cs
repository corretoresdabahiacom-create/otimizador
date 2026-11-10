using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Otimizador.Core;

/// <summary>
/// Inicialização com o Windows via Agendador de Tarefas.
/// (O app exige administrador; o Windows ignora programas elevados na chave "Run",
/// por isso a versão antiga nunca iniciava de verdade com o sistema.)
/// </summary>
internal static class AutoStart
{
    private const string TaskName = "OtimizadorPCPro";

    public static async Task<bool> IsEnabledAsync()
    {
        var r = await Shell.RunAsync(Shell.Sys("schtasks.exe"), $"/Query /TN \"{TaskName}\"", 15_000);
        return r.Ok;
    }

    public static async Task<bool> SetAsync(bool enable)
    {
        if (!enable)
        {
            var del = await Shell.RunAsync(Shell.Sys("schtasks.exe"), $"/Delete /TN \"{TaskName}\" /F", 15_000);
            return del.Ok || !await IsEnabledAsync();
        }

        string user = WindowsIdentity.GetCurrent().Name;
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Inicia o Otimizador PC PRO na bandeja ao entrar no Windows.</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId><Delay>PT20S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author"><UserId>{SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
                <AllowStartOnDemand>true</AllowStartOnDemand>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(AppInfo.ExePath)}</Command><Arguments>--tray</Arguments></Exec>
              </Actions>
            </Task>
            """;

        var file = Path.Combine(Path.GetTempPath(), $"otimizador_task_{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            var r = await Shell.RunAsync(Shell.Sys("schtasks.exe"), $"/Create /TN \"{TaskName}\" /XML \"{file}\" /F", 20_000);
            if (!r.Ok) Log.Warn("schtasks /Create falhou: " + r.Output.Trim());
            return r.Ok;
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }
}

/// <summary>
/// Remove sobras perigosas/inúteis da versão anterior (executa uma única vez).
/// </summary>
internal static class Migration
{
    public static void Run()
    {
        var s = Settings.Current;
        if (s.MigratedFromV2) return;
        try
        {
            // Entradas antigas de inicialização (não funcionavam com app elevado) e o "boot atrasado" em VBS.
            using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                foreach (var name in new[] { "OtimizadorPro", "OTIMIZADOR PC PRO", "MENU_BootManager" })
                    if (run?.GetValue(name) != null) { run.DeleteValue(name, false); Log.Info($"Migração: removida entrada Run '{name}'."); }
            }
            TryDeleteFile(Path.Combine(AppContext.BaseDirectory, "MENU_delay.vbs"));

            // Chaves antigas que guardavam "LicenseValid=true" em texto puro.
            Registry.CurrentUser.DeleteSubKeyTree(@"SOFTWARE\OtimizadorPC", false);
            Registry.CurrentUser.DeleteSubKeyTree(@"SOFTWARE\OtimizadorPro", false);

            if (AppInfo.IsAdmin)
            {
                // A versão 2 gravava LargeSystemCache=1 a cada 10 minutos (prejudica PCs de mesa).
                using var mm = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true);
                if (mm?.GetValue("LargeSystemCache") is int lsc && lsc == 1)
                {
                    mm.SetValue("LargeSystemCache", 0, RegistryValueKind.DWord);
                    Log.Info("Migração: LargeSystemCache restaurado para 0 (padrão do Windows).");
                }
                if (mm?.GetValue("DisablePagingExecutive") is int dpe && dpe == 1)
                {
                    mm.SetValue("DisablePagingExecutive", 0, RegistryValueKind.DWord);
                    Log.Info("Migração: DisablePagingExecutive restaurado para 0.");
                }

                // Regras de firewall "BlockPortXXX" criadas repetidamente pela versão 2
                // (bloqueavam compartilhamento de arquivos e Área de Trabalho Remota).
                _ = Task.Run(async () =>
                {
                    foreach (var port in new[] { 135, 137, 138, 139, 445, 3389 })
                        await Shell.RunAsync(Shell.Sys("netsh.exe"), $"advfirewall firewall delete rule name=\"BlockPort{port}\"", 20_000);
                    Log.Info("Migração: regras antigas BlockPort* removidas do firewall.");
                });
            }
        }
        catch (Exception ex) { Log.Error("Migração", ex); }

        s.MigratedFromV2 = true;
        Settings.Save();
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); } } catch { }
    }
}
