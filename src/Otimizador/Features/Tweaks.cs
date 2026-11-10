using System.Text.RegularExpressions;
using Microsoft.Win32;
using Otimizador.Core;

namespace Otimizador.Features;

internal enum Hive { User, Machine }

/// <summary>Um valor de registro que o ajuste grava. <see cref="Default"/> null = apagar ao reverter sem backup.</summary>
internal sealed record RegValue(Hive Hive, string Path, string Name, object Value, object? Default = null)
{
    public RegistryValueKind Kind => Value is string ? RegistryValueKind.String : RegistryValueKind.DWord;
}

/// <summary>Valor original guardado antes de aplicar um ajuste (para "Desfazer" exato).</summary>
internal sealed class RegBackup
{
    public Hive Hive { get; set; }
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Existed { get; set; }
    public bool IsString { get; set; }
    public string? StringValue { get; set; }
    public int IntValue { get; set; }
}

internal sealed class Tweak
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public required string Group { get; init; } // "desempenho" | "privacidade"
    public bool RequiresAdmin { get; init; }
    public string? RestartNote { get; init; }
    public RegValue[] Values { get; init; } = Array.Empty<RegValue>();

    // Ajustes que não são de registro (planos de energia)
    public Func<Task<bool>>? IsAppliedCustom { get; init; }
    public Func<Task<bool>>? ApplyCustom { get; init; }
    public Func<Task<bool>>? RevertCustom { get; init; }

    public bool Available => !RequiresAdmin || AppInfo.IsAdmin;
}

/// <summary>
/// Motor de ajustes reversíveis. Cada ajuste guarda os valores originais antes de mudar,
/// e "Desfazer" restaura exatamente o que havia (ou o padrão do Windows).
/// Substitui o antigo "Desfazer"/"Backup" que eram apenas simulados.
/// </summary>
internal static class Tweaks
{
    public static IReadOnlyList<Tweak> All { get; } = Catalog.Build();

    public static Tweak Get(string id) => All.First(t => t.Id == id);

    public static IEnumerable<Tweak> InGroup(string group) => All.Where(t => t.Group == group);

    public static async Task<bool> IsAppliedAsync(Tweak t)
    {
        if (t.IsAppliedCustom != null) return await t.IsAppliedCustom();
        return await Task.Run(() => t.Values.All(v => Equals(Read(v.Hive, v.Path, v.Name), v.Value)));
    }

    public static async Task<bool> ApplyAsync(Tweak t)
    {
        if (!t.Available) return false;
        try
        {
            if (t.ApplyCustom != null) return await t.ApplyCustom();
            await Task.Run(() =>
            {
                var store = Settings.Current.TweakBackups;
                if (!store.ContainsKey(t.Id))
                    store[t.Id] = t.Values.Select(Backup).ToList();
                foreach (var v in t.Values) Write(v.Hive, v.Path, v.Name, v.Value);
            });
            Settings.Save();
            Log.Info($"Ajuste aplicado: {t.Id}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Ajuste {t.Id}", ex);
            return false;
        }
    }

    public static async Task<bool> RevertAsync(Tweak t)
    {
        if (!t.Available) return false;
        try
        {
            if (t.RevertCustom != null) return await t.RevertCustom();
            await Task.Run(() =>
            {
                var store = Settings.Current.TweakBackups;
                if (store.TryGetValue(t.Id, out var backups))
                {
                    foreach (var b in backups)
                    {
                        if (!b.Existed) Delete(b.Hive, b.Path, b.Name);
                        else Write(b.Hive, b.Path, b.Name, b.IsString ? b.StringValue ?? "" : b.IntValue);
                    }
                    store.Remove(t.Id);
                }
                else
                {
                    foreach (var v in t.Values)
                    {
                        if (v.Default == null) Delete(v.Hive, v.Path, v.Name);
                        else Write(v.Hive, v.Path, v.Name, v.Default);
                    }
                }
            });
            Settings.Save();
            Log.Info($"Ajuste desfeito: {t.Id}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Desfazer {t.Id}", ex);
            return false;
        }
    }

    /// <summary>Desfaz todos os ajustes que este app aplicou.</summary>
    public static async Task<int> RevertAllAsync()
    {
        int n = 0;
        foreach (var t in All)
            if (t.Available && await IsAppliedAsync(t) && await RevertAsync(t)) n++;
        Settings.Current.GamerActive = false;
        Settings.Current.EcoActive = false;
        Settings.Current.GamerAppliedTweaks.Clear();
        Settings.Save();
        return n;
    }

