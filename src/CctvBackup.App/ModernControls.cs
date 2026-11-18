using System.Drawing.Drawing2D;

namespace CctvBackup.App;
/// <summary>
/// Modern smooth horizontal progress bar without legacy Windows XP/7 gradients.
/// </summary>
internal sealed class ModernProgressBar : Control
{
    private int _value = 0;
    private int _maximum = 100;
    private Color _progressColor = Color.FromArgb(37, 99, 235); // Blue-600

    public ModernProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.FromArgb(226, 232, 240); // Slate-200
        Height = 10;
    }

    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, _maximum);
            Invalidate();
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            if (value > 0) _maximum = value;
            Invalidate();
        }
    }

    public Color ProgressColor
    {
        get => _progressColor;
        set { _progressColor = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var parentBrush = new SolidBrush(Parent?.BackColor ?? Color.White))
            g.FillRectangle(parentBrush, ClientRectangle);

        var bounds = new Rectangle(0, 0, Width, Height);
        int radius = Math.Max(1, Height / 2);
        using (var trackPath = Ui.RoundedRect(bounds, radius))
        using (var bgBrush = new SolidBrush(BackColor))
        {
            g.FillPath(bgBrush, trackPath);
        }

        if (_value > 0 && _maximum > 0)
        {
            int fillWidth = Math.Max(Height, (int)((double)_value / _maximum * Width));
            var fillRect = new Rectangle(0, 0, Math.Min(fillWidth, Width), Height);
            using var fillPath = Ui.RoundedRect(fillRect, radius);
            using var barBrush = new SolidBrush(_progressColor);
            g.FillPath(barBrush, fillPath);
        }
    }
}

/// <summary>
/// Modern input control with rounded border, placeholder text, focus accent border, and clean padding.
/// </summary>
internal sealed class ModernTextBox : UserControl
{
    private readonly TextBox _inner = new();
    private Color _borderColor = Color.FromArgb(203, 213, 225); // Slate-300
    private Color _focusBorderColor = Color.FromArgb(37, 99, 235); // Blue-600
    private bool _isFocused;
    private int _borderRadius = 6;

    public ModernTextBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.White;
        Height = 36;

        _inner.BorderStyle = BorderStyle.None;
        _inner.Font = new Font("Segoe UI", 10f);
        _inner.ForeColor = Color.FromArgb(15, 23, 42);
        _inner.BackColor = Color.White;

        _inner.GotFocus += (_, _) => { _isFocused = true; Invalidate(); };
        _inner.LostFocus += (_, _) => { _isFocused = false; Invalidate(); };
        _inner.TextChanged += (_, e) => OnTextChanged(e);
        _inner.KeyDown += (_, e) => OnKeyDown(e);

