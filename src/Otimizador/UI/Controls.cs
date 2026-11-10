using System.Collections;
using System.Drawing.Drawing2D;

namespace Otimizador.UI;

// ═════════════════════════════════════════════════════════════════════════════
// Texto
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class TextLabel : Label, IThemed, IFlexHeight
{
    private Tone _tone;
    public Tone Tone { get => _tone; set { _tone = value; ApplyTheme(); } }
    public Color? CustomColor { get; set; }

    public TextLabel(string text, Font font, Tone tone = Tone.Normal)
    {
        AutoSize = false;
        UseMnemonic = false;
        BackColor = Color.Transparent;
        Font = font;
        Text = text;
        _tone = tone;
        ApplyTheme();
    }

    public void ApplyTheme() => ForeColor = CustomColor ?? Theme.ForTone(_tone);

    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);


    public int MeasureHeight(int width) =>
        TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? " " : Text, Font, new Size(Math.Max(1, width - 2), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height + 1;

    /// <summary>Ajusta a largura ao texto (uso em linhas horizontais).</summary>
    public TextLabel FitWidth()
    {
        Width = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width + Theme.S(4);
        Height = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix).Height;
        return this;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        // Só recalcula o layout da página se a altura do texto mudou (evita trabalho a cada atualização de métrica).
        if (Parent != null && (Width <= 0 || MeasureHeight(Width) != Height)) Ui.Relayout(this);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Cartão com cantos arredondados
// ═════════════════════════════════════════════════════════════════════════════
internal enum CardStyle { Surface, Soft, Hero, Plain }

internal class Card : StackPanel, IThemed, IBackgroundPainter
{
    private CardStyle _style;
    public CardStyle Style { get => _style; set { _style = value; ApplyTheme(); } }
    public int Radius { get; set; } = Theme.S(14);

    public Card(CardStyle style = CardStyle.Surface)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Padding = new Padding(Theme.S(20));
        Gap = Theme.S(12);
        _style = style;
        ApplyTheme();
    }

    public Color Fill => _style switch
    {
        CardStyle.Soft => Theme.P.AccentSoft,
        CardStyle.Hero => Theme.P.Accent,
        CardStyle.Plain => Theme.EffectiveBack(Parent),
        _ => Theme.P.Surface,
    };

    Color? IThemed.SurfaceColor => _style == CardStyle.Plain ? null : Fill;

    public virtual void ApplyTheme()
    {
        BackColor = _style == CardStyle.Plain ? Color.Transparent : Fill;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) => PaintBackgroundTo(e.Graphics);

    public void PaintBackgroundTo(Graphics g)
    {
        Ui.PaintAncestorBackground(this, g);
        if (_style == CardStyle.Plain) return;
        var oldSmooth = g.SmoothingMode;
        Theme.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.Round(r, Radius);
        if (_style == CardStyle.Hero)
        {
            using var br = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), Theme.P.Gradient1, Theme.P.Gradient2, 20f);
            g.FillPath(br, path);
        }
        else
        {
            using var br = new SolidBrush(Fill);
            g.FillPath(br, path);
            using var pen = new Pen(_style == CardStyle.Soft ? Theme.Blend(Theme.P.AccentSoft, Theme.P.Accent, 0.15f) : Theme.P.Border);
            g.DrawPath(pen, path);
        }
        g.SmoothingMode = oldSmooth;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Botão moderno
// ═════════════════════════════════════════════════════════════════════════════
internal enum ButtonKind { Primary, Secondary, Ghost, Danger, OnHero }

internal sealed class UiButton : Button, IThemed
{
    private bool _hover, _down;
    private string? _glyph;
    public ButtonKind Kind { get; set; }
    public string? Glyph { get => _glyph; set { _glyph = value; FitWidth(); Invalidate(); } }
    public bool AutoWidth { get; set; } = true;