    // ── Registro ──────────────────────────────────────────────────────────
    private static RegistryKey Root(Hive h) =>
        RegistryKey.OpenBaseKey(h == Hive.User ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);

    private static object? Read(Hive h, string path, string name)
    {
        try
        {
            using var root = Root(h);
            using var k = root.OpenSubKey(path);
            return k?.GetValue(name);
        }
        catch { return null; }
    }

    private static void Write(Hive h, string path, string name, object value)
    {
        using var root = Root(h);
        using var k = root.CreateSubKey(path, true);
        k.SetValue(name, value, value is string ? RegistryValueKind.String : RegistryValueKind.DWord);
    }

    private static void Delete(Hive h, string path, string name)
    {
        using var root = Root(h);
        using var k = root.OpenSubKey(path, true);
        k?.DeleteValue(name, false);
    }

    private static RegBackup Backup(RegValue v)
    {
        var cur = Read(v.Hive, v.Path, v.Name);
        return new RegBackup
        {
            Hive = v.Hive, Path = v.Path, Name = v.Name, Existed = cur != null,
            IsString = cur is string, StringValue = cur as string, IntValue = cur is int i ? i : 0,
        };
    }
}

/// <summary>Planos de energia via powercfg.</summary>
internal static class PowerPlans
{
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private static readonly Regex GuidRx = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    private static string Powercfg => Shell.Sys("powercfg.exe");

    public static async Task<string?> ActiveAsync()
    {
        var r = await Shell.RunAsync(Powercfg, "/getactivescheme", 10_000);
        var m = GuidRx.Match(r.Output);
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    public static async Task<bool> IsHighPerformanceAsync()
    {
        var a = await ActiveAsync();
        if (a == null) return false;
        if (a is HighPerformance or Ultimate) return true;
        // Plano duplicado (notebooks com "Modern Standby" não têm o plano original)
        var r = await Shell.RunAsync(Powercfg, "/getactivescheme", 10_000);
        return r.Output.Contains("desempenho", StringComparison.OrdinalIgnoreCase) && r.Output.Contains("alto", StringComparison.OrdinalIgnoreCase)
            || r.Output.Contains("High performance", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<bool> SetHighPerformanceAsync()
    {
        var current = await ActiveAsync();
        if (current != null && current is not (HighPerformance or Ultimate))
        {
            Settings.Current.PreviousPowerScheme = current;
            Settings.Save();
        }
        if ((await Shell.RunAsync(Powercfg, "/setactive " + HighPerformance, 10_000)).Ok) return true;
        // Não existe neste PC: cria uma cópia do modelo e ativa.
        var dup = await Shell.RunAsync(Powercfg, "-duplicatescheme " + HighPerformance, 10_000);
        var m = GuidRx.Match(dup.Output);
        return m.Success && (await Shell.RunAsync(Powercfg, "/setactive " + m.Value, 10_000)).Ok;
    }

    public static async Task<bool> RestorePreviousAsync()
    {
        var prev = Settings.Current.PreviousPowerScheme ?? Balanced;
        var ok = (await Shell.RunAsync(Powercfg, "/setactive " + prev, 10_000)).Ok
                 || (await Shell.RunAsync(Powercfg, "/setactive " + Balanced, 10_000)).Ok;
        if (ok) { Settings.Current.PreviousPowerScheme = null; Settings.Save(); }
        return ok;
    }

    /// <summary>Limite máximo do processador na BATERIA (99% desliga o Turbo = menos calor e mais autonomia).</summary>
    public static async Task<bool> SetBatteryCpuMaxAsync(int percent)
    {
        var a = await Shell.RunAsync(Powercfg, $"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {percent}", 10_000);
        await Shell.RunAsync(Powercfg, "/setactive SCHEME_CURRENT", 10_000);
        return a.Ok;
    }

    public static async Task<int?> GetBatteryCpuMaxAsync()
    {
        var r = await Shell.RunAsync(Powercfg, "/query SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX", 10_000);
        var hex = Regex.Matches(r.Output, @"0x([0-9a-fA-F]{8})");
        if (hex.Count == 0) return null;
        return Convert.ToInt32(hex[^1].Groups[1].Value, 16); // último valor = índice CC (bateria)
    }
}
