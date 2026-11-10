namespace Otimizador.UI;

/// <summary>Controle cuja altura depende da largura disponível (texto quebrado, grades...).</summary>
internal interface IFlexHeight
{
    int MeasureHeight(int width);
}

/// <summary>Controle que sabe pintar o próprio fundo (cartões arredondados, gradientes).</summary>
internal interface IBackgroundPainter
{
    void PaintBackgroundTo(Graphics g);
}

internal static class Ui
{
    /// <summary>
    /// Pinta, no controle, o fundo do primeiro ancestral opaco ou "pintor" (Card). Substitui a
    /// transparência do WinForms, que falha em cadeias de painéis transparentes.
    /// </summary>
    public static void PaintAncestorBackground(Control c, Graphics g)
    {
        int dx = 0, dy = 0;
        var cur = c;
        for (var p = c.Parent; p != null; cur = p, p = p.Parent)
        {
            dx += cur.Left;
            dy += cur.Top;
            if (p is IBackgroundPainter bp)
            {
                var state = g.Save();
                g.TranslateTransform(-dx, -dy);
                bp.PaintBackgroundTo(g);
                g.Restore(state);
                return;
            }
            if (p.BackColor.A == 255)
            {
                g.Clear(p.BackColor);
                return;
            }
        }
        g.Clear(Theme.P.Bg);
    }

    public static int S(int px) => Theme.S(px);

    private static readonly ToolTip SharedTip = new() { InitialDelay = 400 };
    public static void Tip(Control c, string text) => SharedTip.SetToolTip(c, text);

    public static int HeightOf(Control c, int width) => c is IFlexHeight f ? f.MeasureHeight(width) : c.Height;

    /// <summary>Recalcula o layout da página que contém o controle (após mudar textos/visibilidade).</summary>
    public static void Relayout(Control c)
    {
        // De baixo para cima: cada container reposiciona os filhos; no fim a página recalcula a rolagem.
        for (var p = c.Parent; p != null; p = p.Parent)
        {
            p.PerformLayout();
            if (p is Page) return;
        }
    }

    /// <summary>Reaplica o tema em toda a árvore de controles.</summary>
    public static void ApplyThemeTree(Control root)
    {
        if (root is IThemed t) t.ApplyTheme();
        foreach (Control c in root.Controls) ApplyThemeTree(c);
        root.Invalidate();
    }

    public static void OnUi(Control c, Action a)
    {
        if (c.IsDisposed) return;
        try
        {
            if (c.InvokeRequired) c.BeginInvoke(a);
            else a();
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
}

/// <summary>Empilha os filhos verticalmente, ocupando toda a largura.</summary>
internal class StackPanel : Panel, IFlexHeight
{
    public int Gap { get; set; } = Theme.S(12);
    /// <summary>O último filho ocupa a altura restante (páginas com listas).</summary>
    public bool FillLast { get; set; }

    public StackPanel()
    {
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    private IEnumerable<Control> VisibleChildren() => Controls.Cast<Control>().Where(c => c.Visible);

    public virtual int MeasureHeight(int width)
    {
        int inner = Math.Max(1, width - Padding.Horizontal);
        int h = Padding.Vertical, n = 0;
        foreach (var c in VisibleChildren())
        {
            if (n++ > 0) h += Gap;
            h += Ui.HeightOf(c, inner);
        }
        return h;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int w = Math.Max(1, ClientSize.Width - Padding.Horizontal);
        int y = Padding.Top;
        var list = VisibleChildren().ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            int h = Ui.HeightOf(c, w);
            if (FillLast && i == list.Count - 1)
                h = Math.Max(Theme.S(160), ClientSize.Height - Padding.Bottom - y);
            c.SetBounds(Padding.Left, y, w, h);
            y += h + Gap;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);

    protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); e.Control!.VisibleChanged += ChildVisibleChanged; }
    protected override void OnControlRemoved(ControlEventArgs e) { base.OnControlRemoved(e); e.Control!.VisibleChanged -= ChildVisibleChanged; }
    private void ChildVisibleChanged(object? sender, EventArgs e) => Ui.Relayout(this);
}

/// <summary>Linha horizontal: itens à esquerda, um item "Fill" no meio e itens à direita.</summary>
internal sealed class Row : Panel, IFlexHeight
{
    private readonly List<Control> _right = new();
    public int Gap { get; set; } = Theme.S(10);
    public Control? Fill { get; set; }
    public int FixedHeight { get; set; }

    public Row(params Control[] left)
    {
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        foreach (var c in left) Controls.Add(c);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);

    public Row AddRight(params Control[] cs)
    {
        foreach (var c in cs) { _right.Add(c); Controls.Add(c); }
        return this;
    }

    public Row SetFill(Control c)
    {
        if (!Controls.Contains(c)) Controls.Add(c);
        Fill = c;
        return this;
    }

    private int FillWidth(int width)
    {
        int used = 0, n = 0;
        foreach (Control c in Controls)
        {
            if (!c.Visible || c == Fill) continue;
            used += c.Width; n++;
        }
        return Math.Max(Theme.S(40), width - used - Gap * n);
    }

