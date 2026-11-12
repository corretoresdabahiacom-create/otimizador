using Otimizador.UI.Pages;

namespace Otimizador.UI;

/// <summary>Janela principal: barra lateral, páginas (criadas sob demanda), bandeja e rotinas em segundo plano.</summary>
internal sealed class MainForm : Form, IThemed
{
    private readonly Sidebar _sidebar;
    private readonly Panel _host = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<int, Page> _pages = new();
    private readonly NotifyIcon _tray = new();
    private readonly Toast _toast = new();
    private readonly WheelRouter _wheel = new();
    private readonly Scheduler _scheduler = new();
    private readonly System.Windows.Forms.Timer _autoTimer = new() { Interval = 5 * 60 * 1000 };
    private readonly bool _startHidden;
    private bool _exiting, _maintRunning;
    private int _current = -1;

    public static MainForm? Instance { get; private set; }

    private static readonly (string glyph, string text, Func<Page> make)[] Nav =
    {
        (Icons.Home, "Painel", () => new DashboardPage()),
        (Icons.Clean, "Limpeza", () => new CleanupPage()),
        (Icons.Speed, "Desempenho", () => new PerformancePage()),
        (Icons.Power, "Inicialização", () => new StartupPage()),
        (Icons.Apps, "Programas", () => new ProgramsPage()),
        (Icons.Copy, "Duplicados", () => new DuplicatesPage()),
        (Icons.Shield, "Privacidade", () => new PrivacyPage()),
        (Icons.Repair, "Ferramentas", () => new ToolsPage()),
        (Icons.Calendar, "Agendamentos", () => new SchedulerPage()),
        (Icons.Settings, "Configurações", () => new SettingsPage()),
    };

    public const int PageDashboard = 0, PageCleanup = 1, PagePerformance = 2, PageStartup = 3, PageTools = 7, PageSettings = 9;

    public MainForm(bool startHidden)
    {
        Instance = this;
        _startHidden = startHidden;
        Text = AppInfo.Name;
        Icon = AppIcon.Get();
        Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(Theme.S(980), Theme.S(640));
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 768);
        Size = new Size(Math.Min(Theme.S(1200), wa.Width - 40), Math.Min(Theme.S(780), wa.Height - 40));

        _sidebar = new Sidebar(Nav.Select(n => (n.glyph, n.text)).ToArray());
        _sidebar.Navigate += ShowPage;
        Controls.Add(_host);
        Controls.Add(_sidebar);
        Controls.Add(_toast);
        _toast.BringToFront();