    public UiButton(string text, ButtonKind kind = ButtonKind.Secondary, string? glyph = null, bool large = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Opaque, false); // ButtonBase é "Opaque": sem isso o fundo não é pintado
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = large ? Theme.H3 : Theme.BodySemi;
        Kind = kind;
        _glyph = glyph;
        Text = text;
        Height = Theme.S(large ? 46 : 36);
        FitWidth();
    }

    public void ApplyTheme() => Invalidate();

    private void FitWidth()
    {
        if (!AutoWidth) return;
        int tw = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
        int gw = _glyph != null ? Theme.S(string.IsNullOrEmpty(Text) ? 16 : 24) : 0;
        Width = Math.Max(Height, tw + gw + Theme.S(string.IsNullOrEmpty(Text) ? 20 : 34));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        FitWidth();
        if (Parent != null) Parent.PerformLayout();
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var p = Theme.P;
        Color fill, text, border = Color.Empty;
        switch (Kind)
        {
            case ButtonKind.Primary:
                fill = _down ? Theme.Blend(p.AccentHover, Color.Black, 0.1f) : _hover ? p.AccentHover : p.Accent;
                text = p.AccentText;
                break;
            case ButtonKind.Danger:
                fill = _hover ? Theme.Blend(p.Danger, Color.Black, 0.12f) : p.Danger;
                text = Color.White;
                break;
            case ButtonKind.OnHero:
                fill = Color.FromArgb(_down ? 255 : _hover ? 245 : 235, 255, 255, 255);
                text = p.Gradient1;
                break;
            case ButtonKind.Ghost:
                fill = _hover ? p.Hover : Color.Transparent;
                text = p.Text;
                break;
            default:
                fill = _down ? p.Border : _hover ? p.Hover : p.Surface;
                text = p.Text;
                border = p.Border;
                break;
        }
        if (!Enabled)
        {
            fill = fill.A == 0 ? fill : Theme.Blend(fill, Theme.EffectiveBack(Parent), 0.55f);
            text = Theme.Blend(text, Theme.EffectiveBack(Parent), 0.5f);
        }

        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.Round(r, Theme.S(8)))
        {
            if (fill.A > 0) { using var b = new SolidBrush(fill); g.FillPath(b, path); }
            if (border != Color.Empty) { using var pen = new Pen(border); g.DrawPath(pen, path); }
            if (Focused && ShowFocusCues)
            {
                using var pen = new Pen(p.Accent, Theme.S(2)) { DashStyle = DashStyle.Solid };
                using var fp = Theme.Round(RectangleF.Inflate(r, -2, -2), Theme.S(7));
                g.DrawPath(pen, fp);
            }
        }

        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        int textW = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, flags).Width;
        int glyphW = _glyph != null ? Theme.S(16) : 0;
        int spacing = _glyph != null && textW > 0 ? Theme.S(8) : 0;
        int x = (Width - (textW + glyphW + spacing)) / 2;
        if (_glyph != null)
        {
            TextRenderer.DrawText(g, _glyph, Theme.Icon(10.5f), new Rectangle(x, 0, glyphW + Theme.S(2), Height), text,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            x += glyphW + spacing;
        }
        if (textW > 0)
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, textW + 2, Height), text, flags);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Interruptor (on/off) e caixa de seleção
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class ToggleSwitch : CheckBox, IThemed
{
    private bool _hover;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Opaque, false); // ButtonBase é "Opaque": sem isso o fundo não é pintado
        AutoSize = false;
        BackColor = Color.Transparent;
        Size = new Size(Theme.S(44), Theme.S(24));
        Cursor = Cursors.Hand;
        Text = "";
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);

    public void ApplyTheme() => Invalidate();
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var p = Theme.P;
        var bg = Theme.EffectiveBack(Parent);
        float h = Theme.S(22), w = Theme.S(42);
        var r = new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);
        Color track = Checked ? (_hover ? p.AccentHover : p.Accent) : (_hover ? p.Hover : p.SurfaceAlt);
        Color knob = Checked ? Color.White : p.Muted;
        if (!Enabled) { track = Theme.Blend(track, bg, 0.55f); knob = Theme.Blend(knob, bg, 0.5f); }
        using (var path = Theme.Round(r, h / 2))
        {
            using var b = new SolidBrush(track);
            g.FillPath(b, path);
            if (!Checked) { using var pen = new Pen(Theme.Blend(p.Muted, bg, 0.35f), 1.2f); g.DrawPath(pen, path); }
        }
        float d = Checked ? h - Theme.S(8) : h - Theme.S(10);
        float kx = Checked ? r.Right - d - Theme.S(4) : r.X + Theme.S(5);
        using (var kb = new SolidBrush(knob)) g.FillEllipse(kb, kx, r.Y + (h - d) / 2, d, d);
        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(p.Accent, 1.5f);
            using var fp = Theme.Round(RectangleF.Inflate(r, 2, 2), h / 2 + 2);
            g.DrawPath(pen, fp);
        }
    }
}

