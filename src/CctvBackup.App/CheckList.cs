namespace CctvBackup.App;

/// <summary>
/// A list with its own check boxes, drawn in the look of the rest of the window (the standard CheckedListBox
/// ignores owner drawing). Rows can show a second line and a small badge on the right.
/// </summary>
internal sealed class CheckList : ListBox
{
    private readonly List<bool> _checked = new();
    private readonly Label _empty = new()
    {
        AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ui.Faint,
        BackColor = Color.White, Font = Ui.Font(9f), Visible = false,
    };
    private int _hover = -1;
    private int _rowHeight = 28;
    private string _emptyText = string.Empty;

    /// <summary>Raised before a box changes (like CheckedListBox); e.NewValue is the state it is about to get.</summary>
    public event ItemCheckEventHandler? ItemCheck;

    /// <summary>True: a click anywhere on the row ticks it. False: only a click on the box does (the row itself just selects).</summary>
    public bool CheckOnRowClick { get; set; }

    /// <summary>Second line of a row (small, grey). Optional.</summary>
    public Func<int, string?>? SubtitleOf { get; set; }

    /// <summary>Small badge at the right end of a row. Optional.</summary>
    public Func<int, string?>? BadgeOf { get; set; }

    /// <summary>Row height in pixels at 96 dpi.</summary>
    public int RowHeight
    {
        get => _rowHeight;
        set { _rowHeight = value; if (IsHandleCreated) ItemHeight = Ui.S(this, value); }
    }

    /// <summary>Text shown in the middle of the list while it has no rows.</summary>
    public string EmptyText
    {
        get => _emptyText;
        set { _emptyText = value ?? string.Empty; UpdateEmpty(); }
    }