        InitTray();
        Application.AddMessageFilter(_wheel);
        Theme.Changed += OnThemeChanged;
        _scheduler.Ran += (s, sum) => Notify($"Agendamento: {s.Name}", sum.Short());
        _autoTimer.Tick += async (_, _) => await AutoMaintenanceTickAsync();
        _autoTimer.Start();
        ApplyTheme();
        ShowPage(PageDashboard);
        _ = NotifyUpdateAsync();
    }

    private async Task NotifyUpdateAsync()
    {
        var info = await Updater.CheckIfDueAsync();
        if (info == null || IsDisposed) return;
        ShowToast($"Nova versão {info.Version} disponível. Veja em Configurações.", Tone.Accent);
        Notify("Atualização disponível", $"{AppInfo.Name} {info.Version} já pode ser baixado.");
    }

    // ── Páginas ───────────────────────────────────────────────────────────
    public void ShowPage(int index)
    {
        if (index == _current) return;
        if (_current >= 0 && _pages.TryGetValue(_current, out var old))
        {
            if (_pageActive) old.OnHidden();
            old.Visible = false;
        }
        if (!_pages.TryGetValue(index, out var page))
        {
            page = Nav[index].make();
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _pages[index] = page;
            _host.Controls.Add(page);
            Ui.ApplyThemeTree(page);
        }
        _current = index;
        page.Visible = true;
        page.BringToFront();
        page.PerformLayout();
        if (_pageActive) page.OnShown();
        _sidebar.Select(index);
    }

    /// <summary>Pausa a página atual (timers, análises) quando a janela é escondida ou minimizada.</summary>
    private bool _pageActive;
    private void UpdatePageActivity()
    {
        bool active = Visible && WindowState != FormWindowState.Minimized;
        if (active == _pageActive) return;
        _pageActive = active;
        if (_current >= 0 && _pages.TryGetValue(_current, out var p))
        {
            if (active) p.OnShown(); else p.OnHidden();
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdatePageActivity();
    }

    public T? GetPage<T>() where T : Page => _pages.Values.OfType<T>().FirstOrDefault();

    // ── Tema ──────────────────────────────────────────────────────────────
    public void ApplyTheme()
    {
        BackColor = Theme.P.Bg;
        _host.BackColor = Theme.P.Bg;
        if (IsHandleCreated) Native.StyleWindow(Handle, Theme.Dark);
    }

    private void OnThemeChanged()
    {
        SuspendLayout();
        Ui.ApplyThemeTree(this);
        ResumeLayout(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.StyleWindow(Handle, Theme.Dark);
    }

    // ── Notificações ──────────────────────────────────────────────────────
    public void ShowToast(string text, Tone tone = Tone.Success)
    {
        Ui.OnUi(this, () =>
        {
            if (Visible && WindowState != FormWindowState.Minimized) _toast.Show(text, tone, ClientSize);
            else Notify(AppInfo.Name, text);
        });
    }

    public void Notify(string title, string text)
    {
        Ui.OnUi(this, () =>
        {
            if (!_tray.Visible) return;
            _tray.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);
        });
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _toast.Reposition(ClientSize);
        UpdatePageActivity();
    }

    // ── Bandeja ───────────────────────────────────────────────────────────
    private void InitTray()
    {
        _tray.Icon = AppIcon.Get();
        _tray.Text = AppInfo.Name;
        _tray.Visible = true;
        var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
        menu.Items.Add("Abrir " + AppInfo.Name, null, (_, _) => RestoreFromTray());
        menu.Items.Add("Otimizar agora", null, async (_, _) => await QuickOptimizeFromTrayAsync());
        menu.Items.Add("Liberar memória", null, async (_, _) =>
        {
            var r = await Memory.OptimizeAsync();
            Notify("Memória", $"{Fmt.Bytes(r.FreedBytes)} liberados.");
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };
    }

    private async Task QuickOptimizeFromTrayAsync()
    {
        if (_maintRunning) return;
        _maintRunning = true;
        try
        {
            var sum = await Maintenance.RunAsync(MaintenanceTask.ManutencaoCompleta, null, CancellationToken.None);
            Notify("Otimização concluída", sum.Short());
            GetPage<DashboardPage>()?.Refresh(force: true);
        }
        finally { _maintRunning = false; }
    }

    public void RestoreFromTray()
    {
        Ui.OnUi(this, () =>
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Activate();
            Native.SetForegroundWindow(Handle);
        });
    }

    public void ExitApp()
    {
        _exiting = true;
        Close();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden && !IsHandleCreated)
        {
            CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing && Settings.Current.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            if (!Settings.Current.TrayHintShown)
            {
                Settings.Current.TrayHintShown = true;
                Settings.Save();
                _tray.ShowBalloonTip(5000, AppInfo.Name,
                    "O app continua na bandeja para agendamentos e manutenção automática. Para sair, clique com o botão direito no ícone.",
                    ToolTipIcon.Info);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Theme.Changed -= OnThemeChanged;
        Application.RemoveMessageFilter(_wheel);
        _autoTimer.Dispose();
        _scheduler.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosed(e);
    }

    // ── Manutenção automática ─────────────────────────────────────────────
    public static DateTime? NextAutoMaintenance()
    {
        var s = Settings.Current;
        if (!s.AutoMaintenance) return null;
        return (s.LastAutoMaintenance ?? DateTime.Now.AddMinutes(-1).AddHours(-s.AutoMaintenanceHours)).AddHours(s.AutoMaintenanceHours);
    }

    private async Task AutoMaintenanceTickAsync()
    {
        var s = Settings.Current;
        if (!s.AutoMaintenance || _maintRunning) return;
        if (NextAutoMaintenance() is DateTime next && next > DateTime.Now) return;
        _maintRunning = true;
        try
        {
            var lines = new List<string>();
            var sum = await Maintenance.RunAsync(MaintenanceTask.LimpezaRapida, null, CancellationToken.None);
            long ram = 0;
            if (SystemStats.Memory().LoadPercent >= 85)
                ram = (await Memory.OptimizeAsync()).FreedBytes;
            s.LastAutoMaintenance = DateTime.Now;
            Settings.Save();
            if (sum.BytesFreed > 50L * 1024 * 1024 || ram > 0)
                Notify("Manutenção automática", (sum with { RamFreed = ram }).Short());
            GetPage<DashboardPage>()?.Refresh(force: true);
        }
        catch (Exception ex) { Log.Error("Manutenção automática", ex); }
        finally { _maintRunning = false; }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Barra lateral
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class Sidebar : Panel, IThemed
{
    private readonly List<NavItem> _items = new();
    private readonly UiButton _themeBtn;
    private readonly TextLabel _license;
    public event Action<int>? Navigate;

    public Sidebar((string glyph, string text)[] items)
    {
        Dock = DockStyle.Left;
        Width = Theme.S(236);
        DoubleBuffered = true;
        Padding = new Padding(Theme.S(14), Theme.S(18), Theme.S(14), Theme.S(14));

        for (int i = 0; i < items.Length; i++)
        {
            int idx = i;
            var n = new NavItem(items[i].glyph, items[i].text);
            n.Click += (_, _) => Navigate?.Invoke(idx);
            _items.Add(n);
            Controls.Add(n);
        }

        _themeBtn = new UiButton("", ButtonKind.Ghost, Theme.Dark ? Icons.Sun : Icons.Moon);
        _themeBtn.Click += (_, _) =>
        {
            Settings.Current.Theme = Theme.Dark ? "light" : "dark";
            Settings.Save();
            Theme.Apply(Settings.Current.Theme);
        };
        Ui.Tip(_themeBtn, "Alternar tema claro/escuro");
        _license = new TextLabel("", Theme.Small, Tone.Muted);
        Controls.Add(_themeBtn);
        Controls.Add(_license);
        UpdateLicense();
        ApplyTheme();
    }

    public void UpdateLicense()
    {
        var st = LicenseService.Current();
        _license.Text = $"v{AppInfo.Version} · " + (st.Lifetime ? "Licença vitalícia" : st.LastValidDay is DateTime d ? "Válida até " + d.ToString("dd/MM/yyyy") : "");
    }

    public void Select(int index)
    {
        for (int i = 0; i < _items.Count; i++) _items[i].Selected = i == index;
    }

    public void ApplyTheme()
    {
        BackColor = Theme.P.Sidebar;
        _themeBtn.Glyph = Theme.Dark ? Icons.Sun : Icons.Moon;
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (_themeBtn == null || _license == null) return; // ainda no construtor
        int x = Padding.Left, w = ClientSize.Width - Padding.Horizontal;
        int y = Padding.Top + Theme.S(64);
        foreach (var n in _items)
        {
            n.SetBounds(x, y, w, Theme.S(40));
            y += Theme.S(42);
        }
        int bottom = ClientSize.Height - Padding.Bottom;
        _themeBtn.Location = new Point(ClientSize.Width - Padding.Right - _themeBtn.Width, bottom - _themeBtn.Height);
        _license.SetBounds(x + Theme.S(4), bottom - _themeBtn.Height, w - _themeBtn.Width - Theme.S(8), _themeBtn.Height);
        _license.TextAlign = ContentAlignment.MiddleLeft;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Theme.Smooth(g);
        // Logo
        var logo = new RectangleF(Padding.Left + Theme.S(4), Padding.Top, Theme.S(40), Theme.S(40));
        using (var path = Theme.Round(logo, Theme.S(11)))
        using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(logo, Theme.P.Gradient1, Theme.P.Gradient2, 45f))
            g.FillPath(br, path);
        TextRenderer.DrawText(g, Icons.Lightning, Theme.Icon(15f), Rectangle.Round(logo), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        int tx = (int)logo.Right + Theme.S(12);
        TextRenderer.DrawText(g, "Otimizador", Theme.H3, new Point(tx, Padding.Top + Theme.S(1)), Theme.P.Text, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "PC PRO", Theme.SmallSemi, new Point(tx, Padding.Top + Theme.S(22)), Theme.P.Accent, TextFormatFlags.NoPadding);
        // Divisória direita
        using var pen = new Pen(Theme.P.Border);
        g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
    }
}

internal sealed class NavItem : Button, IThemed
{
    private bool _hover, _selected;
    private readonly string _glyph;

    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    public NavItem(string glyph, string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Opaque, false); // ButtonBase é "Opaque": sem isso o fundo não é pintado
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        _glyph = glyph;
        Text = text;
        AccessibleName = text;
    }

    public void ApplyTheme() => Invalidate();
    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var p = Theme.P;
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        if (_selected || _hover)
        {
            using var path = Theme.Round(r, Theme.S(9));
            using var b = new SolidBrush(_selected ? p.AccentSoft : p.Hover);
            g.FillPath(b, path);
        }
        if (_selected)
        {
            using var bar = Theme.Round(new RectangleF(0, Height * 0.25f, Theme.S(3), Height * 0.5f), Theme.S(2));
            using var b = new SolidBrush(p.Accent);
            g.FillPath(b, bar);
        }
        var color = _selected ? p.Accent : p.Muted;
        TextRenderer.DrawText(g, _glyph, Theme.Icon(11f), new Rectangle(Theme.S(12), 0, Theme.S(22), Height), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Text, _selected ? Theme.BodySemi : Theme.Body,
            new Rectangle(Theme.S(44), 0, Width - Theme.S(48), Height), _selected ? p.Text : Theme.Blend(p.Text, p.Muted, 0.35f),
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(p.Accent, 1.5f);
            using var fp = Theme.Round(RectangleF.Inflate(r, -1, -1), Theme.S(9));
            g.DrawPath(pen, fp);
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Aviso flutuante (canto inferior direito)
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class Toast : Control, IThemed
{
    private readonly System.Windows.Forms.Timer _hide = new() { Interval = 3800 };
    private Tone _tone = Tone.Success;

    public Toast()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        Visible = false;
        Font = Theme.BodySemi;
        _hide.Tick += (_, _) => { _hide.Stop(); Visible = false; };
        Click += (_, _) => Visible = false;
    }

    public void Show(string text, Tone tone, Size client)
    {
        Text = text;
        _tone = tone;
        var size = TextRenderer.MeasureText(text, Font, new Size(Theme.S(420), int.MaxValue), TextFormatFlags.WordBreak);
        Size = new Size(size.Width + Theme.S(64), Math.Max(Theme.S(48), size.Height + Theme.S(26)));
        Reposition(client);
        BackColor = Theme.EffectiveBack(Parent);
        Visible = true;
        BringToFront();
        Invalidate();
        _hide.Stop();
        _hide.Start();
    }

    public void Reposition(Size client) =>
        Location = new Point(client.Width - Width - Theme.S(24), client.Height - Height - Theme.S(24));

    public void ApplyTheme() => Invalidate();

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.P.Bg);
        Theme.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.Round(r, Theme.S(12));
        using (var b = new SolidBrush(Theme.Dark ? Theme.P.SurfaceAlt : Color.FromArgb(17, 24, 39)))
            g.FillPath(b, path);
        var c = Theme.ForTone(_tone);
        var glyph = _tone == Tone.Danger ? Icons.Warning : _tone == Tone.Warning ? Icons.Info : Icons.Check;
        TextRenderer.DrawText(g, glyph, Theme.Icon(11f), new Rectangle(Theme.S(16), 0, Theme.S(22), Height), c,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(Theme.S(48), 0, Width - Theme.S(60), Height), Color.White,
            TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hide.Dispose();
        base.Dispose(disposing);
    }
}
