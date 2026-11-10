namespace Otimizador.UI.Pages;

/// <summary>Cartão com uma lista de ajustes reversíveis, cada um com seu interruptor.</summary>
internal sealed class TweakList : Card
{
    private readonly List<(Tweak tweak, ToggleSwitch toggle, SettingRow row)> _rows = new();
    private bool _loading;

    public TweakList(IEnumerable<Tweak> tweaks)
    {
        Padding = new Padding(Theme.S(20), Theme.S(6), Theme.S(20), Theme.S(6));
        Gap = 0;
        bool first = true;
        foreach (var t in tweaks)
        {
            var toggle = new ToggleSwitch { Enabled = false };
            var row = new SettingRow(t.Glyph, t.Title, t.Description, Tone.Accent, toggle) { ShowDivider = !first };
            if (!t.Available) row.AddPill(new Pill("Requer administrador", Tone.Warning));
            else if (t.RestartNote != null) row.AddPill(new Pill(t.RestartNote.Contains("computador") ? "Reiniciar" : "Sair e entrar", Tone.Muted));
            toggle.CheckedChanged += async (_, _) => await OnToggleAsync(t, toggle);
            _rows.Add((t, toggle, row));
            Controls.Add(row);
            first = false;
        }
    }

    /// <summary>Lê o estado atual de cada ajuste no Windows.</summary>
    public async Task RefreshAsync()
    {
        _loading = true;
        try
        {
            foreach (var (t, toggle, _) in _rows)
            {
                bool on = await Tweaks.IsAppliedAsync(t);
                toggle.Checked = on;
                toggle.Enabled = t.Available;
            }
        }
        finally { _loading = false; }
    }

    private async Task OnToggleAsync(Tweak t, ToggleSwitch toggle)
    {
        if (_loading) return;
        bool want = toggle.Checked;
        toggle.Enabled = false;
        bool ok = want ? await Tweaks.ApplyAsync(t) : await Tweaks.RevertAsync(t);
        bool now = await Tweaks.IsAppliedAsync(t);
        _loading = true;
        toggle.Checked = now;
        _loading = false;
        toggle.Enabled = t.Available;
        if (!ok || now != want)
            MainForm.Instance?.ShowToast($"Não foi possível {(want ? "aplicar" : "desfazer")} \"{t.Title}\". Veja o log.", Tone.Danger);
        else
            MainForm.Instance?.ShowToast((want ? "Aplicado: " : "Desfeito: ") + t.Title + (t.RestartNote != null ? ". " + t.RestartNote : "."));
    }
}