    public CheckList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        IntegralHeight = false;
        BorderStyle = BorderStyle.None;
        BackColor = Color.White;
        DoubleBuffered = true;
        HorizontalScrollbar = false;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Controls.Add(_empty);
    }

    public IReadOnlyList<string> CheckedItems =>
        Items.Cast<object>().Where((_, i) => _checked[i]).Select(o => o.ToString() ?? string.Empty).ToList();

    public void AddItem(string text, bool isChecked)
    {
        Items.Add(text);
        _checked.Add(isChecked);
        UpdateEmpty();
        if (IsHandleCreated) Invalidate();
    }

    /// <summary>Removes every row (use this instead of Items.Clear so the ticks stay in step).</summary>
    public void Reset()
    {
        Items.Clear();
        _checked.Clear();
        _hover = -1;
        UpdateEmpty();
    }

    public bool GetItemChecked(int index) => index >= 0 && index < _checked.Count && _checked[index];

    public void SetItemChecked(int index, bool value)
    {
        if (index < 0 || index >= _checked.Count || _checked[index] == value) return;
        ItemCheck?.Invoke(this, new ItemCheckEventArgs(index, value ? CheckState.Checked : CheckState.Unchecked,
            _checked[index] ? CheckState.Checked : CheckState.Unchecked));
        _checked[index] = value;
        if (IsHandleCreated) Invalidate(GetItemRectangle(index));
    }

    private void UpdateEmpty()
    {
        _empty.Text = _emptyText;
        _empty.Visible = Items.Count == 0 && _emptyText.Length > 0;
        LayoutEmpty();
    }

    private void LayoutEmpty()
    {
        int pad = Ui.S(this, 12);
        _empty.SetBounds(pad, Ui.S(this, 8), Math.Max(10, Width - pad * 2), Math.Max(10, Height - Ui.S(this, 16)));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ItemHeight = Ui.S(this, _rowHeight);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutEmpty();
        Invalidate();   // rows are drawn up to the edge: repaint them all when the width changes (scroll bar appears/disappears)
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexFromPoint(e.Location);
        if (index == _hover) return;
        int old = _hover;
        _hover = index;
        if (old >= 0 && old < Items.Count) Invalidate(GetItemRectangle(old));
        if (index >= 0) Invalidate(GetItemRectangle(index));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        int old = _hover;
        _hover = -1;
        if (old >= 0 && old < Items.Count) Invalidate(GetItemRectangle(old));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        int index = IndexFromPoint(e.Location);
        if (index < 0) return;
        if (CheckOnRowClick || BoxBounds(GetItemRectangle(index)).Contains(e.Location))
            SetItemChecked(index, !_checked[index]);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space && SelectedIndex >= 0)
        {
            SetItemChecked(SelectedIndex, !_checked[SelectedIndex]);
            e.Handled = true;
        }
    }

    /// <summary>The clickable area around the box (a little larger than the box itself).</summary>
    private Rectangle BoxBounds(Rectangle row)
    {
        int pad = Ui.S(this, 6);
        int box = Ui.S(this, 18);
        return new Rectangle(row.X, row.Y, pad + box + pad, row.Height);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var row = e.Bounds;
        bool selected = !CheckOnRowClick && (e.State & DrawItemState.Selected) != 0;
        bool hover = e.Index == _hover && Enabled;
        bool isChecked = _checked[e.Index];

        using (var back = new SolidBrush(selected ? Ui.AccentSoft : hover ? Ui.Surface : BackColor))
            g.FillRectangle(back, row);
        if (selected)
        {
            using var accent = new SolidBrush(Ui.Accent);
            g.FillRectangle(accent, row.X, row.Y, Ui.S(this, 3), row.Height);
        }
        using (var divider = new Pen(Ui.Soft, 1))
            g.DrawLine(divider, row.Left, row.Bottom - 1, row.Right, row.Bottom - 1);

        int pad = Ui.S(this, 6);
        int boxSize = Ui.S(this, 16);
        var box = new Rectangle(row.X + pad + Ui.S(this, 2), row.Y + (row.Height - boxSize) / 2, boxSize, boxSize);
        DrawBox(g, box, isChecked);

        string text = Items[e.Index]?.ToString() ?? string.Empty;
        string? subtitle = SubtitleOf?.Invoke(e.Index);
        string? badge = BadgeOf?.Invoke(e.Index);

        int right = row.Right - pad;
        if (!string.IsNullOrEmpty(badge))
        {
            var font = Ui.Font(8f, FontStyle.Bold);
            int w = TextRenderer.MeasureText(badge, font, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.S(this, 14);
            int h = Ui.S(this, 20);
            var pill = new Rectangle(right - w, row.Y + (row.Height - h) / 2, w, h);
            using (var path = Ui.RoundedRect(pill, h / 2))
            using (var fill = new SolidBrush(Ui.Soft))
            using (var line = new Pen(Ui.Line, 1))
            {
                g.FillPath(fill, path);
                g.DrawPath(line, path);
            }
            TextRenderer.DrawText(g, badge, font, pill, Ui.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            right = pill.Left - pad;
        }

        int textX = box.Right + Ui.S(this, 10);
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        Color nameColor = Enabled ? Ui.Ink : Ui.Faint;

        if (string.IsNullOrEmpty(subtitle))
        {
            TextRenderer.DrawText(g, text, Ui.Font(9.5f), new Rectangle(textX, row.Y, Math.Max(10, right - textX), row.Height), nameColor, flags);
        }
        else
        {
            int half = row.Height / 2;
            TextRenderer.DrawText(g, text, Ui.Font(9.5f, FontStyle.Bold),
                new Rectangle(textX, row.Y + Ui.S(this, 3), Math.Max(10, right - textX), half), nameColor, flags);
            TextRenderer.DrawText(g, subtitle, Ui.Font(8.5f),
                new Rectangle(textX, row.Y + half, Math.Max(10, right - textX), half - Ui.S(this, 3)), Ui.Muted, flags);
        }
    }

    private void DrawBox(Graphics g, Rectangle box, bool isChecked)
    {
        using var path = Ui.RoundedRect(box, Ui.S(this, 4));
        if (isChecked)
        {
            using var fill = new SolidBrush(Enabled ? Ui.Accent : Ui.Faint);
            g.FillPath(fill, path);
            using var tick = new Pen(Color.White, Math.Max(1.6f, Ui.S(this, 2))) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            float x = box.X, y = box.Y, w = box.Width, h = box.Height;
            g.DrawLines(tick, new[] { new PointF(x + w * 0.24f, y + h * 0.53f), new PointF(x + w * 0.43f, y + h * 0.72f), new PointF(x + w * 0.77f, y + h * 0.30f) });
        }
        else
        {
            using var fill = new SolidBrush(Enabled ? Color.White : Ui.Soft);
            using var line = new Pen(Enabled ? Color.FromArgb(148, 163, 184) : Ui.LineStrong, 1.25f);
            g.FillPath(fill, path);
            g.DrawPath(line, path);
        }
    }
}
