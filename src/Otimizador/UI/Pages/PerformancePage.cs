using System.Diagnostics;

namespace Otimizador.UI.Pages;

internal sealed class PerformancePage : Page
{
    private static readonly string[] GamerTweaks = { "power_high", "game_mode", "game_dvr", "foreground" };

    private readonly TweakList _tweaks = new(Tweaks.InGroup("desempenho"));
    private readonly UiButton _gamerBtn = new("Ativar Modo Gamer", ButtonKind.Primary, Icons.Play);
    private readonly Pill _gamerPill = new("Desativado", Tone.Muted);
    private readonly UiButton _ecoBtn = new("Ativar Modo Economia", ButtonKind.Primary, Icons.Play);
    private readonly Pill _ecoPill = new("Desativado", Tone.Muted);
    private readonly UiButton _ramBtn = new("Liberar agora", ButtonKind.Secondary, Icons.Memory);
    private bool _loaded;

    public PerformancePage() : base("Desempenho", "Modos prontos e ajustes individuais. Tudo pode ser desfeito.")
    {
        var modes = new Grid { MinColumnWidth = Theme.S(360), MaxColumns = 2 };
        modes.Controls.Add(ModeCard(Icons.Game, "Modo Gamer", _gamerPill,
            "Plano Alto desempenho, Modo de Jogo do Windows, sem gravação em segundo plano, prioridade ao jogo, " +
            "fecha os apps escolhidos e libera memória.", _gamerBtn, out var gamerExtra));
        var apps = new UiButton("Apps a fechar…", ButtonKind.Ghost, Icons.List);
        apps.Click += (_, _) => { using var d = new GamerAppsDialog(); d.ShowDialog(FindForm()); };
        gamerExtra.Controls.Add(apps);
        modes.Controls.Add(ModeCard(Icons.Battery, "Modo Economia (notebook)", _ecoPill,
            "Plano Equilibrado e processador a 99% na bateria: desliga o Turbo só na bateria, " +
            "o notebook esquenta menos e a bateria dura mais. Na tomada o desempenho é o normal.", _ecoBtn, out _));
        Content.Controls.Add(modes);

        _gamerBtn.Click += async (_, _) => await ToggleGamerAsync();
        _ecoBtn.Click += async (_, _) => await ToggleEcoAsync();

        Content.Controls.Add(new TextLabel("Ajustes individuais", Theme.H3));
        Content.Controls.Add(_tweaks);

        var mem = new Card { Padding = new Padding(Theme.S(20), Theme.S(6), Theme.S(20), Theme.S(6)) };
        mem.Controls.Add(new SettingRow(Icons.Memory, "Liberar memória RAM",
            "Devolve ao Windows a memória que os programas reservaram e não estão usando. Útil antes de abrir um jogo ou programa pesado.",
            Tone.Warning, _ramBtn));
        _ramBtn.Click += async (_, _) =>
        {
            _ramBtn.Enabled = false;
            var r = await Memory.OptimizeAsync();
            _ramBtn.Enabled = true;
            MainForm.Instance?.ShowToast($"{Fmt.Bytes(r.FreedBytes)} de memória liberados ({r.After.LoadPercent}% em uso agora).");
        };
        Content.Controls.Add(mem);
        UpdateModes();
    }

    private static Card ModeCard(string glyph, string title, Pill pill, string desc, UiButton button, out Row actions)
    {
        var c = new Card { Padding = new Padding(Theme.S(22)), Gap = Theme.S(12) };
        var head = new Row { FixedHeight = Theme.S(44) };
        head.Controls.Add(new IconBadge(glyph, Tone.Accent, 44));
        head.SetFill(new TextLabel(title, Theme.H2));
        head.AddRight(pill);
        c.Controls.Add(head);
        c.Controls.Add(new TextLabel(desc, Theme.Body, Tone.Muted));
        actions = new Row(button);
        c.Controls.Add(actions);
        return c;
    }

    public override async void OnShown()
    {
        UpdateModes();
        if (_loaded) return;
        _loaded = true;
        await _tweaks.RefreshAsync();
    }

