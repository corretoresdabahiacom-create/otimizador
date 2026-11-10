namespace Otimizador.UI.Pages;

internal sealed class StartupPage : Page
{
    private readonly ListCard _card = new();
    private readonly UiButton _toggle = new("Desativar", ButtonKind.Primary, Icons.Pause) { Enabled = false };
    private readonly UiButton _open = new("Abrir local", ButtonKind.Secondary, Icons.FolderOpen) { Enabled = false };
    private readonly UiButton _refresh = new("", ButtonKind.Secondary, Icons.Refresh);
    private readonly TextLabel _summary = new("", Theme.Body, Tone.Muted);
    private List<StartupEntry> _entries = new();
    private bool _loaded;

    private ThemedListView List => _card.List;

    public StartupPage() : base("Inicialização", "Programas que abrem junto com o Windows. Desativar não desinstala nada.", fillMode: true)
    {
        Header.AddRight(_refresh, _open, _toggle);
        Ui.Tip(_refresh, "Atualizar lista");

        var info = new Row { FixedHeight = Theme.S(24) };
        info.SetFill(_summary);
        Content.Controls.Add(info);

        List.Columns.Add("Programa", Theme.S(230));
        List.Columns.Add("Status", Theme.S(100));
        List.Columns.Add("Editor", Theme.S(190));
        List.Columns.Add("Origem", Theme.S(170));
        List.Columns.Add("Comando", Theme.S(420));
        List.MultiSelect = true;
        List.EnableSorting();
        List.SelectedIndexChanged += (_, _) => UpdateButtons();
        List.DoubleClick += (_, _) => ToggleSelected();
        Content.Controls.Add(_card);

        _refresh.Click += async (_, _) => await LoadAsync();
        _toggle.Click += (_, _) => ToggleSelected();
        _open.Click += (_, _) =>
        {
            if (Selected().FirstOrDefault() is { } e && File.Exists(e.ExePath))
                Shell.ShowInExplorer(e.ExePath);
        };
    }

    public override async void OnShown()
    {
        if (_loaded) return;
        _loaded = true;
        await LoadAsync();
    }

    private IEnumerable<StartupEntry> Selected() => List.SelectedItems.Cast<ListViewItem>().Select(i => (StartupEntry)i.Tag!);

    private async Task LoadAsync()
    {
        _refresh.Enabled = false;
        _summary.Text = "Carregando…";
        _entries = await StartupManager.LoadAsync();
        List.BeginUpdate();
        List.Items.Clear();
        foreach (var e in _entries)
        {
            var it = new ListViewItem(e.Name) { Tag = e, UseItemStyleForSubItems = false };
            it.SubItems.Add("");
            it.SubItems.Add(e.Publisher);
            it.SubItems.Add(e.SourceText);
            it.SubItems.Add(e.Command);
            PaintItem(it);
            List.Items.Add(it);
        }
        List.EndUpdate();
        _refresh.Enabled = true;
        UpdateSummary();
        UpdateButtons();
    }

    private static void PaintItem(ListViewItem it)
    {
        var e = (StartupEntry)it.Tag!;
        var status = it.SubItems[1];
        status.Text = e.Enabled ? "Ativado" : "Desativado";
        status.ForeColor = e.Enabled ? Theme.P.Success : Theme.P.Muted;
        it.ForeColor = e.Enabled ? Theme.P.Text : Theme.P.Muted;
    }

    private void UpdateSummary()
    {
        int on = _entries.Count(e => e.Enabled);
        _summary.Text = $"{on} de {_entries.Count} programas ativados. Menos programas = Windows pronto mais rápido.";
    }

    private void UpdateButtons()
    {
        var sel = Selected().ToList();
        _toggle.Enabled = sel.Count > 0;
        _open.Enabled = sel.Count == 1 && File.Exists(sel[0].ExePath);
        bool enable = sel.Count > 0 && sel.All(e => !e.Enabled);
        _toggle.Text = enable ? "Ativar" : "Desativar";
        _toggle.Glyph = enable ? Icons.Play : Icons.Pause;
    }

    private void ToggleSelected()
    {
        var items = List.SelectedItems.Cast<ListViewItem>().ToList();
        if (items.Count == 0) return;
        bool enable = items.All(i => !((StartupEntry)i.Tag!).Enabled);
        int ok = 0, denied = 0;
        foreach (var it in items)
        {
            var e = (StartupEntry)it.Tag!;
            if (StartupManager.SetEnabled(e, enable)) { ok++; PaintItem(it); } else denied++;
        }
        List.Invalidate();
        UpdateSummary();
        UpdateButtons();
        if (denied > 0) MainForm.Instance?.ShowToast($"{denied} item(ns) exigem administrador.", Tone.Warning);
        else MainForm.Instance?.ShowToast($"{ok} item(ns) {(enable ? "ativado(s)" : "desativado(s)")}. Vale na próxima inicialização.");
    }
}
