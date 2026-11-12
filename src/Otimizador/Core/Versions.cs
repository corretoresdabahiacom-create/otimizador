using System.Text.Json;

namespace Otimizador.Core;

/// <summary>Comparação de versões e leitura da resposta "última versão" do GitHub (sem rede, para ser testável).</summary>
internal static class Versions
{
    /// <summary>Aceita "v3.1.0", "3.1", "3.1.0-beta" (o sufixo é ignorado).</summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().TrimStart('v', 'V');
        int cut = t.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) t = t[..cut];
        if (!Version.TryParse(t, out var v)) return false;
        version = new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        return true;
    }

    public static bool IsNewer(string? current, string? candidate) =>
        TryParse(current, out var a) && TryParse(candidate, out var b) && b > a;

    /// <summary>Lê "tag_name" e "html_url" da API de releases. Rascunhos e pré-lançamentos são ignorados.</summary>
    public static bool TryReadRelease(string json, out string tag, out string url)
    {
        tag = ""; url = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (Flag(root, "draft") || Flag(root, "prerelease")) return false;
            if (!root.TryGetProperty("tag_name", out var t) || t.ValueKind != JsonValueKind.String) return false;
            tag = t.GetString() ?? "";
            if (!TryParse(tag, out _)) return false;
            url = root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? "" : "";
            // só aceita abrir páginas do próprio GitHub
            if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) url = "";
            return true;
        }
        catch { return false; }
    }

    private static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
