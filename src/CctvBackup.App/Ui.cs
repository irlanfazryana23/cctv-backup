using System.Drawing.Drawing2D;

namespace CctvBackup.App;

/// <summary>Shared look of the Windows edition: colours, cached fonts, DPI helpers and button styles.</summary>
internal static class Ui
{
    public static readonly Color Ink = Color.FromArgb(15, 23, 42);
    public static readonly Color Text = Color.FromArgb(51, 65, 85);
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);
    public static readonly Color Faint = Color.FromArgb(148, 163, 184);
    public static readonly Color Line = Color.FromArgb(226, 232, 240);
    public static readonly Color LineStrong = Color.FromArgb(203, 213, 225);
    public static readonly Color Surface = Color.FromArgb(248, 250, 252);
    public static readonly Color Soft = Color.FromArgb(241, 245, 249);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentDark = Color.FromArgb(29, 78, 216);
    public static readonly Color AccentSoft = Color.FromArgb(224, 242, 254);
    public static readonly Color Danger = Color.FromArgb(220, 38, 38);

    private static readonly Dictionary<(float, FontStyle), Font> Fonts = new();

    /// <summary>A Segoe UI font that lives for the whole program (safe to use while painting).</summary>
    public static Font Font(float size, FontStyle style = FontStyle.Regular)
    {
        lock (Fonts)
        {
            if (!Fonts.TryGetValue((size, style), out var font))
                Fonts[(size, style)] = font = new Font("Segoe UI", size, style);
            return font;
        }
    }

    /// <summary>Scales a design pixel (at 96 dpi) to the screen the control is on.</summary>
    public static int S(Control c, int px) => (int)Math.Round(px * c.DeviceDpi / 96.0);

    /// <summary>Makes a window with a hand-made layout grow with the Windows display scale (125%, 150%...).</summary>
    public static void EnableScaling(Form form)
    {
        form.AutoScaleDimensions = new SizeF(96f, 96f);
        form.AutoScaleMode = AutoScaleMode.Dpi;
    }

    public static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void StyleSecondary(ModernButton b)
    {
        b.NormalColor = Color.White;
        b.HoverColor = Soft;
        b.ForeColor = Text;
        b.BorderColor = LineStrong;
        b.Font = Font(9f);
    }

    public static void StylePrimary(ModernButton b)
    {
        b.NormalColor = Accent;
        b.HoverColor = AccentDark;
        b.ForeColor = Color.White;
        b.BorderColor = Color.Transparent;
        b.DisabledColor = Line;
        b.DisabledForeColor = Muted;
        b.DisabledBorderColor = LineStrong;
        b.Font = Font(9.5f, FontStyle.Bold);
    }

    public static void StyleSuccess(ModernButton b)
    {
        StylePrimary(b);
        b.NormalColor = Color.FromArgb(22, 163, 74);
        b.HoverColor = Color.FromArgb(21, 128, 61);
    }

    public static void StyleDanger(ModernButton b)
    {
        b.NormalColor = Color.FromArgb(254, 242, 242);
        b.HoverColor = Color.FromArgb(254, 226, 226);
        b.ForeColor = Danger;
        b.BorderColor = Color.FromArgb(252, 165, 165);
        b.Font = Font(9.5f, FontStyle.Bold);
    }
}
