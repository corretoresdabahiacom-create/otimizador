namespace Otimizador.UI.Pages;

/// <summary>Cartão de métrica (CPU, memória, disco...).</summary>
internal sealed class StatTile : Card
{
    public TextLabel Value { get; }
    public TextLabel Sub { get; }
    public MetricBar Bar { get; } = new();

    public StatTile(string glyph, string title, Tone tone)
    {
        Padding = new Padding(Theme.S(18));
        Gap = Theme.S(8);
        var head = new Row { FixedHeight = Theme.S(32) };
        head.Controls.Add(new IconBadge(glyph, tone, 32));
        head.SetFill(new TextLabel(title, Theme.SmallSemi, Tone.Muted));
        Value = new TextLabel("—", Theme.Metric);
        Sub = new TextLabel(" ", Theme.Small, Tone.Muted);
        Bar.Tone = tone;
        Controls.AddRange(new Control[] { head, Value, Sub, Bar });
    }
}

/// <summary>Atalho clicável para outra página.</summary>
internal sealed class ActionTile : Card
{
    public ActionTile(string glyph, string title, string desc, Tone tone, Action onClick)
    {
        Padding = new Padding(Theme.S(18));
        Gap = Theme.S(10);
        Cursor = Cursors.Hand;
        Controls.Add(new Row(new IconBadge(glyph, tone, 40)) { FixedHeight = Theme.S(40) });
        Controls.Add(new TextLabel(title, Theme.H3));
        Controls.Add(new TextLabel(desc, Theme.Small, Tone.Muted));
        void Hook(Control c)
        {
            c.Click += (_, _) => onClick();
            c.Cursor = Cursors.Hand;
            foreach (Control ch in c.Controls) Hook(ch);
        }
        Hook(this);
    }
}

internal sealed class DashboardPage : Page
{
    private readonly SystemStats _stats = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly StatTile _cpu, _ram, _disk, _uptime;
    private readonly ScoreRing _ring = new();
    private readonly TextLabel _healthTitle = new("Analisando…", Theme.H2);
    private readonly TextLabel _healthText = new("Verificando disco, memória, inicialização e segurança.", Theme.Small, Tone.Muted);
    private readonly StackPanel _findings = new() { Gap = Theme.S(6) };
    private readonly UiButton _optimize = new("Otimizar agora", ButtonKind.Primary, Icons.Lightning, large: true);
    private readonly UiButton _analyze = new("Analisar novamente", ButtonKind.Secondary, Icons.Refresh);
    private readonly ToggleSwitch _autoToggle = new();
    private readonly TextLabel _autoInfo = new("", Theme.Small);
    private readonly LogView _log = new() { Height = Theme.S(170) };
    private readonly Card _activity = new();
    private readonly Card _licenseBanner = new(CardStyle.Soft) { Visible = false };
    private readonly TextLabel _licenseText = new("", Theme.Body);
    private bool _analyzing, _busy;

