using System.Globalization;
using System.Reflection;
using System.Security.Principal;

namespace Otimizador.Core;

internal static class AppInfo
{
    public const string Name = "Otimizador PC PRO";
    public const string SupportEmail = "admmeuarmazem@gmail.com";
    /// <summary>Versão dos Termos. Altere quando o texto mudar para pedir novo aceite.</summary>
    public const string TermsVersion = "3.0";

    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "3.1.0";

    public static string DataDir { get; } = CreateDir(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OtimizadorPRO"));

    public static string LogDir { get; } = CreateDir(Path.Combine(DataDir, "logs"));

    public static string ExePath { get; } = Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsAdmin { get; } = CheckAdmin();

    private static bool CheckAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string CreateDir(string path)
    {
        try { Directory.CreateDirectory(path); } catch { /* sem permissão: usado só para logs/config */ }
        return path;
    }

    public static string ReadResource(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (s == null) return "";
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}

internal static class Fmt
{
    public static string Bytes(long bytes)
    {
        var c = AppInfo.PtBr;
        if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.0", c) + " GB";
        if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0.0", c) + " MB";
        if (bytes >= 1L << 10) return (bytes / (double)(1L << 10)).ToString("0", c) + " KB";
        return bytes.ToString(c) + " B";
    }

    public static string Num(long n) => n.ToString("N0", AppInfo.PtBr);

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" :
        t.TotalHours >= 1 ? $"{t.Hours}h {t.Minutes:00}min" : $"{t.Minutes}min";

    public static string When(DateTime? d)
    {
        if (d is null) return "nunca";
        var local = d.Value.Kind == DateTimeKind.Utc ? d.Value.ToLocalTime() : d.Value;
        var today = DateTime.Today;
        if (local.Date == today) return "hoje às " + local.ToString("HH:mm", AppInfo.PtBr);
        if (local.Date == today.AddDays(-1)) return "ontem às " + local.ToString("HH:mm", AppInfo.PtBr);
        if (local.Date == today.AddDays(1)) return "amanhã às " + local.ToString("HH:mm", AppInfo.PtBr);
        return local.ToString("dd/MM/yyyy HH:mm", AppInfo.PtBr);
    }
}

/// <summary>Log simples em arquivo, com rotação (máx. ~1 MB). Nunca grava segredos.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(AppInfo.LogDir, "otimizador.log");

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("AVISO", msg);
    public static void Error(string msg, Exception? ex = null) =>
        Write("ERRO", ex == null ? msg : $"{msg}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 1_000_000)
                {
                    var old = FilePath + ".1";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(FilePath, old);
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* log nunca derruba o app */ }
    }
}