    public int MeasureHeight(int width)
    {
        if (FixedHeight > 0) return FixedHeight;
        int h = 0;
        foreach (Control c in Controls)
            if (c.Visible) h = Math.Max(h, c == Fill ? Ui.HeightOf(c, FillWidth(width)) : c.Height);
        return h;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int H = ClientSize.Height, W = ClientSize.Width;
        int x = 0;
        int fillW = Fill != null && Fill.Visible ? FillWidth(W) : 0;
        foreach (Control c in Controls)
        {
            if (!c.Visible || _right.Contains(c)) continue;
            int w = c == Fill ? fillW : c.Width;
            int h = c == Fill ? Math.Min(H, Ui.HeightOf(c, w)) : Math.Min(H, c.Height);
            c.SetBounds(x, (H - h) / 2, w, h);
            x += w + Gap;
        }
        int rx = W;
        for (int i = _right.Count - 1; i >= 0; i--)
        {
            var c = _right[i];
            if (!c.Visible) continue;
            rx -= c.Width;
            c.SetBounds(rx, (H - Math.Min(H, c.Height)) / 2, c.Width, Math.Min(H, c.Height));
            rx -= Gap;
        }
    }
}

/// <summary>Grade responsiva: número de colunas conforme a largura.</summary>
internal sealed class Grid : Panel, IFlexHeight
{
    public int MinColumnWidth { get; set; } = Theme.S(220);
    public int MaxColumns { get; set; } = 4;
    public int Gap { get; set; } = Theme.S(14);

    public Grid()
    {
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);

    private int Columns(int w) => Math.Clamp((w + Gap) / (MinColumnWidth + Gap), 1, MaxColumns);

    private IEnumerable<List<Control>> Rows(int cols)
    {
        var vis = Controls.Cast<Control>().Where(c => c.Visible).ToList();
        for (int i = 0; i < vis.Count; i += cols) yield return vis.Skip(i).Take(cols).ToList();
    }

    public int MeasureHeight(int width)
    {
        int cols = Columns(width), colW = (width - Gap * (cols - 1)) / cols, h = 0, n = 0;
        foreach (var row in Rows(cols))
        {
            if (n++ > 0) h += Gap;
            h += row.Max(c => Ui.HeightOf(c, colW));
        }
        return h;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int width = ClientSize.Width;
        int cols = Columns(width), colW = (width - Gap * (cols - 1)) / cols, y = 0;
        foreach (var row in Rows(cols))
        {
            int rh = row.Max(c => Ui.HeightOf(c, colW));
            for (int i = 0; i < row.Count; i++)
                row[i].SetBounds(i * (colW + Gap), y, colW, rh);
            y += rh + Gap;
        }
    }
}

/// <summary>Página rolável com cabeçalho (título, subtítulo e ações à direita).</summary>
internal abstract class Page : Panel, IThemed
{
    public StackPanel Content { get; } = new() { Gap = Theme.S(18) };
    public Row Header { get; }
    protected TextLabel TitleLabel { get; }
    protected TextLabel SubtitleLabel { get; }
    public string Title => TitleLabel.Text;
    public bool FillMode { get; }

    protected Page(string title, string subtitle, bool fillMode = false)
    {
        FillMode = fillMode;
        DoubleBuffered = true;
        AutoScroll = !fillMode;
        TitleLabel = new TextLabel(title, Theme.H1);
        SubtitleLabel = new TextLabel(subtitle, Theme.Body, Tone.Muted);
        var titles = new StackPanel { Gap = Theme.S(2) };
        titles.Controls.Add(TitleLabel);
        titles.Controls.Add(SubtitleLabel);
        Header = new Row();
        Header.SetFill(titles);
        Content.Controls.Add(Header);
        Content.FillLast = fillMode;
        Controls.Add(Content);
        ApplyTheme();
    }

    public virtual void ApplyTheme() => BackColor = Theme.P.Bg;

    /// <summary>Rola a página pela roda do mouse (delta em unidades de 120).</summary>
    public void ScrollByWheel(int delta)
    {
        if (!AutoScroll) return;
        int y = -AutoScrollPosition.Y - delta / 120 * Theme.S(64);
        AutoScrollPosition = new Point(0, Math.Max(0, y));
    }

    /// <summary>Chamado sempre que a página fica visível.</summary>
    public virtual void OnShown() { }
    public virtual void OnHidden() { }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int padX = Theme.S(32), padTop = Theme.S(26), padBottom = Theme.S(28);
        int w = Math.Max(Theme.S(300), ClientSize.Width - padX * 2);
        if (FillMode)
        {
            Content.SetBounds(padX, padTop, w, Math.Max(Theme.S(200), ClientSize.Height - padTop - padBottom));
        }
        else
        {
            int h = Content.MeasureHeight(w);
            var min = new Size(0, h + padTop + padBottom);
            if (AutoScrollMinSize != min) AutoScrollMinSize = min;
            Content.SetBounds(padX + AutoScrollPosition.X, padTop + AutoScrollPosition.Y, w, h);
        }
        Content.PerformLayout();
        base.OnLayout(levent); // ScrollableControl atualiza as barras de rolagem aqui
    }
}

/// <summary>
/// Envia a roda do mouse para o que está sob o cursor: listas e caixas de texto rolam a si mesmas,
/// o resto rola a página (assim a rolagem funciona mesmo com o foco em um botão).
/// </summary>
internal sealed class WheelRouter : IMessageFilter
{
    private const int WM_MOUSEWHEEL = 0x020A;

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL) return false;
        var target = Control.FromChildHandle(Native.WindowFromPoint(Cursor.Position));
        for (var c = target; c != null; c = c.Parent)
        {
            bool selfScroll = c is ListView or TextBoxBase ||
                              (c is ComboBox or NumericUpDown or DateTimePicker && c.Focused);
            if (selfScroll)
            {
                if (c.Handle == m.HWnd) return false;
                Native.SendMessage(c.Handle, m.Msg, m.WParam, m.LParam);
                return true;
            }
            if (c is Page page && page.AutoScroll)
            {
                page.ScrollByWheel((short)((long)m.WParam >> 16));
                return true;
            }
        }
        return false;
    }
}