    public DashboardPage() : base("Painel", "Visão geral e otimização do seu computador.")
    {
        Header.AddRight(_optimize);
        _optimize.Click += async (_, _) => await OptimizeAsync();

        // Aviso de licença
        var lr = new Row { FixedHeight = Theme.S(36) };
        lr.Controls.Add(new IconBadge(Icons.Warning, Tone.Warning, 34));
        lr.SetFill(_licenseText);
        var renew = new UiButton("Renovar", ButtonKind.Primary, Icons.Mail);
        renew.Click += (_, _) => Shell.Open($"mailto:{AppInfo.SupportEmail}?subject=Renova%C3%A7%C3%A3o%20{Uri.EscapeDataString(AppInfo.Name)}");
        lr.AddRight(renew);
        _licenseBanner.Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), Theme.S(12));
        _licenseBanner.Controls.Add(lr);
        Content.Controls.Add(_licenseBanner);

        // Saúde + manutenção automática
        var top = new Grid { MinColumnWidth = Theme.S(380), MaxColumns = 2 };
        var health = new Card { Padding = new Padding(Theme.S(22)) };
        var hrow = new Row { FixedHeight = Theme.S(150), Gap = Theme.S(22) };
        hrow.Controls.Add(_ring);
        var htext = new StackPanel { Gap = Theme.S(6) };
        htext.Controls.Add(_healthTitle);
        htext.Controls.Add(_healthText);
        htext.Controls.Add(_findings);
        hrow.SetFill(htext);
        health.Controls.Add(hrow);
        _analyze.Click += async (_, _) => await AnalyzeAsync();
        health.Controls.Add(new Row().AddRight(_analyze));

        var auto = new Card(CardStyle.Hero) { Padding = new Padding(Theme.S(22)), Gap = Theme.S(10) };
        var arow = new Row { FixedHeight = Theme.S(40) };
        arow.Controls.Add(new IconBadge(Icons.Sync, Tone.Accent, 40) { OnHero = true });
        arow.SetFill(new TextLabel("Manutenção automática", Theme.H2) { CustomColor = Color.White });
        arow.AddRight(_autoToggle);
        auto.Controls.Add(arow);
        auto.Controls.Add(new TextLabel(
            "Remove periodicamente arquivos temporários, relatórios de erro e logs antigos, e libera memória quando ela passa de 85%. " +
            "Não mexe na Lixeira, em documentos nem nas configurações do Windows.",
            Theme.Body) { CustomColor = Color.FromArgb(235, 255, 255, 255) });
        _autoInfo.CustomColor = Color.FromArgb(225, 255, 255, 255);
        auto.Controls.Add(_autoInfo);
        var cfg = new UiButton("Configurar", ButtonKind.OnHero, Icons.Settings);
        cfg.Click += (_, _) => MainForm.Instance?.ShowPage(MainForm.PageSettings);
        auto.Controls.Add(new Row(cfg));
        _autoToggle.Checked = Settings.Current.AutoMaintenance;
        _autoToggle.CheckedChanged += (_, _) =>
        {
            Settings.Current.AutoMaintenance = _autoToggle.Checked;
            if (_autoToggle.Checked) Settings.Current.LastAutoMaintenance ??= DateTime.Now;
            Settings.Save();
            UpdateAutoInfo();
            MainForm.Instance?.ShowToast(_autoToggle.Checked ? "Manutenção automática ativada." : "Manutenção automática desativada.");
        };
        top.Controls.Add(health);
        top.Controls.Add(auto);
        Content.Controls.Add(top);

        // Métricas
        var stats = new Grid { MinColumnWidth = Theme.S(200), MaxColumns = 4 };
        _cpu = new StatTile(Icons.Speed, "PROCESSADOR", Tone.Accent);
        _ram = new StatTile(Icons.Memory, "MEMÓRIA", Tone.Warning);
        _disk = new StatTile(Icons.Disk, "DISCO DO SISTEMA", Tone.Success);
        _uptime = new StatTile(Icons.Clock, "LIGADO HÁ", Tone.Muted);
        _uptime.Bar.Visible = false;
        stats.Controls.AddRange(new Control[] { _cpu, _ram, _disk, _uptime });
        Content.Controls.Add(stats);

        // Atividade
        _activity.Controls.Add(new Row(new TextLabel("Atividade", Theme.H3).FitWidth()) { FixedHeight = Theme.S(24) });
        _activity.Controls.Add(_log);
        _activity.Visible = false;
        Content.Controls.Add(_activity);

        // Ações rápidas
        Content.Controls.Add(new TextLabel("Atalhos", Theme.H3));
        var actions = new Grid { MinColumnWidth = Theme.S(200), MaxColumns = 4 };
        actions.Controls.Add(new ActionTile(Icons.Clean, "Limpeza", "Escolha o que limpar e veja quanto espaço volta.", Tone.Success, () => Go(MainForm.PageCleanup)));
        actions.Controls.Add(new ActionTile(Icons.Game, "Modo Gamer", "Mais desempenho para jogos com um clique.", Tone.Accent, () => Go(MainForm.PagePerformance)));
        actions.Controls.Add(new ActionTile(Icons.Power, "Inicialização", "Windows ligando mais rápido.", Tone.Warning, () => Go(MainForm.PageStartup)));
        actions.Controls.Add(new ActionTile(Icons.Repair, "Ferramentas", "Reparo do sistema, disco, rede e segurança.", Tone.Danger, () => Go(MainForm.PageTools)));
        Content.Controls.Add(actions);

        _timer.Tick += (_, _) => UpdateStats();
        UpdateAutoInfo();
    }

    private static void Go(int page) => MainForm.Instance?.ShowPage(page);

    public override void OnShown()
    {
        UpdateStats();
        _timer.Start();
        _autoToggle.Checked = Settings.Current.AutoMaintenance;
        UpdateAutoInfo();
        UpdateLicenseBanner();
        if (_ring.Value < 0) _ = AnalyzeAsync();
    }

    public override void OnHidden() => _timer.Stop();

    public void Refresh(bool force)
    {
        Ui.OnUi(this, () =>
        {
            UpdateAutoInfo();
            if (force && Visible) _ = AnalyzeAsync();
            else if (force) _ring.Value = -1; // reanalisa ao voltar para a página
        });
    }

    private void UpdateLicenseBanner()
    {
        var st = LicenseService.Current();
        bool show = st.State is LicenseState.ExpiringSoon or LicenseState.Grace;
        _licenseBanner.Visible = show;
        if (show) _licenseText.Text = st.Message;
    }

    private void UpdateAutoInfo()
    {
        var s = Settings.Current;
        _autoInfo.Text = s.AutoMaintenance
            ? $"A cada {s.AutoMaintenanceHours} h · última: {Fmt.When(s.LastAutoMaintenance)} · próxima: {Fmt.When(MainForm.NextAutoMaintenance())}"
            : "Desativada. Ative para manter o PC limpo sem precisar lembrar.";
    }

    private void UpdateStats()
    {
        double cpu = _stats.SampleCpu();
        _cpu.Value.Text = $"{cpu:0}%";
        _cpu.Sub.Text = $"{Environment.ProcessorCount} núcleos lógicos";
        _cpu.Bar.Value = cpu;

        var m = SystemStats.Memory();
        _ram.Value.Text = $"{m.LoadPercent}%";
        _ram.Sub.Text = $"{Fmt.Bytes((long)m.Used)} de {Fmt.Bytes((long)m.Total)}";
        _ram.Bar.Value = m.LoadPercent;
        _ram.Bar.Tone = m.LoadPercent >= 85 ? Tone.Danger : Tone.Warning;

        if (SystemStats.SystemDrive() is { } d)
        {
            double usedPct = 100.0 * (d.TotalSize - d.AvailableFreeSpace) / Math.Max(1, d.TotalSize);
            _disk.Value.Text = Fmt.Bytes(d.AvailableFreeSpace);
            _disk.Sub.Text = $"livres de {Fmt.Bytes(d.TotalSize)} ({d.Name.TrimEnd('\\')})";
            _disk.Bar.Value = usedPct;
            _disk.Bar.Tone = usedPct >= 90 ? Tone.Danger : usedPct >= 80 ? Tone.Warning : Tone.Success;
        }

        _uptime.Value.Text = Fmt.Duration(SystemStats.Uptime);
        _uptime.Sub.Text = SystemStats.Uptime.TotalDays >= 7 ? "Reinicie de vez em quando para aplicar atualizações." : "Desde a última inicialização";
    }

    // ── Análise de saúde ──────────────────────────────────────────────────
    private async Task AnalyzeAsync()
    {
        if (_analyzing) return;
        _analyzing = true;
        _analyze.Enabled = false;
        _healthTitle.Text = "Analisando…";
        try
        {
            var junkTask = Maintenance.EstimateJunkAsync(CancellationToken.None);
            var startupTask = StartupManager.LoadAsync();
            var secTask = SystemTools.SecurityAsync();
            long junk = await junkTask;
            var startup = await startupTask;
            var sec = await secTask;

            int score = 0;
            var items = new List<(string text, Tone tone)>();

            if (SystemStats.SystemDrive() is { } d)
            {
                double free = 100.0 * d.AvailableFreeSpace / Math.Max(1, d.TotalSize);
                score += free >= 20 ? 30 : free >= 10 ? 15 : 0;
                items.Add(free >= 20 ? ($"Disco com {free:0}% livre", Tone.Success)
                    : ($"Pouco espaço no disco ({free:0}% livre)", free >= 10 ? Tone.Warning : Tone.Danger));
            }
            var mem = SystemStats.Memory();
            score += mem.LoadPercent < 70 ? 20 : mem.LoadPercent < 85 ? 10 : 0;
            items.Add(mem.LoadPercent < 85 ? ($"Memória em {mem.LoadPercent}% de uso", mem.LoadPercent < 70 ? Tone.Success : Tone.Warning)
                : ($"Memória alta ({mem.LoadPercent}%)", Tone.Danger));

            score += junk < 500L << 20 ? 25 : junk < 2L << 30 ? 15 : 5;
            items.Add(junk < 100L << 20 ? ("Pouco lixo para limpar", Tone.Success)
                : ($"{Fmt.Bytes(junk)} podem ser liberados", junk < 2L << 30 ? Tone.Warning : Tone.Danger));

            int active = startup.Count(e => e.Enabled);
            score += active <= 8 ? 15 : active <= 15 ? 8 : 0;
            items.Add(active <= 8 ? ($"{active} programas na inicialização", Tone.Success)
                : ($"{active} programas abrem com o Windows", active <= 15 ? Tone.Warning : Tone.Danger));

            bool secure = sec.Firewall != false && sec.Defender != false;
            score += secure ? 10 : 0;
            if (!secure) items.Add(("Proteção do Windows desativada", Tone.Danger));

            _ring.Value = Math.Clamp(score, 0, 100);
            _healthTitle.Text = score >= 80 ? "Seu PC está em ótima forma" : score >= 55 ? "Seu PC pode melhorar" : "Seu PC precisa de atenção";
            _healthText.Text = "Atualizado " + Fmt.When(DateTime.Now) + ".";
            _findings.SuspendLayout();
            foreach (Control c in _findings.Controls.Cast<Control>().ToList()) c.Dispose();
            foreach (var (text, tone) in items.OrderBy(i => i.tone == Tone.Success).Take(4))
            {
                var dot = tone == Tone.Success ? "●" : "▲";
                _findings.Controls.Add(new TextLabel($"{dot}  {text}", Theme.Body, tone == Tone.Success ? Tone.Normal : tone));
            }
            _findings.ResumeLayout();
            Ui.Relayout(_findings);
        }
        catch (Exception ex)
        {
            Log.Error("Análise do painel", ex);
            _healthTitle.Text = "Não foi possível analisar";
        }
        finally
        {
            _analyzing = false;
            _analyze.Enabled = true;
        }
    }

    // ── Otimizar agora ────────────────────────────────────────────────────
    private async Task OptimizeAsync()
    {
        if (_busy) return;
        _busy = true;
        _optimize.Enabled = false;
        _optimize.Text = "Otimizando…";
        _activity.Visible = true;
        _log.Clear();
        _log.Add($"Otimização iniciada às {DateTime.Now:HH:mm:ss}", Tone.Muted);
        try
        {
            var sum = await Maintenance.RunAsync(MaintenanceTask.ManutencaoCompleta, s => _log.Add(s), CancellationToken.None);
            _log.Add("Concluído: " + sum.Short(), Tone.Accent);
            MainForm.Instance?.ShowToast(sum.Short());
        }
        catch (Exception ex)
        {
            Log.Error("Otimizar agora", ex);
            _log.Add("✗ Erro: " + ex.Message);
        }
        finally
        {
            _busy = false;
            _optimize.Text = "Otimizar agora";
            _optimize.Enabled = true;
            await AnalyzeAsync();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