internal sealed class UiCheck : CheckBox, IThemed
{
    public UiCheck()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Opaque, false); // ButtonBase é "Opaque": sem isso o fundo não é pintado
        AutoSize = false;
        BackColor = Color.Transparent;
        Size = new Size(Theme.S(22), Theme.S(22));
        Cursor = Cursors.Hand;
        Text = "";
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);

    public void ApplyTheme() => Invalidate();
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var p = Theme.P;
        float s = Theme.S(18);
        var r = new RectangleF((Width - s) / 2f, (Height - s) / 2f, s, s);
        var bg = Theme.EffectiveBack(Parent);
        using var path = Theme.Round(r, Theme.S(5));
        if (Checked)
        {
            using var b = new SolidBrush(Enabled ? p.Accent : Theme.Blend(p.Accent, bg, 0.6f));
            g.FillPath(b, path);
            TextRenderer.DrawText(g, Icons.Check, Theme.Icon(8.5f), Rectangle.Round(r), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        else
        {
            using var pen = new Pen(Enabled ? p.Muted : Theme.Blend(p.Muted, bg, 0.6f), 1.4f);
            g.DrawPath(pen, path);
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Barra de progresso fina, anel de pontuação, selo e ícone
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class MetricBar : Control, IThemed
{
    private double _value;
    private bool _indeterminate;
    private float _phase;
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 30 };
    public Tone Tone { get; set; } = Tone.Accent;

    public double Value { get => _value; set { _value = Math.Clamp(value, 0, 100); Invalidate(); } }

    public bool Indeterminate
    {
        get => _indeterminate;
        set { _indeterminate = value; UpdateTimer(); Invalidate(); }
    }

    public MetricBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = Theme.S(6);
        _anim.Tick += (_, _) => { _phase = (_phase + 0.018f) % 1.4f; Invalidate(); };
    }

    private void UpdateTimer() => _anim.Enabled = _indeterminate && Visible && IsHandleCreated;
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateTimer(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateTimer(); }
    public void ApplyTheme() => Invalidate();

    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var track = Theme.Round(r, r.Height / 2))
        using (var b = new SolidBrush(Theme.P.SurfaceAlt == Theme.EffectiveBack(Parent) ? Theme.P.Border : Theme.P.SurfaceAlt))
            g.FillPath(b, track);

        RectangleF fill;
        if (_indeterminate)
        {
            float w = r.Width * 0.35f, x = (r.Width + w) * _phase - w;
            fill = RectangleF.Intersect(r, new RectangleF(x, 0, w, r.Height));
        }
        else fill = new RectangleF(0, 0, (float)(r.Width * _value / 100.0), r.Height);
        if (fill.Width < 1) return;
        using var fp = Theme.Round(fill, r.Height / 2);
        using var fb = new SolidBrush(Theme.ForTone(Tone));
        g.FillPath(fb, fp);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _anim.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class ScoreRing : Control, IThemed
{
    private int _value = -1;
    public int Value { get => _value; set { _value = value; Invalidate(); } }
    public string Caption { get; set; } = "de 100";

    public ScoreRing()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(Theme.S(132), Theme.S(132));
    }

    public void ApplyTheme() => Invalidate();

    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);

    public static Tone ToneFor(int v) => v < 0 ? Tone.Muted : v >= 80 ? Tone.Success : v >= 55 ? Tone.Warning : Tone.Danger;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        float thick = Theme.S(11);
        float size = Math.Min(Width, Height) - thick - 2;
        var r = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);
        using (var track = new Pen(Theme.P.SurfaceAlt, thick))
            g.DrawEllipse(track, r);
        if (_value > 0)
        {
            using var pen = new Pen(Theme.ForTone(ToneFor(_value)), thick) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, r, -90, 360f * Math.Min(100, _value) / 100f);
        }
        var text = _value < 0 ? "—" : _value.ToString();
        var center = new Rectangle(0, 0, Width, Height - Theme.S(14));
        TextRenderer.DrawText(g, text, Theme.Metric, center, Theme.P.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Caption, Theme.Small, new Rectangle(0, Height / 2 + Theme.S(12), Width, Theme.S(18)), Theme.P.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
    }
}

/// <summary>Selo/etiqueta ("Admin", "Ativo", "Requer reinício").</summary>
internal sealed class Pill : Control, IThemed
{
    private Tone _tone;
    public Tone Tone { get => _tone; set { _tone = value; Invalidate(); } }

