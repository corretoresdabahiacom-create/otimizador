namespace Otimizador.UI.Pages;

internal sealed class CleanupPage : Page
{
    private sealed record Item(CleanCategory Cat, UiCheck Check, TextLabel Size, SettingRow Row)
    {
        public JunkSize? Scanned { get; set; }
    }

    private readonly List<Item> _items = new();
    private readonly UiButton _scan = new("Analisar", ButtonKind.Secondary, Icons.Search);
    private readonly UiButton _clean = new("Limpar selecionados", ButtonKind.Primary, Icons.Clean);
    private readonly TextLabel _total = new("—", Theme.Metric);
    private readonly TextLabel _totalSub = new("Clique em Analisar para calcular quanto espaço pode ser liberado.", Theme.Body, Tone.Muted);
    private readonly MetricBar _progress = new() { Visible = false };
    private readonly LogView _log = new() { Height = Theme.S(160) };
    private readonly Card _logCard = new() { Visible = false };
    private CancellationTokenSource? _cts;
    private bool _scannedOnce;

    public CleanupPage() : base("Limpeza", "Remova arquivos que só ocupam espaço. Nada pessoal é apagado.")
    {
        Header.AddRight(_scan, _clean);
        _scan.Click += async (_, _) => await ScanAsync();
        _clean.Click += async (_, _) => await CleanAsync();

        var summary = new Card(CardStyle.Soft) { Padding = new Padding(Theme.S(22), Theme.S(18), Theme.S(22), Theme.S(18)), Gap = Theme.S(4) };
        summary.Controls.Add(new TextLabel("ESPAÇO A LIBERAR (ITENS SELECIONADOS)", Theme.SmallSemi, Tone.Accent));
        summary.Controls.Add(_total);
        summary.Controls.Add(_totalSub);
        summary.Controls.Add(_progress);
        Content.Controls.Add(summary);

        var list = new Card { Padding = new Padding(Theme.S(20), Theme.S(6), Theme.S(20), Theme.S(6)), Gap = 0 };
        bool first = true;
        foreach (var cat in Cleaner.Categories)
        {
            var check = new UiCheck { Checked = cat.DefaultOn && cat.Available, Enabled = cat.Available };
            var size = new TextLabel("", Theme.BodySemi, Tone.Muted) { TextAlign = ContentAlignment.MiddleRight, Width = Theme.S(150), Height = Theme.S(22) };
            var row = new SettingRow(cat.Glyph, cat.Title, cat.Description, Tone.Success, size, check) { ShowDivider = !first };
            if (!cat.Available) row.AddPill(new Pill("Requer administrador", Tone.Warning));
            check.CheckedChanged += (_, _) => UpdateTotal();
            _items.Add(new Item(cat, check, size, row));
            list.Controls.Add(row);
            first = false;
        }
        Content.Controls.Add(list);

        _logCard.Controls.Add(new TextLabel("Resultado", Theme.H3));
        _logCard.Controls.Add(_log);
        Content.Controls.Add(_logCard);
    }

    public override void OnShown()
    {
        if (!_scannedOnce) _ = ScanAsync();
    }

    private void UpdateTotal()
    {
        long total = _items.Where(i => i.Check.Checked && i.Scanned != null).Sum(i => i.Scanned!.Value.Bytes);
        _total.Text = _scannedOnce ? Fmt.Bytes(total) : "—";
        _clean.Enabled = _items.Any(i => i.Check.Checked) && _cts == null;
    }

    private void SetBusy(bool busy)
    {
        _scan.Enabled = !busy;
        _clean.Enabled = !busy && _items.Any(i => i.Check.Checked);
        _progress.Visible = busy;
        _progress.Indeterminate = busy;
        foreach (var i in _items) i.Check.Enabled = !busy && i.Cat.Available;
    }

    private async Task ScanAsync()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        SetBusy(true);
        _totalSub.Text = "Analisando…";
        try
        {
            foreach (var i in _items)
            {
                if (!i.Cat.Available) { i.Size.Text = "—"; continue; }
                i.Size.Text = "analisando…";
                var r = await Task.Run(() => i.Cat.Scan(_cts.Token));
                i.Scanned = r;
                i.Size.Text = r.Files == 0 && r.Bytes == 0 ? "vazio" : Fmt.Bytes(r.Bytes);
                i.Size.Tone = r.Bytes > 100L << 20 ? Tone.Accent : Tone.Muted;
            }
            _scannedOnce = true;
            long all = _items.Where(i => i.Scanned != null).Sum(i => i.Scanned!.Value.Bytes);
            _totalSub.Text = $"Total encontrado em todas as categorias: {Fmt.Bytes(all)}.";
        }
        catch (Exception ex)
        {
            Log.Error("Análise da limpeza", ex);
            _totalSub.Text = "Erro na análise: " + ex.Message;
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
            UpdateTotal();
        }
    }

    private async Task CleanAsync()
    {
        if (_cts != null) return;
        var selected = _items.Where(i => i.Check.Checked && i.Cat.Available).ToList();
        if (selected.Count == 0) return;
        if (selected.Any(i => i.Cat.Id == "recycle") &&
            MessageBox.Show(FindForm(), "A Lixeira será esvaziada e os itens dela não poderão ser recuperados. Continuar?",
                "Esvaziar Lixeira", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _cts = new CancellationTokenSource();
        SetBusy(true);
        _logCard.Visible = true;
        _log.Clear();
        long freed = 0;
        try
        {
            foreach (var i in selected)
            {
                i.Size.Text = "limpando…";
                var r = await Task.Run(() => i.Cat.Clean(_cts.Token));
                freed += r.Bytes;
                _log.Add($"✓ {i.Cat.Title}: {Fmt.Num(r.Files)} arquivo(s), {Fmt.Bytes(r.Bytes)}" +
                         (r.Skipped > 0 ? $" ({Fmt.Num(r.Skipped)} em uso foram mantidos)" : ""));
                if (r.Note != null) _log.Add("⚠ " + r.Note);
            }
            Settings.Current.TotalBytesFreed += freed;
            Settings.Current.LastOptimization = DateTime.Now;
            Settings.Save();
            _log.Add($"Total liberado: {Fmt.Bytes(freed)}", Tone.Accent);
            MainForm.Instance?.ShowToast($"{Fmt.Bytes(freed)} liberados.");
            Log.Info($"Limpeza manual: {Fmt.Bytes(freed)}");
            MainForm.Instance?.GetPage<DashboardPage>()?.Refresh(force: true);
        }
        catch (Exception ex)
        {
            Log.Error("Limpeza", ex);
            _log.Add("✗ Erro: " + ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
        await ScanAsync();
    }
}
