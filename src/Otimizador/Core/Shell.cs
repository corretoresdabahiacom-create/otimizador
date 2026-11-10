using System.Diagnostics;
using System.Text;

namespace Otimizador.Core;

internal sealed record ShellResult(int ExitCode, string Output, bool TimedOut)
{
    public bool Ok => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// Execução de programas do sistema sem janela e sem "cmd /c" (evita injeção de comandos).
/// Sempre usa o caminho completo em System32 para não executar um binário falso do PATH.
/// </summary>
internal static class Shell
{
    private static readonly Encoding Oem = GetOemEncoding();

    private static Encoding GetOemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding((int)Native.GetOEMCP());
        }
        catch { return Encoding.UTF8; }
    }

    public static string Sys(string exe) => Path.Combine(Environment.SystemDirectory, exe);

    public static async Task<ShellResult> RunAsync(string file, string args, int timeoutMs = 120_000,
        Action<string>? onLine = null, CancellationToken ct = default, bool unicodeOutput = false)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = unicodeOutput ? Encoding.UTF8 : Oem,
            StandardErrorEncoding = unicodeOutput ? Encoding.UTF8 : Oem,
            WorkingDirectory = Environment.SystemDirectory,
        };

        var sb = new StringBuilder();
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        void Handle(string? line)
        {
            if (line == null) return;
            lock (sb) sb.AppendLine(line);
            var t = line.Trim();
            if (t.Length > 0) onLine?.Invoke(t);
        }
        p.OutputDataReceived += (_, e) => Handle(e.Data);
        p.ErrorDataReceived += (_, e) => Handle(e.Data);

        try
        {
            p.Start();
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao iniciar {Path.GetFileName(file)}", ex);
            return new ShellResult(-1, ex.Message, false);
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);
        try
        {
            await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            p.WaitForExit(); // garante que a saída redirecionada terminou de chegar
            return new ShellResult(p.ExitCode, sb.ToString(), false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            return new ShellResult(-1, sb.ToString(), true);
        }
    }

    /// <summary>Abre um link, pasta ou página de Configurações do Windows.</summary>
    public static bool Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível abrir '{target}': {ex.Message}");
            return false;
        }
    }

    /// <summary>Abre o Explorador com o arquivo selecionado.</summary>
    public static void ShowInExplorer(string path)
    {
        try
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo(explorer, $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn("Explorador: " + ex.Message); }
    }

    /// <summary>Abre um console visível (para ferramentas longas como SFC/DISM).</summary>
    public static bool OpenConsole(string commandLine)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Sys("cmd.exe"), "/k " + commandLine)
            {
                UseShellExecute = true,
                Verb = AppInfo.IsAdmin ? "" : "runas",
            });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Console não aberto: {ex.Message}");
            return false;
        }
    }

    public static void RestartAsAdmin(string args = "")
    {
        SingleInstance.Release(); // libera para a nova instância elevada
        try
        {
            Process.Start(new ProcessStartInfo(AppInfo.ExePath, args) { UseShellExecute = true, Verb = "runas" });
            Application.Exit();
        }
        catch
        {
            SingleInstance.TryAcquire(); // usuário recusou o UAC: continua como está
        }
    }
}
