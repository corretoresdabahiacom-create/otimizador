namespace Otimizador.UI.Pages;

internal sealed class DuplicatesPage : Page
{
    private readonly ListCard _card = new();
    private readonly TextField _path = new("Pasta para procurar", Icons.Folder, 360);
    private readonly UiButton _browse = new("Escolher…", ButtonKind.Secondary, Icons.FolderOpen);
    private readonly UiCombo _min = new(150, "Maiores que 100 KB", "Maiores que 1 MB", "Maiores que 10 MB", "Maiores que 100 MB") { SelectedIndex = 1 };
    private readonly UiButton _scan = new("Procurar", ButtonKind.Primary, Icons.Search);
    private readonly UiButton _mark = new("Marcar cópias", ButtonKind.Secondary, Icons.Check) { Enabled = false };
    private readonly UiButton _delete = new("Mover para a Lixeira", ButtonKind.Danger, Icons.Delete) { Enabled = false };
    private readonly TextLabel _status = new("Escolha uma pasta e clique em Procurar. Os arquivos removidos vão para a Lixeira.", Theme.Body, Tone.Muted);
    private readonly MetricBar _progress = new() { Visible = false };
    private CancellationTokenSource? _cts;

    private ThemedListView List => _card.List;
    private static readonly long[] MinSizes = { 100L << 10, 1L << 20, 10L << 20, 100L << 20 };

