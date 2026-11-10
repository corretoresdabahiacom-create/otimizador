namespace Otimizador.UI.Pages;

internal sealed class ProgramsPage : Page
{
    private readonly ListCard _card = new();
    private readonly TextField _search = new("Pesquisar por nome ou editor", Icons.Search, 320);
    private readonly UiButton _uninstall = new("Desinstalar", ButtonKind.Danger, Icons.Delete) { Enabled = false };
    private readonly UiButton _refresh = new("", ButtonKind.Secondary, Icons.Refresh);
    private readonly TextLabel _summary = new("", Theme.Body, Tone.Muted);
    private List<InstalledProgram> _all = new();
    private bool _loaded;

    private ThemedListView List => _card.List;

    public ProgramsPage() : base("Programas", "Desinstale o que você não usa mais. Usa o desinstalador oficial de cada programa.", fillMode: true)
    {
        Header.AddRight(_refresh, _uninstall);
        Ui.Tip(_refresh, "Atualizar lista");

        var bar = new Row { FixedHeight = Theme.S(36) };
        bar.Controls.Add(_search);
        bar.SetFill(_summary);
        _summary.TextAlign = ContentAlignment.MiddleRight;
        Content.Controls.Add(bar);

        List.Columns.Add("Programa", Theme.S(320));
        List.Columns.Add("Editor", Theme.S(220));
        List.Columns.Add("Versão", Theme.S(120));
        List.Columns.Add("Tamanho", Theme.S(100));
        List.Columns.Add("Instalado em", Theme.S(110));
        List.MultiSelect = false;
        List.EnableSorting();
        List.SelectedIndexChanged += (_, _) => _uninstall.Enabled = List.SelectedItems.Count == 1;
        Content.Controls.Add(_card);

        _search.Box.TextChanged += (_, _) => Fill();
        _refresh.Click += async (_, _) => await LoadAsync();
        _uninstall.Click += async (_, _) => await UninstallAsync();
    }

    public override async void OnShown()
    {
        if (_loaded) return;
        _loaded = true;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _refresh.Enabled = false;
        _summary.Text = "Carregando…";
        _all = await ProgramsManager.LoadAsync();
        _refresh.Enabled = true;
        Fill();
    }

    private void Fill()
    {
        var q = _search.Text.Trim();
        var shown = _all.Where(p => q.Length == 0 || p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                                    p.Publisher.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        List.BeginUpdate();
        List.Items.Clear();
        foreach (var p in shown)
        {
            var it = new ListViewItem(p.Name) { Tag = p };
            it.SubItems.Add(p.Publisher);
            it.SubItems.Add(p.Version);
            it.SubItems.Add(new ListViewItem.ListViewSubItem(it, p.SizeBytes > 0 ? Fmt.Bytes(p.SizeBytes) : "—") { Tag = p.SizeBytes });
            it.SubItems.Add(new ListViewItem.ListViewSubItem(it, p.InstallDate?.ToString("dd/MM/yyyy") ?? "—") { Tag = p.InstallDate ?? DateTime.MinValue });
            List.Items.Add(it);
        }
        List.EndUpdate();
        long total = shown.Sum(p => p.SizeBytes);
        _summary.Text = $"{shown.Count} programa(s) · {Fmt.Bytes(total)} informados pelos instaladores";
        _uninstall.Enabled = false;
    }

    private async Task UninstallAsync()
    {
        if (List.SelectedItems.Count != 1 || List.SelectedItems[0].Tag is not InstalledProgram p) return;
        if (MessageBox.Show(FindForm(), $"Desinstalar \"{p.Name}\"?\n\nO desinstalador do próprio programa será aberto.",
                "Desinstalar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _uninstall.Enabled = false;
        _uninstall.Text = "Aguardando o desinstalador…";
        bool ok = await ProgramsManager.UninstallAsync(p);
        _uninstall.Text = "Desinstalar";
        if (!ok)
        {
            if (MessageBox.Show(FindForm(), "Não foi possível iniciar o desinstalador deste programa.\nAbrir \"Aplicativos instalados\" do Windows?",
                    "Desinstalar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                Shell.Open("ms-settings:appsfeatures");
            return;
        }
        await LoadAsync();
        bool gone = !_all.Any(x => x.Name == p.Name && x.Version == p.Version);
        MainForm.Instance?.ShowToast(gone ? $"\"{p.Name}\" foi desinstalado." : "Desinstalador fechado. O programa ainda aparece na lista.", gone ? Tone.Success : Tone.Warning);
    }
}
