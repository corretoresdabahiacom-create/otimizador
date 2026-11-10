using Microsoft.Win32;
using Otimizador.Core;

namespace Otimizador.Features;

internal sealed class PrivacyItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required Func<int> Clean { get; init; }
    /// <summary>Precisa rodar na thread da interface (área de transferência).</summary>
    public bool UiThread { get; init; }
}

/// <summary>Limpeza de rastros de uso do Windows (a versão anterior apenas simulava esta função).</summary>
internal static class PrivacyCleaner
{
    private const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    private static string Recent => Environment.GetFolderPath(Environment.SpecialFolder.Recent);

    public static IReadOnlyList<PrivacyItem> Items { get; } = new List<PrivacyItem>
    {
        new()
        {
            Id = "recent", Title = "Arquivos recentes",
            Description = "Lista de documentos abertos recentemente (os arquivos em si não são apagados).",
            Clean = () => DeleteFiles(Recent, "*.lnk", false),
        },
        new()
        {
            Id = "jumplists", Title = "Listas de atalhos da barra de tarefas",
            Description = "Itens recentes (e fixados) que aparecem ao clicar com o botão direito nos ícones da barra.",
            Clean = () => DeleteFiles(Path.Combine(Recent, "AutomaticDestinations"), "*", false)
                        + DeleteFiles(Path.Combine(Recent, "CustomDestinations"), "*", false),
        },
        new()
        {
            Id = "run", Title = "Histórico da caixa Executar (Win + R)",
            Description = "Comandos digitados na caixa Executar.",
            Clean = () => ClearValues(Explorer + @"\RunMRU"),
        },
        new()
        {
            Id = "typed", Title = "Endereços digitados no Explorador",
            Description = "Caminhos digitados na barra de endereço do Explorador de Arquivos.",
            Clean = () => ClearValues(Explorer + @"\TypedPaths"),
        },
        new()
        {
            Id = "search", Title = "Pesquisas do Explorador de Arquivos",
            Description = "Termos pesquisados na caixa de busca do Explorador.",
            Clean = () => ClearValues(Explorer + @"\WordWheelQuery"),
        },
        new()
        {
            Id = "clipboard", Title = "Área de transferência",
            Description = "Conteúdo copiado no momento (Ctrl + C).", UiThread = true,
            Clean = () => { try { Clipboard.Clear(); return 1; } catch { return 0; } },
        },
    };

    private static int DeleteFiles(string dir, string pattern, bool recursive)
    {
        int n = 0;
        foreach (var f in FileWalker.Files(dir, recursive, pattern).ToList())
            if (FileWalker.TryDelete(f)) n++;
        return n;
    }

    private static int ClearValues(string path)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(path, true);
            if (k == null) return 0;
            int n = 0;
            foreach (var name in k.GetValueNames())
            {
                try { k.DeleteValue(name, false); n++; } catch { }
            }
            return n;
        }
        catch { return 0; }
    }
}