    public Pill(string text, Tone tone)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Theme.SmallSemi;
        _tone = tone;
        Text = text;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Size = new Size(TextRenderer.MeasureText(Text, Font).Width + Theme.S(16), Theme.S(22));
        Invalidate();
        Parent?.PerformLayout();
    }

    public void ApplyTheme() => Invalidate();

    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var c = Theme.ForTone(_tone);
        var bg = Theme.Blend(Theme.EffectiveBack(Parent), c, Theme.Dark ? 0.22f : 0.12f);
        using var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f);
        using var b = new SolidBrush(bg);
        g.FillPath(b, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, c,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Ícone dentro de um quadrado arredondado colorido.</summary>
internal sealed class IconBadge : Control, IThemed
{
    private string _glyph;
    private Tone _tone;
    public string Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }
    public Tone Tone { get => _tone; set { _tone = value; Invalidate(); } }
    public bool OnHero { get; set; }

    public IconBadge(string glyph, Tone tone = Tone.Accent, int size = 38)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        _glyph = glyph;
        _tone = tone;
        Size = new Size(Theme.S(size), Theme.S(size));
    }

    public void ApplyTheme() => Invalidate();

    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var c = OnHero ? Color.White : Theme.ForTone(_tone);
        var bg = OnHero ? Color.FromArgb(50, 255, 255, 255) : Theme.Blend(Theme.EffectiveBack(Parent), c, Theme.Dark ? 0.2f : 0.11f);
        using var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(10));
        using var b = new SolidBrush(bg);
        g.FillPath(b, path);
        TextRenderer.DrawText(g, _glyph, Theme.Icon(Height / Theme.Scale * 0.33f), ClientRectangle, c,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Linha de configuração: ícone + título + descrição + controle à direita
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class SettingRow : Panel, IFlexHeight, IThemed
{
    private readonly IconBadge _icon;
    private readonly TextLabel _title, _desc;
    private readonly List<Control> _right = new();
    private readonly PillsPanel _pills = new() { AutoSize = false, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty, Padding = Padding.Empty };
    public bool ShowDivider { get; set; }
    public TextLabel TitleLabel => _title;
    public TextLabel DescriptionLabel => _desc;
    public IconBadge Icon => _icon;

    public SettingRow(string glyph, string title, string description, Tone tone = Tone.Accent, params Control[] right)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        _icon = new IconBadge(glyph, tone, 36);
        _title = new TextLabel(title, Theme.BodySemi);
        _desc = new TextLabel(description, Theme.Small, Tone.Muted);
        Controls.AddRange(new Control[] { _icon, _title, _pills, _desc });
        foreach (var c in right) AddRight(c);
    }

    public SettingRow AddRight(Control c) { _right.Add(c); Controls.Add(c); PerformLayout(); return this; }

    public SettingRow AddPill(Pill p) { p.Margin = new Padding(Theme.S(8), 0, 0, 0); _pills.Controls.Add(p); PerformLayout(); return this; }

    public void ClearPills() { foreach (Control c in _pills.Controls.Cast<Control>().ToList()) c.Dispose(); PerformLayout(); }

    private int RightWidth() => _right.Where(c => c.Visible).Sum(c => c.Width + Theme.S(10));
    private int TextX => _icon.Width + Theme.S(14);
    private int TextWidth(int w) => Math.Max(Theme.S(80), w - TextX - RightWidth() - Theme.S(8));

    public int MeasureHeight(int width)
    {
        if (_icon == null) return 0;
        int tw = TextWidth(width);
        int titleH = Math.Max(_title.MeasureHeight(tw - _pills.PreferredSize.Width), _pills.Controls.Count > 0 ? Theme.S(22) : 0);
        int descH = string.IsNullOrEmpty(_desc.Text) ? 0 : _desc.MeasureHeight(tw) + Theme.S(2);
        int pad = Theme.S(14);
        return Math.Max(_icon.Height, titleH + descH) + pad * 2;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (_icon == null) return; // ainda no construtor
        int w = ClientSize.Width, h = ClientSize.Height;
        int tw = TextWidth(w);
        int pillsW = _pills.Controls.Count > 0 ? _pills.PreferredSize.Width : 0;
        int titleW = Math.Min(tw - pillsW, TextRenderer.MeasureText(_title.Text, _title.Font, new Size(tw - pillsW, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Width + Theme.S(4));
        int titleH = _title.MeasureHeight(Math.Max(Theme.S(40), titleW));
        int descH = string.IsNullOrEmpty(_desc.Text) ? 0 : _desc.MeasureHeight(tw);
        int contentH = Math.Max(titleH, pillsW > 0 ? Theme.S(22) : 0) + (descH > 0 ? descH + Theme.S(2) : 0);
        int top = (h - Math.Max(_icon.Height, contentH)) / 2;

        _icon.Location = new Point(0, (h - _icon.Height) / 2);
        int ty = top + Math.Max(0, (_icon.Height - contentH) / 2);
        _title.SetBounds(TextX, ty, Math.Max(Theme.S(40), titleW), titleH);
        _pills.SetBounds(TextX + titleW, ty + (titleH - Theme.S(22)) / 2, Math.Max(1, pillsW), Theme.S(22));
        _pills.Visible = pillsW > 0;
        _desc.SetBounds(TextX, ty + Math.Max(titleH, pillsW > 0 ? Theme.S(22) : 0) + Theme.S(2), tw, descH);
        _desc.Visible = descH > 0;

        int rx = w;
        for (int i = _right.Count - 1; i >= 0; i--)
        {
            var c = _right[i];
            if (!c.Visible) continue;
            rx -= c.Width;
            c.Location = new Point(rx, (h - c.Height) / 2);
            rx -= Theme.S(10);
        }
    }

    public void ApplyTheme() => Invalidate();

    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!ShowDivider) return;
        using var pen = new Pen(Theme.P.Border);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);
    }
}

