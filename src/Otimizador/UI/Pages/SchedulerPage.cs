namespace Otimizador.UI.Pages;

internal sealed class SchedulerPage : Page
{
    private readonly Card _list = new() { Padding = new Padding(Theme.S(20), Theme.S(6), Theme.S(20), Theme.S(6)), Gap = 0 };
    private readonly Card _empty = new(CardStyle.Soft) { Padding = new Padding(Theme.S(24)) };

    public SchedulerPage() : base("Agendamentos", "Rode limpezas e manutenções automaticamente no horário que preferir.")
    {
        var add = new UiButton("Novo agendamento", ButtonKind.Primary, Icons.Add);
        add.Click += (_, _) => Edit(null);
        Header.AddRight(add);

        var er = new Row { FixedHeight = Theme.S(44) };
        er.Controls.Add(new IconBadge(Icons.Calendar, Tone.Accent, 44));
        er.SetFill(new TextLabel("Nenhum agendamento ainda. Sugestão: uma \"Manutenção completa\" semanal no horário do almoço.", Theme.Body));
        _empty.Controls.Add(er);
        Content.Controls.Add(_empty);
        Content.Controls.Add(_list);
    }

    public override void OnShown() => Rebuild();

    private void Rebuild()
    {
        _list.SuspendLayout();
        foreach (Control c in _list.Controls.Cast<Control>().ToList()) c.Dispose();
        bool first = true;
        foreach (var s in Scheduler.All.OrderBy(s => s.NextRun ?? DateTime.MaxValue))
        {
            var toggle = new ToggleSwitch { Checked = s.Enabled };
            var edit = new UiButton("", ButtonKind.Ghost, Icons.Edit);
            var del = new UiButton("", ButtonKind.Ghost, Icons.Delete);
            Ui.Tip(edit, "Editar");
            Ui.Tip(del, "Excluir");
            var desc = $"{Schedule.TaskName(s.Task)} · {s.Describe()}\nPróxima: {(s.Enabled ? Fmt.When(s.NextRun) : "pausado")} · Última: {Fmt.When(s.LastRun)}";
            var row = new SettingRow(Icons.Calendar, s.Name, desc, s.Enabled ? Tone.Accent : Tone.Muted, edit, del, toggle) { ShowDivider = !first };
            toggle.CheckedChanged += (_, _) => { Scheduler.SetEnabled(s, toggle.Checked); BeginInvoke(Rebuild); };
            edit.Click += (_, _) => BeginInvoke(() => Edit(s));
            del.Click += (_, _) =>
            {
                if (MessageBox.Show(FindForm(), $"Excluir o agendamento \"{s.Name}\"?", "Agendamentos",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Scheduler.Remove(s.Id);
                BeginInvoke(Rebuild);
            };
            _list.Controls.Add(row);
            first = false;
        }
        _list.Visible = Scheduler.All.Count > 0;
        _empty.Visible = Scheduler.All.Count == 0;
        _list.ResumeLayout();
        Ui.ApplyThemeTree(_list);
        PerformLayout();
    }

    private void Edit(Schedule? s)
    {
        using var d = new ScheduleDialog(s);
        if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
        Scheduler.Upsert(d.Result);
        Rebuild();
        MainForm.Instance?.ShowToast($"Agendado: próxima execução {Fmt.When(d.Result.NextRun)}.");
    }
}
