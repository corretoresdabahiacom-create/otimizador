using System.Text.Json;
using System.Text.Json.Serialization;
using Otimizador.Features;

namespace Otimizador.Core;

/// <summary>
/// Configurações persistidas em %LocalAppData%\OtimizadorPRO\config.json.
/// Gravação atômica (arquivo temporário + troca) para não corromper em queda de energia.
/// </summary>
internal sealed class Settings
{
    public string Theme { get; set; } = "system";          // system | light | dark
    public bool CloseToTray { get; set; } = true;
    public bool TrayHintShown { get; set; }

    public bool AutoMaintenance { get; set; }
    public int AutoMaintenanceHours { get; set; } = 6;
    public DateTime? LastAutoMaintenance { get; set; }

    public string? TermsAcceptedVersion { get; set; }
    public DateTime? TermsAcceptedAt { get; set; }

    public string? LicenseKey { get; set; }
    /// <summary>Maior data já vista — dificulta "voltar o relógio" para burlar a validade.</summary>
    public DateTime LicenseMaxSeenUtc { get; set; }

    public List<Schedule> Schedules { get; set; } = new();
    public Dictionary<string, List<RegBackup>> TweakBackups { get; set; } = new();
    public string? PreviousPowerScheme { get; set; }
    public List<string> GamerAppliedTweaks { get; set; } = new();
    public bool GamerActive { get; set; }
    public List<string> GamerCloseApps { get; set; } = new() { "OneDrive", "Teams", "ms-teams", "Skype", "Dropbox", "GoogleDriveFS", "AdobeARM", "jusched" };
    public bool EcoActive { get; set; }

    public long TotalBytesFreed { get; set; }
    public DateTime? LastOptimization { get; set; }
    public long LastJunkEstimate { get; set; } = -1;

    public bool MigratedFromV2 { get; set; }

    // ─────────────────────────────────────────────────────────────────────
    [JsonIgnore] private static readonly string FilePath = Path.Combine(AppInfo.DataDir, "config.json");
    [JsonIgnore] private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
        }
        catch (Exception ex)
        {
            Log.Error("Configuração corrompida, usando padrão", ex);
            try { File.Copy(FilePath, FilePath + ".corrompido", true); } catch { }
            Current = new Settings();
        }
        Current.AutoMaintenanceHours = Math.Clamp(Current.AutoMaintenanceHours, 1, 168);
    }

    public static void Save()
    {
        lock (Gate)
        {
            try
            {
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception ex) { Log.Error("Falha ao salvar configuração", ex); }
        }
    }
}