internal sealed class PillsPanel : FlowLayoutPanel
{
    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);
}

// ═════════════════════════════════════════════════════════════════════════════
// Campo de texto, combo, log e lista
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class TextField : Panel, IThemed
{
    public TextBox Box { get; } = new() { BorderStyle = BorderStyle.None };
    private readonly string? _glyph;

    public TextField(string placeholder = "", string? glyph = null, int width = 280)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        _glyph = glyph;
        Size = new Size(Theme.S(width), Theme.S(36));
        Box.Font = Theme.Body;
        Box.PlaceholderText = placeholder;
        Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Cursor = Cursors.IBeam;
        Click += (_, _) => Box.Focus();
        ApplyTheme();
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text { get => Box.Text; set => Box.Text = value ?? ""; }

    public void ApplyTheme()
    {
        Box.BackColor = Theme.P.SurfaceAlt;
        Box.ForeColor = Theme.P.Text;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Ui.PaintAncestorBackground(this, e.Graphics);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int left = Theme.S(_glyph != null ? 34 : 12);
        Box.SetBounds(left, (Height - Box.PreferredHeight) / 2, Width - left - Theme.S(10), Box.PreferredHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(8));
        using var b = new SolidBrush(Theme.P.SurfaceAlt);
        g.FillPath(b, path);
        using var pen = new Pen(Box.Focused ? Theme.P.Accent : Theme.P.Border, Box.Focused ? 1.6f : 1f);
        g.DrawPath(pen, path);
        if (_glyph != null)
            TextRenderer.DrawText(g, _glyph, Theme.Icon(10f), new Rectangle(Theme.S(10), 0, Theme.S(18), Height), Theme.P.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
    }
}

internal sealed class UiCombo : ComboBox, IThemed
{
    public UiCombo(int width = 200, params object[] items)
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        Font = Theme.Body;
        Width = Theme.S(width);
        Items.AddRange(items);
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        BackColor = Theme.P.SurfaceAlt;
        ForeColor = Theme.P.Text;
    }
}

internal sealed class LogView : RichTextBox, IThemed
{
    public LogView()
    {
        ReadOnly = true;
        BorderStyle = BorderStyle.None;
        DetectUrls = false;
        ScrollBars = RichTextBoxScrollBars.Vertical;
        Font = Theme.Mono;
        ShortcutsEnabled = true;
        Height = Theme.S(200);
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        BackColor = Theme.P.SurfaceAlt;
        ForeColor = Theme.P.Text;
    }

    public void AddLine(string text) => Add(text);