    public DuplicatesPage() : base("Duplicados", "Encontre arquivos idênticos (mesmo conteúdo) que ocupam espaço à toa.", fillMode: true)
    {
        Header.AddRight(_mark, _delete);
        _path.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var bar = new Row { FixedHeight = Theme.S(36) };
        bar.Controls.Add(_path);
        bar.Controls.Add(_browse);
        bar.Controls.Add(_min);
        bar.Controls.Add(_scan);
        Content.Controls.Add(bar);
        var st = new StackPanel { Gap = Theme.S(6) };
        st.Controls.Add(_status);
        st.Controls.Add(_progress);
        Content.Controls.Add(st);

        List.CheckBoxes = true;
        List.ShowGroups = true;
        List.Columns.Add("Arquivo", Theme.S(520));
        List.Columns.Add("Tamanho", Theme.S(100));
        List.Columns.Add("Modificado em", Theme.S(140));
        List.ItemChecked += (_, _) => UpdateSelection();
        List.DoubleClick += (_, _) =>
        {
            if (List.SelectedItems.Count == 1 && List.SelectedItems[0].Tag is FileInfo f) Shell.ShowInExplorer(f.FullName);
        };
        Content.Controls.Add(_card);

        _browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = _path.Text, Description = "Pasta onde procurar duplicados", UseDescriptionForTitle = true };
            if (d.ShowDialog(FindForm()) == DialogResult.OK) _path.Text = d.SelectedPath;
        };
        _scan.Click += async (_, _) => await ScanOrCancelAsync();
        _mark.Click += (_, _) => MarkCopies();
        _delete.Click += (_, _) => DeleteChecked();
    }

    private async Task ScanOrCancelAsync()
    {
        if (_cts != null) { _cts.Cancel(); return; }
        var root = _path.Text.Trim();
        if (!Directory.Exists(root)) { _status.Text = "Pasta não encontrada."; _status.Tone = Tone.Danger; return; }
        if (string.Equals(Path.GetFullPath(root).TrimEnd('\\'), Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
        {
            _status.Text = "Não procure duplicados dentro da pasta do Windows: arquivos repetidos ali são normais e necessários.";
            _status.Tone = Tone.Danger;
            return;
        }

        _cts = new CancellationTokenSource();
        _scan.Text = "Cancelar";
        _scan.Glyph = Icons.Cancel;
        _scan.Kind = ButtonKind.Secondary;
        _progress.Visible = true;
        _progress.Indeterminate = true;
        _status.Tone = Tone.Muted;
        List.Items.Clear();
        List.Groups.Clear();
        UpdateSelection();
        try
        {
            var progress = new Progress<string>(s => _status.Text = s);
            var groups = await DuplicateFinder.FindAsync(root, MinSizes[_min.SelectedIndex], progress, _cts.Token);
            Show(groups);
        }
        catch (OperationCanceledException) { _status.Text = "Busca cancelada."; }
        catch (Exception ex)
        {
            Log.Error("Duplicados", ex);
            _status.Text = "Erro: " + ex.Message;
            _status.Tone = Tone.Danger;
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _scan.Text = "Procurar";
            _scan.Glyph = Icons.Search;
            _scan.Kind = ButtonKind.Primary;
            _progress.Visible = false;
            _progress.Indeterminate = false;
        }
    }

    private void Show(List<DuplicateGroup> groups)
    {
        List.BeginUpdate();
        int gi = 0;
        foreach (var g in groups.Take(2000))
        {
            var grp = new ListViewGroup($"Grupo {++gi} · {g.Files.Count} cópias de {Fmt.Bytes(g.Size)} · {Fmt.Bytes(g.Wasted)} desperdiçados");
            List.Groups.Add(grp);
            foreach (var f in g.Files.OrderBy(f => f.LastWriteTime))
            {
                var it = new ListViewItem(f.FullName, grp) { Tag = f };
                it.SubItems.Add(Fmt.Bytes(g.Size));
                it.SubItems.Add(f.LastWriteTime.ToString("dd/MM/yyyy HH:mm"));
                List.Items.Add(it);
            }
        }
        List.EndUpdate();
        long wasted = groups.Sum(g => g.Wasted);
        _status.Text = groups.Count == 0
            ? "Nenhum arquivo duplicado encontrado."
            : $"{groups.Count} grupo(s) de arquivos idênticos · {Fmt.Bytes(wasted)} podem ser liberados. Use \"Marcar cópias\" para manter só o arquivo mais antigo de cada grupo.";
        _status.Tone = groups.Count == 0 ? Tone.Success : Tone.Normal;
        _mark.Enabled = groups.Count > 0;
    }

    private void MarkCopies()
    {
        List.BeginUpdate();
        foreach (ListViewGroup g in List.Groups)
            for (int i = 0; i < g.Items.Count; i++)
                g.Items[i].Checked = i > 0; // mantém o mais antigo (primeiro)
        List.EndUpdate();
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var checkedItems = List.CheckedItems.Cast<ListViewItem>().ToList();
        long bytes = checkedItems.Sum(i => i.Tag is FileInfo f ? SafeLen(f) : 0);
        _delete.Enabled = checkedItems.Count > 0 && _cts == null;
        _delete.Text = checkedItems.Count > 0 ? $"Mover {checkedItems.Count} para a Lixeira ({Fmt.Bytes(bytes)})" : "Mover para a Lixeira";
    }

    private static long SafeLen(FileInfo f) { try { return f.Length; } catch { return 0; } }

    private void DeleteChecked()
    {
        // Segurança: nunca remover todas as cópias de um grupo.
        foreach (ListViewGroup g in List.Groups)
        {
            var items = g.Items.Cast<ListViewItem>().ToList();
            if (items.Count > 0 && items.All(i => i.Checked))
            {
                MessageBox.Show(FindForm(), "Em pelo menos um grupo todas as cópias estão marcadas.\nDeixe ao menos um arquivo de cada grupo desmarcado.",
                    "Duplicados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        var toDelete = List.CheckedItems.Cast<ListViewItem>().ToList();
        if (toDelete.Count == 0) return;
        if (MessageBox.Show(FindForm(), $"Mover {toDelete.Count} arquivo(s) para a Lixeira?\nVocê pode restaurá-los pela Lixeira se precisar.",
                "Duplicados", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        int ok = 0, fail = 0;
        long bytes = 0;
        List.BeginUpdate();
        foreach (var it in toDelete)
        {
            var f = (FileInfo)it.Tag!;
            long len = SafeLen(f);
            if (Native.SendToRecycleBin(f.FullName) && !File.Exists(f.FullName)) { ok++; bytes += len; it.Remove(); }
            else fail++;
        }
        foreach (var g in List.Groups.Cast<ListViewGroup>().Where(g => g.Items.Count < 2).ToList())
        {
            foreach (var it in g.Items.Cast<ListViewItem>().ToList()) it.Remove();
            List.Groups.Remove(g);
        }
        List.EndUpdate();
        UpdateSelection();
        Log.Info($"Duplicados: {ok} movidos para a Lixeira ({Fmt.Bytes(bytes)}), {fail} falharam.");
        MainForm.Instance?.ShowToast(fail == 0 ? $"{ok} arquivo(s) movidos para a Lixeira ({Fmt.Bytes(bytes)})."
            : $"{ok} movidos, {fail} não puderam ser movidos (em uso ou sem permissão).", fail == 0 ? Tone.Success : Tone.Warning);
    }
}
