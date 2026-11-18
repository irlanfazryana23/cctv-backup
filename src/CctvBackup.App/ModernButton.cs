using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;

namespace CctvBackup.App;

/// <summary>
/// Flat rounded button drawn completely by itself (not a native Button), so no system border, focus box or
/// dark corner can show through. Transparent background: the rounded corners show whatever is behind it.
/// Works as an AcceptButton / CancelButton (IButtonControl) and supports Alt+letter shortcuts ("&amp;Save").
/// </summary>
internal sealed class ModernButton : Control, IButtonControl
{
    private Color _normalBack = Color.FromArgb(241, 245, 249);
    private Color _hoverBack = Color.FromArgb(226, 232, 240);
    private Color _borderColor = Color.FromArgb(203, 213, 225);
    private int _borderRadius = 6;
    private bool _hovered, _pressed, _isDefault;
    private DialogResult _dialogResult;

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick
                 | ControlStyles.StandardDoubleClick | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Ui.Font(9f, FontStyle.Bold);
        ForeColor = Color.FromArgb(15, 23, 42);
        Cursor = Cursors.Hand;
        Size = new Size(90, 32);
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
    }

    // ---- look ----

    /// <summary>Fill colour in the normal state.</summary>
    public Color NormalColor { get => _normalBack; set { _normalBack = value; Invalidate(); } }
    public Color HoverColor { get => _hoverBack; set { _hoverBack = value; Invalidate(); } }
    public Color BorderColor { get => _borderColor; set { _borderColor = value; Invalidate(); } }
    public Color DisabledColor { get; set; } = Color.FromArgb(241, 245, 249);
    public Color DisabledForeColor { get; set; } = Color.FromArgb(148, 163, 184);
    public Color DisabledBorderColor { get; set; } = Color.FromArgb(226, 232, 240);
    public int BorderRadius { get => _borderRadius; set { _borderRadius = value; Invalidate(); } }

    // ---- button behaviour ----

    public DialogResult DialogResult { get => _dialogResult; set => _dialogResult = value; }

    public void NotifyDefault(bool value)
    {
        _isDefault = value;
        Invalidate();
    }

    public void PerformClick()
    {
        if (Enabled && Visible) OnClick(EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e)
    {
        // Same as a normal button: close a dialog when a DialogResult is set.
        if (FindForm() is { Modal: true } form && _dialogResult != DialogResult.None)
            form.DialogResult = _dialogResult;
        base.OnClick(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Enter && _isDefault || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space) { _pressed = true; Invalidate(); e.Handled = true; }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space && _pressed)
        {
            _pressed = false;
            Invalidate();
            PerformClick();
            e.Handled = true;
        }
    }

    protected override bool ProcessMnemonic(char charCode)
    {
        if (UseMnemonic && CanSelect && IsMnemonic(charCode, Text))
        {
            Focus();
            PerformClick();
            return true;
        }
        return base.ProcessMnemonic(charCode);
    }

    public bool UseMnemonic { get; set; } = true;

    // ---- mouse / state ----

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; _pressed = false; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { _pressed = true; Focus(); Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _pressed = false; Invalidate(); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (AutoSize) { PerformLayout(); } Invalidate(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Invalidate(); }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        _hovered = _pressed = false;
        Invalidate();
    }

    // ---- size ----

    public override bool AutoSize
    {
        get => base.AutoSize;
        set { base.AutoSize = value; if (value) PerformLayout(); }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&"), Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        int w = text.Width + Ui.S(this, 28);
        int h = text.Height + Ui.S(this, 14);
        return new Size(Math.Max(w, MinimumSize.Width), Math.Max(h, MinimumSize.Height));
    }

    // ---- painting ----

    private bool HasMnemonic => UseMnemonic && Regex.IsMatch(Text, @"&[\p{L}\p{N}]");

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width < 2 || rect.Height < 2) return;

        Color fill = !Enabled ? DisabledColor
            : _pressed ? ControlPaint.Dark(_hoverBack, 0.06f)
            : _hovered ? _hoverBack
            : _normalBack;
        Color stroke = !Enabled ? DisabledBorderColor : _borderColor;

        int radius = Math.Min(Ui.S(this, _borderRadius), Math.Min(rect.Width, rect.Height) / 2);
        using var path = Ui.RoundedRect(rect, radius);
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        if (stroke != Color.Transparent && stroke.A > 0)
        {
            using var pen = new Pen(stroke, 1);
            g.DrawPath(pen, path);
        }

        // keyboard focus: a soft ring just inside the edge
        if (Focused && ShowFocusCues && Enabled)
        {
            var inner = Rectangle.Inflate(rect, -Ui.S(this, 3), -Ui.S(this, 3));
            using var ringPath = Ui.RoundedRect(inner, Math.Max(1, radius - Ui.S(this, 2)));
            using var ring = new Pen(Color.FromArgb(140, 37, 99, 235), Math.Max(1.5f, Ui.S(this, 2) * 0.75f));
            g.DrawPath(ring, ringPath);
        }

        Color textColor = !Enabled ? DisabledForeColor : ForeColor;
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        if (!HasMnemonic) flags |= TextFormatFlags.NoPrefix;
        else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(g, Text, Font, Rectangle.Inflate(ClientRectangle, -Ui.S(this, 8), 0), textColor, flags);
    }
}
