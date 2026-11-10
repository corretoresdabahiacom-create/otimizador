using System.Diagnostics;

namespace Otimizador.UI;

/// <summary>Base das janelas de diálogo: tema, barra de título escura e conteúdo empilhado.</summary>
internal class DialogBase : Form, IThemed
{
    protected StackPanel Body { get; } = new() { Gap = Theme.S(14) };
    protected Row Buttons { get; } = new() { FixedHeight = Theme.S(40) };

    public DialogBase(string title, int width, int height, bool standalone = false)
    {
        Text = title;
        Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = standalone;
        ShowInTaskbar = standalone;
        StartPosition = standalone ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
        ClientSize = new Size(Theme.S(width), Theme.S(height));
        Icon = AppIcon.Get();
        Body.Padding = new Padding(Theme.S(28), Theme.S(24), Theme.S(28), Theme.S(20));
        Body.Dock = DockStyle.Fill;
        Controls.Add(Body);
        ApplyTheme();
    }

    /// <summary>Adiciona a linha de botões como último item (alinhada à direita).</summary>
    protected void FinishLayout()
    {
        Body.Controls.Add(Buttons);
    }

    public virtual void ApplyTheme()
    {
        BackColor = Theme.P.Bg;
        ForeColor = Theme.P.Text;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.StyleWindow(Handle, Theme.Dark);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Body.PerformLayout();
        if (ShowInTaskbar) { Activate(); Native.SetForegroundWindow(Handle); }
    }
}

