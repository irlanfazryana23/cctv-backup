using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Playback;

namespace CctvBackup.App;

/// <summary>
/// The built-in video player: plays the .h264 backups inside this program, with its own controls.
/// The picture is drawn by the vendor's decoder library straight into the black panel.
/// </summary>
internal sealed class PlayerForm : Form
{
    // Speed steps the decoder offers: slow, normal, 2x, 4x.
    private static readonly int[] SpeedLevels = { -1, 0, 1, 2 };

    private readonly string _decoderDir;
    private PlaySdk? _sdk;
    private PlaySdk.Session? _session;
    private string? _path;
    private bool _paused, _ended, _scrubbing, _fullScreen;
    private int _speedIndex = 1;
    private int _durationMs;
    private FormBorderStyle _borderBefore;
    private FormWindowState _stateBefore;

    private readonly Panel _video = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    private readonly TrackBar _seek = new() { Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, Dock = DockStyle.Fill, LargeChange = 50, SmallChange = 10, AccessibleName = Loc.T("Position in the recording") };
    private readonly Label _time = new() { AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(8, 8, 3, 3), Text = "00:00 / 00:00" };
    private readonly Button _playPause = new() { AutoSize = true, MinimumSize = new Size(90, 32) };
    private readonly Button _slower = new() { AutoSize = true, MinimumSize = new Size(90, 32) };
    private readonly Button _normal = new() { AutoSize = true, MinimumSize = new Size(90, 32) };
    private readonly Button _faster = new() { AutoSize = true, MinimumSize = new Size(90, 32) };
    private readonly Button _prevFile = new() { AutoSize = true, MinimumSize = new Size(90, 32) };
    private readonly Button _nextFile = new() { AutoSize = true, MinimumSize = new Size(90, 32) };
    private readonly Button _snapshot = new() { AutoSize = true, MinimumSize = new Size(90, 32) };
    private readonly Button _full = new() { AutoSize = true, MinimumSize = new Size(90, 32) };
    private readonly Label _speedText = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 8, 3, 3) };
    private readonly Label _info = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _hint = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = SystemColors.GrayText, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TableLayoutPanel _bar = new() { Dock = DockStyle.Bottom, ColumnCount = 1, AutoSize = true, Padding = new Padding(10, 4, 10, 6) };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 250 };

    public PlayerForm(string decoderDirectory)
    {
        _decoderDir = decoderDirectory;
        Text = Loc.T("CCTV Backup - Video player");
        Icon = AppIcon.Get();
        Font = SystemFonts.MessageBoxFont ?? Font;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 640);
        MinimumSize = new Size(640, 420);
        KeyPreview = true;

        _slower.Text = Loc.T("Slower"); _normal.Text = Loc.T("Normal speed"); _faster.Text = Loc.T("Faster");
        _prevFile.Text = Loc.T("Previous file"); _nextFile.Text = Loc.T("Next file");
        _snapshot.Text = Loc.T("Save picture"); _full.Text = Loc.T("Full screen");
        _playPause.Text = Loc.T("Pause");

        // seek bar + time
        var seekRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        seekRow.Controls.Add(_seek, 0, 0);
        seekRow.Controls.Add(_time, 1, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        buttons.Controls.AddRange(new Control[] { _playPause, _slower, _normal, _faster, _speedText, _prevFile, _nextFile, _snapshot, _full });

        _info.Height = 22;
        _hint.Height = 20;
        _hint.Text = Loc.T("Space: play or pause.   Left / Right: 10 seconds back or forward.   Double-click the picture or press F11: full screen.");
        _bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _bar.Controls.Add(seekRow, 0, 0);
        _bar.Controls.Add(buttons, 0, 1);
        _bar.Controls.Add(_info, 0, 2);
        _bar.Controls.Add(_hint, 0, 3);

        Controls.Add(_video);
        Controls.Add(_bar);
        _bar.BringToFront();      // the bar docks at the bottom, the picture fills the rest
        _video.BringToFront();

        _playPause.Click += (_, _) => TogglePause();
        _slower.Click += (_, _) => ChangeSpeed(-1);
        _faster.Click += (_, _) => ChangeSpeed(+1);
        _normal.Click += (_, _) => SetSpeed(1);
        _prevFile.Click += (_, _) => OpenNeighbour(-1);
        _nextFile.Click += (_, _) => OpenNeighbour(+1);
        _snapshot.Click += (_, _) => SavePicture();
        _full.Click += (_, _) => ToggleFullScreen();
        _video.DoubleClick += (_, _) => ToggleFullScreen();
        _video.Resize += (_, _) => _session?.Redraw();
        Resize += (_, _) => _session?.Redraw();
        _seek.MouseDown += OnSeekMouseDown;
        _seek.MouseUp += (_, _) => _scrubbing = false;
        _seek.Scroll += (_, _) => { if (_session is not null) { _ended = false; _session.SeekFraction(_seek.Value / 1000f); } };
        _tick.Tick += (_, _) => OnTick();
        FormClosed += (_, _) => CloseSession();
        UpdateSpeedText();
        UpdateButtons();
    }

    /// <summary>Opens a recording and starts playing it. Throws <see cref="PlaybackException"/> with a message for the user.</summary>
    public void Open(string path)
    {
        _sdk ??= PlaySdk.Load(_decoderDir);
        CloseSession();
        _session = _sdk.NewSession();
        try
        {
            _session.Open(path, _video.Handle);
        }
        catch
        {
            CloseSession();
            throw;
        }
        // The decoder accepts any file; an empty or damaged one has no frames and no length.
        if (_session.TotalFrames == 0 || _session.DurationSeconds <= 0)
        {
            CloseSession();
            throw new PlaybackException(Loc.F("This recording has no playable video. It may be empty or damaged:\n{0}", path));
        }
        _path = path;
        _paused = false; _ended = false; _speedIndex = 1;
        _durationMs = _session.DurationSeconds * 1000;
        Text = Loc.F("CCTV Backup - Video player - {0}", Path.GetFileName(path));
        _info.Text = path;
        UpdateSpeedText();
        UpdateButtons();
        _tick.Start();
        OnTick();
    }

    private void CloseSession()
    {
        _tick.Stop();
        _session?.Dispose();
        _session = null;
    }

    private void OnTick()
    {
        if (_session is null) return;
        float fraction = _session.Fraction;
        int pos = Math.Clamp(_session.PositionMs, 0, _durationMs);
        _time.Text = $"{Format(pos)} / {Format(_durationMs)}";
        if (!_scrubbing) _seek.Value = Math.Clamp((int)(fraction * 1000), 0, 1000);

        // The decoder keeps showing the last picture when the file ends; notice it so Play can start again.
        if (!_paused && !_ended && fraction >= 0.995f)
        {
            _ended = true;
            _paused = true;
            _session.SetPaused(true);
            UpdateButtons();
        }
    }

    private void OnSeekMouseDown(object? sender, MouseEventArgs e)
    {
        if (_session is null || e.Button != MouseButtons.Left) return;
        // A click on the bar jumps there (the TrackBar itself only pages). 13 px is the width of the bar's end caps.
        const int pad = 13;
        double f = Math.Clamp((e.X - pad) / (double)Math.Max(1, _seek.Width - 2 * pad), 0, 1);
        _scrubbing = true;
        _seek.Value = (int)(f * 1000);
        _ended = false;
        _session.SeekFraction((float)f);
    }

    private void TogglePause()
    {
        if (_session is null) return;
        if (_ended)
        {
            // played to the end: start again from the beginning
            _ended = false; _paused = false;
            _session.SeekFraction(0f);
            _session.SetPaused(false);
        }
        else
        {
            _paused = !_paused;
            _session.SetPaused(_paused);
        }
        UpdateButtons();
    }

    private void ChangeSpeed(int direction) => SetSpeed(Math.Clamp(_speedIndex + direction, 0, SpeedLevels.Length - 1));

    private void SetSpeed(int index)
    {
        if (_session is null) return;
        _speedIndex = index;
        // Starting to play again returns to normal speed; then step to the wanted one.
        _session.NormalSpeed(_video.Handle);
        int level = SpeedLevels[index];
        if (level < 0) _session.Slower();
        for (int i = 0; i < level; i++) _session.Faster();
        if (_paused) _session.SetPaused(true);
        UpdateSpeedText();
    }

    private void UpdateSpeedText()
    {
        string name = SpeedLevels[_speedIndex] switch { -1 => Loc.T("Slow"), 0 => "1x", 1 => "2x", _ => "4x" };
        _speedText.Text = Loc.F("Speed: {0}", name);
    }

    private void UpdateButtons()
    {
        bool open = _session is not null;
        _playPause.Text = _paused ? Loc.T("Play") : Loc.T("Pause");
        _playPause.Enabled = _slower.Enabled = _normal.Enabled = _faster.Enabled = _snapshot.Enabled = open;
        var (prev, next) = Neighbours();
        _prevFile.Enabled = prev is not null;
        _nextFile.Enabled = next is not null;
    }

    /// <summary>The recordings before and after this one in the same folder (they are named by time of day).</summary>
    private (string? Prev, string? Next) Neighbours()
    {
        if (_path is null) return (null, null);
        try
        {
            var files = Directory.GetFiles(Path.GetDirectoryName(_path)!, "*.h264")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            int i = files.FindIndex(f => string.Equals(f, _path, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return (null, null);
            return (i > 0 ? files[i - 1] : null, i < files.Count - 1 ? files[i + 1] : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, null); }
    }

    private void OpenNeighbour(int direction)
    {
        var (prev, next) = Neighbours();
        string? target = direction < 0 ? prev : next;
        if (target is null) return;
        TryOpen(target);
    }

    private void TryOpen(string path)
    {
        try { Open(path); }
        catch (PlaybackException ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateButtons();
        }
    }

    private void SavePicture()
    {
        if (_session is null || _path is null) return;
        string baseName = $"{Path.GetFileNameWithoutExtension(_path)}_at-{Format(_session.PositionMs).Replace(':', '-')}";
        string jpg = Path.Combine(Path.GetDirectoryName(_path)!, baseName + ".jpg");
        for (int n = 2; File.Exists(jpg); n++) jpg = Path.Combine(Path.GetDirectoryName(_path)!, $"{baseName} ({n}).jpg");
        string bmp = Path.Combine(Path.GetTempPath(), "cctvbackup-" + Guid.NewGuid().ToString("N") + ".bmp");
        try
        {
            // The decoder writes a BMP; keep a smaller JPG next to the recording.
            if (!_session.SaveSnapshot(bmp))
            {
                _info.Text = Loc.T("The picture could not be saved.");
                return;
            }
            using (var img = Image.FromFile(bmp))
            {
                var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var q = new EncoderParameters(1);
                q.Param[0] = new EncoderParameter(Encoder.Quality, 90L);
                img.Save(jpg, codec, q);
            }
            _info.Text = Loc.F("Picture saved: {0}", jpg);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or ArgumentException)
        {
            _info.Text = Loc.F("The picture could not be saved: {0}", ex.Message);
        }
        finally
        {
            try { if (File.Exists(bmp)) File.Delete(bmp); } catch (IOException) { }
        }
    }

    private void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _borderBefore = FormBorderStyle; _stateBefore = WindowState;
            _fullScreen = true;
            _bar.Visible = false;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;   // so Maximized covers the taskbar too
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            _fullScreen = false;
            FormBorderStyle = _borderBefore;
            WindowState = _stateBefore;
            _bar.Visible = true;
        }
        _full.Text = _fullScreen ? Loc.T("Exit full screen") : Loc.T("Full screen");
        _session?.Redraw();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Space: TogglePause(); return true;
            case Keys.Left: SeekBy(-10_000); return true;
            case Keys.Right: SeekBy(+10_000); return true;
            case Keys.F11: ToggleFullScreen(); return true;
            case Keys.Escape when _fullScreen: ToggleFullScreen(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void SeekBy(int deltaMs)
    {
        if (_session is null) return;
        _ended = false;
        _session.SeekMs(Math.Clamp(_session.PositionMs + deltaMs, 0, _durationMs));
    }

    private static string Format(int ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tick.Dispose();
        base.Dispose(disposing);
    }
}