    private void UpdateModes()
    {
        var s = Settings.Current;
        _gamerPill.Text = s.GamerActive ? "Ativo" : "Desativado";
        _gamerPill.Tone = s.GamerActive ? Tone.Success : Tone.Muted;
        _gamerBtn.Text = s.GamerActive ? "Desativar Modo Gamer" : "Ativar Modo Gamer";
        _gamerBtn.Glyph = s.GamerActive ? Icons.Stop : Icons.Play;
        _gamerBtn.Kind = s.GamerActive ? ButtonKind.Secondary : ButtonKind.Primary;
        _ecoPill.Text = s.EcoActive ? "Ativo" : "Desativado";
        _ecoPill.Tone = s.EcoActive ? Tone.Success : Tone.Muted;
        _ecoBtn.Text = s.EcoActive ? "Desativar Modo Economia" : "Ativar Modo Economia";
        _ecoBtn.Glyph = s.EcoActive ? Icons.Stop : Icons.Play;
        _ecoBtn.Kind = s.EcoActive ? ButtonKind.Secondary : ButtonKind.Primary;
    }

    private async Task ToggleGamerAsync()
    {
        var s = Settings.Current;
        _gamerBtn.Enabled = false;
        try
        {
            if (!s.GamerActive)
            {
                var running = RunningApps(s.GamerCloseApps);
                if (running.Count > 0 && MessageBox.Show(FindForm(),
                        "Estes programas serão fechados:\n\n• " + string.Join("\n• ", running.Select(p => p.name).Distinct(StringComparer.OrdinalIgnoreCase)) +
                        "\n\nSalve seu trabalho antes de continuar. Continuar?",
                        "Modo Gamer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                if (s.EcoActive) await DeactivateEcoAsync();
                s.GamerAppliedTweaks.Clear();
                foreach (var id in GamerTweaks)
                {
                    var t = Tweaks.Get(id);
                    if (!t.Available || await Tweaks.IsAppliedAsync(t)) continue;
                    if (await Tweaks.ApplyAsync(t)) s.GamerAppliedTweaks.Add(id);
                }
                int closed = await Task.Run(() => CloseApps(running.Select(r => r.pid)));
                var mem = await Memory.OptimizeAsync();
                s.GamerActive = true;
                Settings.Save();
                MainForm.Instance?.ShowToast($"Modo Gamer ativo. {closed} app(s) fechado(s), {Fmt.Bytes(mem.FreedBytes)} de memória liberados.");
            }
            else
            {
                foreach (var id in s.GamerAppliedTweaks.ToList())
                    await Tweaks.RevertAsync(Tweaks.Get(id));
                s.GamerAppliedTweaks.Clear();
                s.GamerActive = false;
                Settings.Save();
                MainForm.Instance?.ShowToast("Modo Gamer desativado. Configurações anteriores restauradas.");
            }
        }
        finally
        {
            _gamerBtn.Enabled = true;
            UpdateModes();
            await _tweaks.RefreshAsync();
        }
    }

    private async Task ToggleEcoAsync()
    {
        var s = Settings.Current;
        _ecoBtn.Enabled = false;
        try
        {
            if (!s.EcoActive)
            {
                if (s.GamerActive) await ToggleGamerAsync();
                var power = Tweaks.Get("power_high");
                if (await Tweaks.IsAppliedAsync(power)) await Tweaks.RevertAsync(power);
                await Shell.RunAsync(Shell.Sys("powercfg.exe"), "/setactive " + PowerPlans.Balanced, 10_000);
                await Tweaks.ApplyAsync(Tweaks.Get("battery_cpu"));
                s.EcoActive = true;
                Settings.Save();
                MainForm.Instance?.ShowToast("Modo Economia ativo: menos calor e mais bateria.");
            }
            else
            {
                await DeactivateEcoAsync();
                MainForm.Instance?.ShowToast("Modo Economia desativado.");
            }
        }
        finally
        {
            _ecoBtn.Enabled = true;
            UpdateModes();
            await _tweaks.RefreshAsync();
        }
    }

    private static async Task DeactivateEcoAsync()
    {
        await Tweaks.RevertAsync(Tweaks.Get("battery_cpu"));
        Settings.Current.EcoActive = false;
        Settings.Save();
    }

    private static List<(int pid, string name)> RunningApps(IEnumerable<string> names)
    {
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var list = new List<(int, string)>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.SessionId != 0 && set.Contains(p.ProcessName) && p.Id != Environment.ProcessId)
                        list.Add((p.Id, p.ProcessName));
                }
                catch { }
            }
        }
        return list;
    }

    private static int CloseApps(IEnumerable<int> pids)
    {
        int n = 0;
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow() && p.WaitForExit(3000)) { n++; continue; }
                p.Kill();
                n++;
            }
            catch { }
        }
        return n;
    }
}