    /// <summary>Acrescenta uma linha (thread-safe). A cor segue o prefixo: ✓ sucesso, ✗ erro, ⚠ aviso.</summary>
    public void Add(string text, Tone? tone = null)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { Ui.OnUi(this, () => Add(text, tone)); return; }
        var t = tone ?? (text.StartsWith('✓') ? Tone.Success : text.StartsWith('✗') ? Tone.Danger : text.StartsWith('⚠') ? Tone.Warning : Tone.Normal);
        if (Lines.Length > 4000) { Select(0, GetFirstCharIndexFromLine(1000)); ReadOnly = false; SelectedText = ""; ReadOnly = true; }
        SelectionStart = TextLength;
        SelectionLength = 0;
        SelectionColor = Theme.ForTone(t);
        AppendText(text + Environment.NewLine);
        SelectionColor = ForeColor;
        ScrollToCaret();
    }
}

internal sealed class ThemedListView : ListView, IThemed
{
    private int _sortColumn = -1;
    private bool _sortAsc = true;

    public ThemedListView()
    {
        View = View.Details;
        FullRowSelect = true;
        HideSelection = false;
        BorderStyle = BorderStyle.None;
        DoubleBuffered = true;
        OwnerDraw = true;
        HeaderStyle = ColumnHeaderStyle.Clickable;
        Font = Theme.Body;
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        BackColor = Theme.P.Surface;
        ForeColor = Theme.P.Text;
        if (IsHandleCreated) Native.SetWindowTheme(Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetWindowTheme(Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
    }

    /// <summary>Ordena ao clicar no cabeçalho. Use SubItem.Tag (IComparable) para ordenar números/datas.</summary>
    public void EnableSorting()
    {
        ColumnClick += (_, e) =>
        {
            _sortAsc = e.Column == _sortColumn ? !_sortAsc : true;
            _sortColumn = e.Column;
            ListViewItemSorter = new Sorter(e.Column, _sortAsc);
        };
    }

    private sealed class Sorter(int col, bool asc) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = (ListViewItem)x!; var b = (ListViewItem)y!;
            var sa = col < a.SubItems.Count ? a.SubItems[col] : null;
            var sb = col < b.SubItems.Count ? b.SubItems[col] : null;
            int r = sa?.Tag is IComparable ca && sb?.Tag is IComparable cb && ca.GetType() == cb.GetType()
                ? ca.CompareTo(cb)
                : string.Compare(sa?.Text, sb?.Text, StringComparison.CurrentCultureIgnoreCase);
            return asc ? r : -r;
        }
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        var g = e.Graphics;
        using (var b = new SolidBrush(Theme.P.SurfaceAlt)) g.FillRectangle(b, e.Bounds);
        using (var pen = new Pen(Theme.P.Border)) g.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        var text = e.Header?.Text ?? "";
        if (e.ColumnIndex == _sortColumn) text += _sortAsc ? "  ▲" : "  ▼";
        var r = Rectangle.FromLTRB(e.Bounds.Left + Theme.S(8), e.Bounds.Top, e.Bounds.Right - Theme.S(4), e.Bounds.Bottom);
        TextRenderer.DrawText(g, text, Theme.SmallSemi, r, Theme.P.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e) => e.DrawDefault = CheckBoxes;

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (CheckBoxes || e.Item == null || e.SubItem == null) { e.DrawDefault = true; return; }
        var g = e.Graphics;
        bool sel = e.Item.Selected;
        var bg = sel ? Theme.P.AccentSoft : Theme.P.Surface;
        using (var b = new SolidBrush(bg)) g.FillRectangle(b, e.Bounds);
        var color = e.Item.UseItemStyleForSubItems ? e.Item.ForeColor : e.SubItem.ForeColor;
        if (color == ForeColor || color.IsEmpty || color == SystemColors.WindowText) color = Theme.P.Text;
        var r = Rectangle.FromLTRB(e.Bounds.Left + Theme.S(8), e.Bounds.Top, e.Bounds.Right - Theme.S(4), e.Bounds.Bottom);
        TextRenderer.DrawText(g, e.SubItem.Text, e.ColumnIndex == 0 ? Theme.BodySemi : Theme.Body, r, color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        if (e.ColumnIndex == 0)
        {
            using var pen = new Pen(Theme.P.Border);
            g.DrawLine(pen, 0, e.Bounds.Bottom - 1, Width, e.Bounds.Bottom - 1);
        }
    }
}

/// <summary>Cartão que contém uma lista (ocupa a altura restante da página).</summary>
internal sealed class ListCard : Card
{
    public ThemedListView List { get; } = new();

    public ListCard()
    {
        Padding = new Padding(Theme.S(1), Theme.S(8), Theme.S(1), Theme.S(8));
        FillLast = true;
        Controls.Add(List);
    }
}
