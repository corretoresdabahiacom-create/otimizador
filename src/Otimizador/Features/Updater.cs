using System.Net.Http.Headers;

namespace Otimizador.Features;

internal sealed record UpdateInfo(string Version, string Url);

/// <summary>
/// Verifica se há versão nova no GitHub. Só consulta a última versão publicada (nenhum dado do usuário é enviado)
/// e nunca baixa nem executa nada sozinho: apenas avisa e abre a página de download.
/// </summary>
internal static class Updater
{
    public const string Repo = "corretoresdabahiacom-create/otimizador";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OtimizadorPCPro", AppInfo.Version));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    });

    /// <summary>Consulta agora. Devolve null se não houver versão nova ou se a consulta falhar (sem internet etc.).</summary>
    public static async Task<(UpdateInfo? Update, bool Reached)> CheckAsync()
    {
        try
        {
            using var resp = await Http.Value.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            var s = Settings.Current;
            s.LastUpdateCheck = DateTime.UtcNow;
            // 404 = ainda não há nenhuma versão publicada: a consulta funcionou, só não há o que oferecer
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) { Settings.Save(); return (null, true); }
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            Settings.Save();
            if (!Versions.TryReadRelease(json, out var tag, out var url)) return (null, true);
            if (!Versions.IsNewer(AppInfo.Version, tag)) return (null, true);
            return (new UpdateInfo(tag.TrimStart('v', 'V'), url.Length > 0 ? url : ReleasesPage), true);
        }
        catch (Exception ex)
        {
            Log.Warn("Verificação de atualização falhou: " + ex.Message);
            return (null, false);
        }
    }

    /// <summary>Verificação automática: no máximo 1 vez a cada 24 h e só se o usuário não desligou.</summary>
    public static async Task<UpdateInfo?> CheckIfDueAsync()
    {
        var s = Settings.Current;
        if (!s.CheckUpdates) return null;
        if (s.LastUpdateCheck is { } last && DateTime.UtcNow - last < Interval) return null;
        return (await CheckAsync()).Update;
    }
}