        Controls.Add(_inner);
        LayoutInner();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutInner();
    }

    private void LayoutInner()
    {
        int sidePad = 10;
        int innerY = Math.Max(2, (Height - _inner.PreferredHeight) / 2);
        _inner.Location = new Point(sidePad, innerY);
        _inner.Width = Math.Max(10, Width - (sidePad * 2));
    }

    [System.ComponentModel.Browsable(true)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Visible)]
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _inner.Text;
        set => _inner.Text = value ?? string.Empty;
    }

    public string PlaceholderText
    {
        get => _inner.PlaceholderText;
        set => _inner.PlaceholderText = value;
    }

    public bool UseSystemPasswordChar
    {
        get => _inner.UseSystemPasswordChar;
        set => _inner.UseSystemPasswordChar = value;
    }

    public bool ReadOnly
    {
        get => _inner.ReadOnly;
        set
        {
            _inner.ReadOnly = value;
            _inner.BackColor = value ? Color.FromArgb(248, 250, 252) : Color.White;
            BackColor = _inner.BackColor;
            Invalidate();
        }
    }

    public HorizontalAlignment TextAlign
    {
        get => _inner.TextAlign;
        set => _inner.TextAlign = value;
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override Font Font
    {
        get => _inner.Font;
        set
        {
            if (value != null)
            {
                base.Font = value;
                _inner.Font = value;
                LayoutInner();
            }
        }
    }

    public int MaxLength
    {
        get => _inner.MaxLength;
        set => _inner.MaxLength = value;
    }

    public int BorderRadius
    {
        get => _borderRadius;
        set { _borderRadius = value; Invalidate(); }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set { _borderColor = value; Invalidate(); }
    }

    public TextBox InnerTextBox => _inner;

    public new void Focus() => _inner.Focus();
    public void SelectAll() => _inner.SelectAll();

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color parentBg = Parent?.BackColor ?? Color.FromArgb(248, 250, 252);
        using (var parentBrush = new SolidBrush(parentBg))
        {
            g.FillRectangle(parentBrush, ClientRectangle);
        }

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Color stroke = _isFocused ? _focusBorderColor : _borderColor;
        int penWidth = _isFocused ? 2 : 1;

        using var path = CreateRoundedRectangle(rect, _borderRadius);
        using var brush = new SolidBrush(BackColor);
        g.FillPath(brush, path);

        using var pen = new Pen(stroke, penWidth);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// Modern iOS/macOS/Fluent segmented switch control with smooth rounded pills.
/// </summary>
internal sealed class ModernSegmentControl : Control
{
    private string[] _items = Array.Empty<string>();
    private int _selectedIndex = 0;
    private int _hoverIndex = -1;

    public event EventHandler? SelectedIndexChanged;

    public ModernSegmentControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 38;
        Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        Cursor = Cursors.Hand;
    }

    public string[] Items
    {
        get => _items;
        set
        {
            _items = value ?? Array.Empty<string>();
            _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _items.Length - 1));
            Invalidate();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex != value && value >= 0 && value < _items.Length)
            {
                _selectedIndex = value;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_items.Length == 0) return;
        int segW = Width / _items.Length;
        int hover = Math.Clamp(e.X / segW, 0, _items.Length - 1);
        if (hover != _hoverIndex)
        {
            _hoverIndex = hover;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_items.Length == 0) return;
        int segW = Width / _items.Length;
        int clicked = Math.Clamp(e.X / segW, 0, _items.Length - 1);
        SelectedIndex = clicked;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        Color parentBg = Parent?.BackColor ?? Color.FromArgb(248, 250, 252);
        using (var parentBrush = new SolidBrush(parentBg))
        {
            g.FillRectangle(parentBrush, ClientRectangle);
        }

        if (_items.Length == 0) return;

        // Container Pill (radius 8, Slate-200)
        var outerRect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var outerPath = CreateRoundedRectangle(outerRect, 8))
        using (var bgBrush = new SolidBrush(Color.FromArgb(226, 232, 240)))
        {
            g.FillPath(bgBrush, outerPath);
        }

        int segWidth = (Width - 6) / _items.Length;
        for (int i = 0; i < _items.Length; i++)
        {
            var segRect = new Rectangle(3 + (i * segWidth), 3, segWidth, Height - 6);
            bool isSelected = (i == _selectedIndex);
            bool isHovered = (i == _hoverIndex && !isSelected);

            if (isSelected)
            {
                using var segPath = CreateRoundedRectangle(segRect, 6);
                using var cardBrush = new SolidBrush(Color.White);
                g.FillPath(cardBrush, segPath);

                using var borderPen = new Pen(Color.FromArgb(203, 213, 225), 1);
                g.DrawPath(borderPen, segPath);
            }
            else if (isHovered)
            {
                using var segPath = CreateRoundedRectangle(segRect, 6);
                using var hoverBrush = new SolidBrush(Color.FromArgb(241, 245, 249));
                g.FillPath(hoverBrush, segPath);
            }

            Color textColor = isSelected ? Color.FromArgb(15, 23, 42) : Color.FromArgb(100, 116, 139);
            Font textFont = isSelected ? new Font(Font, FontStyle.Bold) : new Font(Font, FontStyle.Regular);
            TextRenderer.DrawText(g, _items[i], textFont, segRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// Clean container card with subtle border and card header with numbered circle pill.
/// </summary>
internal sealed class ModernCard : Panel
{
    private readonly Panel _header = new() { Dock = DockStyle.Fill, Height = 34, BackColor = Color.Transparent };
    private readonly Panel _body = new() { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 8), BackColor = Color.Transparent };
    private readonly Label _numBadge = new() { Size = new Size(20, 20), Location = new Point(8, 7), Font = new Font("Segoe UI", 8f, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent };
    private readonly Label _titleLabel = new() { Location = new Point(34, 7), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42), BackColor = Color.Transparent, AutoEllipsis = true };
    private readonly FlowLayoutPanel _actions = new() { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 6, 8, 0), BackColor = Color.Transparent };

    public ModernCard(string stepNumber, string title)
    {
        BackColor = Color.White;
        Padding = new Padding(1);
        DoubleBuffered = true;

        _numBadge.Text = stepNumber;
        _numBadge.TextAlign = ContentAlignment.MiddleCenter;
        _numBadge.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(Color.FromArgb(37, 99, 235));
            e.Graphics.FillEllipse(b, 0, 0, _numBadge.Width - 1, _numBadge.Height - 1);
            TextRenderer.DrawText(e.Graphics, _numBadge.Text, _numBadge.Font, _numBadge.ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        _titleLabel.Text = title;

        _header.Controls.Add(_numBadge);
        _header.Controls.Add(_titleLabel);
        _header.Controls.Add(_actions);

        _header.Resize += (_, _) =>
        {
            int maxW = Math.Max(40, _header.Width - 36 - _actions.Width);
            _titleLabel.Size = new Size(maxW, 20);
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(_header, 0, 0);
        root.Controls.Add(_body, 0, 1);

        Controls.Add(root);
    }

    public string CardTitle
    {
        get => _titleLabel.Text;
        set
        {
            _titleLabel.Text = value;
            int maxW = Math.Max(40, _header.Width - 38 - _actions.Width);
            _titleLabel.Size = new Size(maxW, 20);
        }
    }

    public Panel Body => _body;
    public FlowLayoutPanel HeaderActions => _actions;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new Pen(Color.FromArgb(226, 232, 240), 1);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
