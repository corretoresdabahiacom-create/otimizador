using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;

namespace Otimizador.UI;

internal sealed record Palette(
    Color Bg, Color Sidebar, Color Surface, Color SurfaceAlt, Color Border, Color Hover,
    Color Text, Color Muted, Color Accent, Color AccentHover, Color AccentText, Color AccentSoft,
    Color Success, Color Warning, Color Danger, Color Gradient1, Color Gradient2);

/// <summary>Tema claro/escuro com paleta única para todo o app.</summary>
internal static class Theme
{
    private static readonly Palette Light = new(
        Bg: C("#F4F6FB"), Sidebar: C("#FFFFFF"), Surface: C("#FFFFFF"), SurfaceAlt: C("#F1F3F9"),
        Border: C("#E3E7EF"), Hover: C("#EEF1F8"),
        Text: C("#111827"), Muted: C("#667085"),
        Accent: C("#4F46E5"), AccentHover: C("#4338CA"), AccentText: Color.White, AccentSoft: C("#EEF0FF"),
        Success: C("#16A34A"), Warning: C("#D97706"), Danger: C("#DC2626"),
        Gradient1: C("#4F46E5"), Gradient2: C("#0EA5E9"));

    private static readonly Palette DarkP = new(
        Bg: C("#0E1117"), Sidebar: C("#141821"), Surface: C("#181C26"), SurfaceAlt: C("#212634"),
        Border: C("#2A3040"), Hover: C("#232938"),
        Text: C("#E6E9F0"), Muted: C("#98A1B3"),
        Accent: C("#6366F1"), AccentHover: C("#7C7FF5"), AccentText: Color.White, AccentSoft: C("#252A4D"),
        Success: C("#22C55E"), Warning: C("#F59E0B"), Danger: C("#F87171"),
        Gradient1: C("#6366F1"), Gradient2: C("#0EA5E9"));

    public static Palette P { get; private set; } = Light;
    public static bool Dark { get; private set; }
    public static event Action? Changed;

    public static void Apply(string mode)
    {
        Dark = mode == "dark" || (mode == "system" && SystemPrefersDark());
        P = Dark ? DarkP : Light;
        Changed?.Invoke();
    }

    public static bool SystemPrefersDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    // ── Escala (DPI do sistema) ───────────────────────────────────────────
    public static float Scale { get; private set; } = 1f;
    public static int S(int px) => (int)Math.Round(px * Scale);
    public static int S(float px) => (int)Math.Round(px * Scale);

    public static void InitScale()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        Scale = Math.Max(1f, g.DpiX / 96f);
    }

    // ── Fontes ────────────────────────────────────────────────────────────
    private const string Family = "Segoe UI";
    private const string FamilySemi = "Segoe UI Semibold";
    public static string IconFamily { get; private set; } = "Segoe MDL2 Assets";

    public static Font Body { get; private set; } = null!;
    public static Font BodySemi { get; private set; } = null!;
    public static Font Small { get; private set; } = null!;
    public static Font SmallSemi { get; private set; } = null!;
    public static Font H1 { get; private set; } = null!;
    public static Font H2 { get; private set; } = null!;
    public static Font H3 { get; private set; } = null!;
    public static Font Metric { get; private set; } = null!;
    public static Font Mono { get; private set; } = null!;
    private static readonly Dictionary<float, Font> IconFonts = new();

    public static void InitFonts()
    {
        using (var fonts = new InstalledFontCollection())
        {
            var names = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (names.Contains("Segoe Fluent Icons")) IconFamily = "Segoe Fluent Icons";
            Mono = new Font(names.Contains("Cascadia Mono") ? "Cascadia Mono" : "Consolas", 9f);
        }
        Body = new Font(Family, 9.75f);
        BodySemi = new Font(FamilySemi, 9.75f);
        Small = new Font(Family, 8.5f);
        SmallSemi = new Font(FamilySemi, 8.5f);
        H1 = new Font(FamilySemi, 18f);
        H2 = new Font(FamilySemi, 12.5f);
        H3 = new Font(FamilySemi, 10.5f);
        Metric = new Font(FamilySemi, 19f);
    }

    public static Font Icon(float size)
    {
        if (!IconFonts.TryGetValue(size, out var f))
            IconFonts[size] = f = new Font(IconFamily, size);
        return f;
    }

    // ── Desenho ───────────────────────────────────────────────────────────
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static Color ForTone(Tone t) => t switch
    {
        Tone.Success => P.Success,
        Tone.Warning => P.Warning,
        Tone.Danger => P.Danger,
        Tone.Accent => P.Accent,
        Tone.Muted => P.Muted,
        _ => P.Text,
    };

    /// <summary>Pinta o fundo "transparente" de um controle com a cor efetiva do pai.</summary>
    public static Color EffectiveBack(Control? c)
    {
        while (c != null)
        {
            if (c is IThemed t && t.SurfaceColor is Color sc) return sc;
            if (c.BackColor.A == 255) return c.BackColor;
            c = c.Parent;
        }
        return P.Bg;
    }

    private static Color C(string hex) => ColorTranslator.FromHtml(hex);
}

internal enum Tone { Normal, Muted, Accent, Success, Warning, Danger }

internal interface IThemed
{
    void ApplyTheme();
    /// <summary>Cor de fundo que filhos transparentes devem usar (null = BackColor).</summary>
    Color? SurfaceColor => null;
}
