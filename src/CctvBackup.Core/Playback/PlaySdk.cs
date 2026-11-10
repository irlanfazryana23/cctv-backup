using System.Runtime.InteropServices;
using System.Text;
using CctvBackup.Core.Localization;

namespace CctvBackup.Core.Playback;

/// <summary>A problem opening or playing a recording; the message is safe to show to the user.</summary>
public sealed class PlaybackException : Exception
{
    public PlaybackException(string message) : base(message) { }
}

/// <summary>
/// The vendor's video decoder (H264Play.dll, from the DVR player) used directly, so recordings play inside this program.
/// It is a 32-bit stdcall DLL: one "port" per open file, drawing straight into a window handle.
/// Argument sizes were read from the DLL (every function returns with the expected byte count) and match the usual
/// PlaySDK layout; the calls below were checked by playing real recordings.
/// </summary>
public sealed class PlaySdk : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetPortFn(IntPtr portOut);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PortFn(int port);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PortPtrFn(int port, IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PortUIntFn(int port, uint v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint PortGetUIntFn(int port);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate float PortGetFloatFn(int port);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PortFloatFn(int port, float v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CatchPicFn(int port, IntPtr file, int format);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PictureSizeFn(int port, IntPtr width, IntPtr height);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint length);
    private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

    private readonly IntPtr _lib;
    private readonly GetPortFn _getPort;
    private readonly PortFn _freePort, _stop, _closeFile, _fast, _slow, _refreshPlay, _getLastError;
    private readonly PortPtrFn _openFile, _play;
    private readonly PortUIntFn _pause, _setPlayedTimeEx;
    private readonly PortGetUIntFn _getFileTime, _getPlayedTimeEx, _getFileTotalFrames;
    private readonly PortGetFloatFn _getPlayPos;
    private readonly PortFloatFn _setPlayPos;
    private readonly CatchPicFn _catchPic;
    private readonly PictureSizeFn _getPictureSize;
    private bool _disposed;

    public string Directory { get; }

    private PlaySdk(string directory)
    {
        if (Environment.Is64BitProcess)
            throw new PlaybackException(Loc.T("The video decoder is 32-bit; this program must run as x86."));
        Directory = directory;
        string dll = Path.Combine(directory, "H264Play.dll");
        if (!File.Exists(dll))
            throw new PlaybackException(Loc.F("The video decoder was not found in '{0}'.", directory));

        // Load with the DLL's own folder first, so its sibling DLLs (avcodec, StreamReader...) are found there.
        _lib = LoadLibraryEx(dll, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (_lib == IntPtr.Zero)
            throw new PlaybackException(Loc.F("The video decoder could not be loaded (Windows error {0}).", Marshal.GetLastWin32Error()));

        T Get<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, "H264_PLAY_" + name));

        _getPort = Get<GetPortFn>("GetPort");
        _freePort = Get<PortFn>("FreePort");
        _openFile = Get<PortPtrFn>("OpenFile");
        _closeFile = Get<PortFn>("CloseFile");
        _play = Get<PortPtrFn>("Play");
        _pause = Get<PortUIntFn>("Pause");
        _stop = Get<PortFn>("Stop");
        _fast = Get<PortFn>("Fast");
        _slow = Get<PortFn>("Slow");
        _refreshPlay = Get<PortFn>("RefreshPlay");
        _getFileTime = Get<PortGetUIntFn>("GetFileTime");
        _getFileTotalFrames = Get<PortGetUIntFn>("GetFileTotalFrames");
        _getPlayedTimeEx = Get<PortGetUIntFn>("GetPlayedTimeEx");
        _setPlayedTimeEx = Get<PortUIntFn>("SetPlayedTimeEx");
        _getPlayPos = Get<PortGetFloatFn>("GetPlayPos");
        _setPlayPos = Get<PortFloatFn>("SetPlayPos");
        _catchPic = Get<CatchPicFn>("CatchPic");
        _getPictureSize = Get<PictureSizeFn>("GetPictureSize");
        _getLastError = Get<PortFn>("GetLastError");
    }

    public static PlaySdk Load(string directory) => new(directory);

    /// <summary>One open recording drawing into a window. Dispose to stop and release it.</summary>
    public sealed class Session : IDisposable
    {
        private readonly PlaySdk _sdk;
        private readonly int _port;
        private IntPtr _path;
        private bool _closed;
        private bool _fileOpen;
        private bool _playing;

        internal Session(PlaySdk sdk, int port) { _sdk = sdk; _port = port; }

        public void Open(string file, IntPtr window)
        {
            string ansi = PlaySdk.ToAnsiSafePath(file);
            _path = Marshal.StringToHGlobalAnsi(ansi);
            if (_sdk._openFile(_port, _path) == 0)
                throw new PlaybackException(Loc.F("This recording could not be opened (error {0}). The file may be damaged or incomplete.", _sdk._getLastError(_port)));
            _fileOpen = true;
            if (_sdk._play(_port, window) == 0)
                throw new PlaybackException(Loc.F("This recording could not be played (error {0}).", _sdk._getLastError(_port)));
            _playing = true;
        }

        public void SetPaused(bool paused) => _sdk._pause(_port, paused ? 1u : 0u);

        /// <summary>Total length of the recording in seconds.</summary>
        public int DurationSeconds => (int)_sdk._getFileTime(_port);

        public uint TotalFrames => _sdk._getFileTotalFrames(_port);

        /// <summary>Position in milliseconds.</summary>
        public int PositionMs => (int)_sdk._getPlayedTimeEx(_port);

        public void SeekMs(int ms) => _sdk._setPlayedTimeEx(_port, (uint)Math.Max(0, ms));

        /// <summary>0..1</summary>
        public float Fraction => _sdk._getPlayPos(_port);

        public void SeekFraction(float f) => _sdk._setPlayPos(_port, Math.Clamp(f, 0f, 1f));

        public void Faster() => _sdk._fast(_port);
        public void Slower() => _sdk._slow(_port);

        /// <summary>Back to normal speed (starting to play again resets it).</summary>
        public void NormalSpeed(IntPtr window) => _sdk._play(_port, window);

        /// <summary>Draws the current picture again (after the window was resized, hidden or covered).</summary>
        public void Redraw() => _sdk._refreshPlay(_port);

        public (int Width, int Height) PictureSize
        {
            get
            {
                IntPtr w = Marshal.AllocHGlobal(8), h = w + 4;
                try
                {
                    Marshal.WriteInt32(w, 0); Marshal.WriteInt32(h, 0);
                    _sdk._getPictureSize(_port, w, h);
                    return (Marshal.ReadInt32(w), Marshal.ReadInt32(h));
                }
                finally { Marshal.FreeHGlobal(w); }
            }
        }

        /// <summary>Saves the picture on screen as a BMP file (the decoder's own format). Returns false if it could not.</summary>
        public bool SaveSnapshot(string path, int format = 0) => SaveSnapshotCore(path, format);

        public bool SaveSnapshotBmp(string bmpPath) => SaveSnapshotCore(bmpPath, 0);

        private bool SaveSnapshotCore(string bmpPath, int format)
        {
            IntPtr p = Marshal.StringToHGlobalAnsi(PlaySdk.ToAnsiSafePath(bmpPath, mustExist: false));
            try { return _sdk._catchPic(_port, p, format) != 0; }
            finally { Marshal.FreeHGlobal(p); }
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            try
            {
                if (_playing) _sdk._stop(_port);
                if (_fileOpen) _sdk._closeFile(_port);
                _sdk._freePort(_port);
            }
            catch { /* best effort */ }
            if (_path != IntPtr.Zero) Marshal.FreeHGlobal(_path);
        }
    }

    public Session NewSession()
    {
        IntPtr p = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(p, -1);
            if (_getPort(p) == 0)
                throw new PlaybackException(Loc.T("The video decoder has no free player slot. Close another video and try again."));
            return new Session(this, Marshal.ReadInt32(p));
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    /// <summary>The decoder only understands ANSI paths. Use the 8.3 short path when the real one has other characters.</summary>
    internal static string ToAnsiSafePath(string path, bool mustExist = true)
    {
        if (path.All(c => c < 128)) return path;
        if (mustExist)
        {
            var sb = new StringBuilder(520);
            uint n = GetShortPathName(path, sb, (uint)sb.Capacity);
            if (n > 0 && n < sb.Capacity && sb.ToString().All(c => c < 128)) return sb.ToString();
        }
        throw new PlaybackException(Loc.F("The decoder cannot open a path with these characters:\n{0}\nMove the backup to a folder with a plain name.", path));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // The decoder is left loaded on purpose: unloading it while a window may still be drawing is not worth the risk,
        // and the process is about to exit anyway.
    }
}