internal static class AppIcon
{
    private static Icon? _icon;
    public static Icon Get()
    {
        if (_icon != null) return _icon;
        try { _icon = Icon.ExtractAssociatedIcon(AppInfo.ExePath); } catch { }
        return _icon ??= SystemIcons.Application;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Termos de uso (primeira execução ou nova versão dos termos)
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class TermsForm : DialogBase
{
    public TermsForm(bool readOnly = false) : base("Termos de uso — " + AppInfo.Name, 760, 640, standalone: !readOnly)
    {
        var title = new TextLabel(readOnly ? "Termos de uso" : "Antes de começar", Theme.H2);
        var sub = new TextLabel(readOnly
            ? "Contrato de licença de uso do software."
            : "Leia e aceite o contrato de licença para usar o " + AppInfo.Name + ".", Theme.Body, Tone.Muted);
        var box = new Card { Padding = new Padding(Theme.S(14)) };
        box.FillLast = true;
        var text = new RichTextBox
        {
            ReadOnly = true, BorderStyle = BorderStyle.None, Font = Theme.Mono, DetectUrls = false,
            Text = AppInfo.ReadResource("Termos.txt"), BackColor = Theme.P.Surface, ForeColor = Theme.P.Text,
        };
        box.Controls.Add(text);
        box.Height = Theme.S(380);

        Body.Controls.Add(title);
        Body.Controls.Add(sub);
        Body.Controls.Add(box);

        if (readOnly)
        {
            var close = new UiButton("Fechar", ButtonKind.Primary) { DialogResult = DialogResult.OK };
            Buttons.AddRight(close);
            AcceptButton = close;
            CancelButton = close;
        }
        else
        {
            var check = new UiCheck();
            var lbl = new TextLabel("Li e concordo com os termos de uso", Theme.Body).FitWidth();
            lbl.Cursor = Cursors.Hand;
            lbl.Click += (_, _) => check.Checked = !check.Checked;
            var accept = new UiButton("Aceitar e continuar", ButtonKind.Primary, Icons.Check) { Enabled = false };
            var refuse = new UiButton("Recusar", ButtonKind.Secondary);
            check.CheckedChanged += (_, _) => accept.Enabled = check.Checked;
            accept.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
            refuse.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            Buttons.Controls.Add(check);
            Buttons.Controls.Add(lbl);
            Buttons.AddRight(refuse, accept);
            CancelButton = refuse;
        }
        FinishLayout();
        Body.FillLast = false;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Ativação de licença
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class LicenseForm : DialogBase
{
    private readonly TextField _key = new("Cole aqui a sua chave de licença", Icons.Permissions, 520);
    private readonly TextLabel _status = new("", Theme.Small, Tone.Muted);

    public LicenseForm(string? reason = null, bool standalone = true) : base("Ativação — " + AppInfo.Name, 600, 330, standalone)
    {
        var head = new Row { FixedHeight = Theme.S(48) };
        var logo = new IconBadge(Icons.Lightning, Tone.Accent, 46);
        var titles = new StackPanel { Gap = Theme.S(2) };
        titles.Controls.Add(new TextLabel("Ative sua licença", Theme.H2));
        titles.Controls.Add(new TextLabel(AppInfo.Name + " " + AppInfo.Version, Theme.Small, Tone.Muted));
        head.Controls.Add(logo);
        head.SetFill(titles);

        var info = new TextLabel(reason ?? "Digite a chave recebida por e-mail após a compra. A ativação funciona sem internet.",
            Theme.Body, Tone.Muted);
        _key.Height = Theme.S(40);
        _key.Box.CharacterCasing = CharacterCasing.Upper;
        _key.Box.TextChanged += (_, _) => _status.Text = "";

        Body.Controls.Add(head);
        Body.Controls.Add(info);
        Body.Controls.Add(_key);
        Body.Controls.Add(_status);

        var activate = new UiButton("Ativar", ButtonKind.Primary, Icons.Check);
        var buy = new UiButton("Comprar / suporte", ButtonKind.Ghost, Icons.Mail);
        var exit = new UiButton(standalone ? "Sair" : "Cancelar", ButtonKind.Secondary) { DialogResult = DialogResult.Cancel };
        activate.Click += (_, _) => TryActivate();
        buy.Click += (_, _) =>
        {
            if (!Shell.Open($"mailto:{AppInfo.SupportEmail}?subject=Licen%C3%A7a%20{Uri.EscapeDataString(AppInfo.Name)}"))
                MessageBox.Show(this, "Envie um e-mail para " + AppInfo.SupportEmail, "Suporte", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        Buttons.Controls.Add(buy);
        Buttons.AddRight(exit, activate);
        AcceptButton = activate;
        CancelButton = exit;
        FinishLayout();
    }

    private void TryActivate()
    {
        var key = _key.Text.Trim();
        var st = LicenseService.Check(key);
        if (st.State == LicenseState.Invalid || st.State == LicenseState.Missing)
        {
            _status.Tone = Tone.Danger;
            _status.Text = "Chave inválida. Confira se copiou a chave inteira (inclusive os hífens).";
            Log.Warn("Tentativa de ativação com chave inválida.");
            return;
        }
        if (st.State == LicenseState.Expired)
        {
            _status.Tone = Tone.Danger;
            _status.Text = "Esta chave já venceu. Entre em contato para renovar.";
            return;
        }
        Settings.Current.LicenseKey = LicenseService.Normalize(key);
        Settings.Save();
        Log.Info($"Licença ativada (id {st.Id}).");
        DialogResult = DialogResult.OK;
        Close();
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Novo / editar agendamento
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class ScheduleDialog : DialogBase
{
    public Schedule Result { get; }

    public ScheduleDialog(Schedule? existing) : base(existing == null ? "Novo agendamento" : "Editar agendamento", 520, 470)
    {
        Result = existing == null ? new Schedule() : new Schedule
        {
            Id = existing.Id, Name = existing.Name, Task = existing.Task, Frequency = existing.Frequency, Hour = existing.Hour,
            Minute = existing.Minute, WeekDay = existing.WeekDay, MonthDay = existing.MonthDay, Enabled = existing.Enabled,
            LastRun = existing.LastRun,
        };

        var name = new TextField("Ex.: Limpeza de segunda-feira", Icons.Edit, 440) { Text = Result.Name };
        var task = new UiCombo(440, Enum.GetValues<MaintenanceTask>().Select(t => (object)Schedule.TaskName(t)).ToArray());
        task.SelectedIndex = (int)Result.Task;
        var freq = new UiCombo(165, "Diariamente", "Semanalmente", "Mensalmente") { SelectedIndex = (int)Result.Frequency };
        var time = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Font = Theme.Body,
            Width = Theme.S(84), Value = DateTime.Today.AddHours(Result.Hour).AddMinutes(Result.Minute),
        };
        var days = AppInfo.PtBr.DateTimeFormat.DayNames;
        var week = new UiCombo(165, days.Select(d => (object)(char.ToUpper(d[0]) + d[1..])).ToArray()) { SelectedIndex = (int)Result.WeekDay };
        var month = new NumericUpDown { Minimum = 1, Maximum = 31, Value = Result.MonthDay, Width = Theme.S(80), Font = Theme.Body };

        TextLabel Lbl(string t) => new(t, Theme.SmallSemi, Tone.Muted);
        Body.Controls.Add(Lbl("NOME"));
        Body.Controls.Add(name);
        Body.Controls.Add(Lbl("TAREFA"));
        Body.Controls.Add(new Row(task));
        Body.Controls.Add(Lbl("QUANDO"));
        var whenRow = new Row(freq, time, week, month);
        Body.Controls.Add(whenRow);
        var hint = new TextLabel("O agendamento roda com o app aberto ou na bandeja. Se o PC estiver desligado no horário, roda assim que o app abrir.", Theme.Small, Tone.Muted);
        Body.Controls.Add(hint);

        void Sync()
        {
            week.Visible = freq.SelectedIndex == 1;
            month.Visible = freq.SelectedIndex == 2;
            whenRow.PerformLayout();
        }
        freq.SelectedIndexChanged += (_, _) => Sync();
        Sync();

        var save = new UiButton("Salvar", ButtonKind.Primary, Icons.Check);
        var cancel = new UiButton("Cancelar") { DialogResult = DialogResult.Cancel };
        save.Click += (_, _) =>
        {
            Result.Name = string.IsNullOrWhiteSpace(name.Text) ? Schedule.TaskName((MaintenanceTask)task.SelectedIndex) : name.Text.Trim();
            Result.Task = (MaintenanceTask)task.SelectedIndex;
            Result.Frequency = (ScheduleFrequency)freq.SelectedIndex;
            Result.Hour = time.Value.Hour;
            Result.Minute = time.Value.Minute;
            Result.WeekDay = (DayOfWeek)week.SelectedIndex;
            Result.MonthDay = (int)month.Value;
            DialogResult = DialogResult.OK;
            Close();
        };
        Buttons.AddRight(cancel, save);
        AcceptButton = save;
        CancelButton = cancel;
        FinishLayout();

        foreach (var c in new Control[] { month, time })
        {
            c.BackColor = Theme.P.SurfaceAlt;
            c.ForeColor = Theme.P.Text;
        }
        month.Height = time.Height = Theme.S(30);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Visualizador de texto (Ajuda / Sobre)
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class TextViewer : DialogBase
{
    public TextViewer(string title, string content) : base(title, 720, 600)
    {
        var box = new Card { Padding = new Padding(Theme.S(14)), Height = Theme.S(480), FillLast = true };
        box.Controls.Add(new RichTextBox
        {
            ReadOnly = true, BorderStyle = BorderStyle.None, Font = Theme.Body, DetectUrls = true, Text = content,
            BackColor = Theme.P.Surface, ForeColor = Theme.P.Text,
        });
        Body.Controls.Add(box);
        var close = new UiButton("Fechar", ButtonKind.Primary) { DialogResult = DialogResult.OK };
        Buttons.AddRight(close);
        AcceptButton = close;
        CancelButton = close;
        FinishLayout();
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Escolha dos apps que o Modo Gamer fecha
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class GamerAppsDialog : DialogBase
{
    private readonly ThemedListView _list = new() { CheckBoxes = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };

    public GamerAppsDialog() : base("Apps que o Modo Gamer fecha", 560, 540)
    {
        Body.Controls.Add(new TextLabel("Marque os programas que podem ser fechados ao ativar o Modo Gamer. " +
                                        "Salve seu trabalho antes: programas fechados perdem o que não foi salvo.", Theme.Body, Tone.Muted));
        _list.Columns.Add("Programa", Theme.S(240));
        _list.Columns.Add("Processo", Theme.S(200));
        var card = new ListCard { Height = Theme.S(330) };
        card.Controls.Remove(card.List);
        card.Controls.Add(_list);
        Body.Controls.Add(card);

        var selected = new HashSet<string>(Settings.Current.GamerCloseApps, StringComparer.OrdinalIgnoreCase);
        var names = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == self || p.SessionId == 0) continue;
                    if (p.MainWindowHandle == IntPtr.Zero && !selected.Contains(p.ProcessName)) continue;
                    var title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? p.ProcessName : p.MainWindowTitle;
                    names.TryAdd(p.ProcessName, title.Length > 60 ? title[..60] + "…" : title);
                }
                catch { }
            }
        }
        foreach (var s in selected) names.TryAdd(s, s + " (não está aberto)");
        foreach (var (proc, title) in names)
        {
            if (proc.Equals("explorer", StringComparison.OrdinalIgnoreCase)) continue;
            var it = new ListViewItem(title) { Checked = selected.Contains(proc), Tag = proc };
            it.SubItems.Add(proc + ".exe");
            _list.Items.Add(it);
        }

        var save = new UiButton("Salvar", ButtonKind.Primary, Icons.Check);
        var cancel = new UiButton("Cancelar") { DialogResult = DialogResult.Cancel };
        save.Click += (_, _) =>
        {
            Settings.Current.GamerCloseApps = _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (string)i.Tag!).ToList();
            Settings.Save();
            DialogResult = DialogResult.OK;
            Close();
        };
        Buttons.AddRight(cancel, save);
        AcceptButton = save;
        CancelButton = cancel;
        FinishLayout();
    }
}
