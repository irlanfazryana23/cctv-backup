using System.Runtime.InteropServices;
using CctvBackup.Core.Localization;

namespace CctvBackup.Core.Sdk;

public sealed class SdkException : Exception
{
    public int Code { get; }
    public SdkException(string operation, int code)
        : base(Loc.F("{0} failed: {1}", Loc.T(operation), SdkErrors.Describe(code))) => Code = code;
    public SdkException(string message) : base(message) { }
}

/// <summary>
/// Thin wrapper over the Xiongmai (XM) NetSdk (libnetsdk.so on native Linux, or NetSdk.dll on Windows / Wine).
/// CallingConvention.Winapi uses stdcall on Windows/Wine x86 and cdecl on Linux.
/// </summary>
public sealed class XmSdk : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int InitFn(IntPtr disconnectCb, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate byte CleanupFn();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetLastErrorFn();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetConnectTimeFn(int waitMs, int tries);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int LoginCloudFn(IntPtr serial, int port, IntPtr user, IntPtr pass, IntPtr deviceInfo, IntPtr error, int socketStyle);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int LoginFn(IntPtr address, int port, IntPtr user, IntPtr pass, IntPtr deviceInfo, IntPtr error, int socketStyle);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate byte LogoutFn(int loginId);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int FindFileFn(int loginId, IntPtr findInfo, IntPtr fileData, int maxCount, IntPtr found, int waitMs);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetFileByNameFn(int loginId, IntPtr fileData, IntPtr savePath, IntPtr posCb, IntPtr user, IntPtr dataCb);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDownloadPosFn(int handle);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate byte StopGetFileFn(int handle);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void DisconnectCb(int loginId, IntPtr ip, int port, uint user);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string? path);

    private const int DeviceInfoBytes = 4096;

    private readonly IntPtr _lib;
    private readonly InitFn _init;
    private readonly CleanupFn _cleanup;
    private readonly GetLastErrorFn _getLastError;
    private readonly SetConnectTimeFn _setConnectTime;
    private readonly LoginCloudFn? _loginCloud;
    private readonly LoginFn _login;
    private readonly LogoutFn _logout;
    private readonly FindFileFn _findFile;
    private readonly GetFileByNameFn _getFileByName;
    private readonly GetDownloadPosFn _getDownloadPos;
    private readonly StopGetFileFn _stopGetFile;
    private readonly DisconnectCb _disconnectCallback;
    private bool _disposed;

    public string SdkDirectory { get; }

    private XmSdk(string sdkDir)
    {
        SdkDirectory = sdkDir;
        string soPath = Path.Combine(sdkDir, SdkLocator.LinuxSoName);
        string dllPath = Path.Combine(sdkDir, SdkLocator.DllName);

        string libPath;
        if (!OperatingSystem.IsWindows() && File.Exists(soPath))
        {
            libPath = soPath;
        }
        else if (File.Exists(dllPath))
        {
            if (Environment.Is64BitProcess)
                throw new SdkException(Loc.T("NetSdk.dll is 32-bit; this process must run as x86."));
            libPath = dllPath;
        }
        else
        {
            throw new SdkException(Loc.F("NetSdk.dll not found in '{0}'. Install the CMS or set the SDK folder.", sdkDir));
        }

        if (OperatingSystem.IsWindows())
            SetDllDirectory(sdkDir);

        _lib = NativeLibrary.Load(libPath);

        T Get<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, name));

        _init = Get<InitFn>("H264_DVR_Init");
        _cleanup = Get<CleanupFn>("H264_DVR_Cleanup");
        _getLastError = Get<GetLastErrorFn>("H264_DVR_GetLastError");
        _setConnectTime = Get<SetConnectTimeFn>("H264_DVR_SetConnectTime");
        _login = Get<LoginFn>("H264_DVR_Login");
        _logout = Get<LogoutFn>("H264_DVR_Logout");
        _findFile = Get<FindFileFn>("H264_DVR_FindFile");
        _getFileByName = Get<GetFileByNameFn>("H264_DVR_GetFileByName");
        _getDownloadPos = Get<GetDownloadPosFn>("H264_DVR_GetDownloadPos");
        _stopGetFile = Get<StopGetFileFn>("H264_DVR_StopGetFile");

        if (NativeLibrary.TryGetExport(_lib, "H264_DVR_Login_Cloud", out IntPtr pLoginCloud))
            _loginCloud = Marshal.GetDelegateForFunctionPointer<LoginCloudFn>(pLoginCloud);

        _disconnectCallback = (_, _, _, _) => { };
        int ok = _init(Marshal.GetFunctionPointerForDelegate(_disconnectCallback), IntPtr.Zero);
        if (ok == 0)
            throw new SdkException("H264_DVR_Init", _getLastError());
    }

    public static XmSdk Load(string sdkDir) => new(sdkDir);

    public static string DefaultSdkDirectory =>
        OperatingSystem.IsWindows() ? @"C:\Program Files (x86)\CMS" : "/usr/local/lib/cctv-backup";

    public int LastError => _getLastError();

    public void SetConnectTime(int waitMs, int tries) => _setConnectTime(waitMs, tries);

    public int LoginCloud(string cloudId, string user, string password) =>
        LoginCloudDetailed(cloudId, user, password).Handle;

    public (int Handle, DeviceDetails Device) LoginCloudDetailed(string cloudId, string user, string password) =>
        LoginCore(cloudId, 0, user, password, cloud: true);

    public (int Handle, DeviceDetails Device) LoginIpDetailed(string address, int port, string user, string password) =>
        LoginCore(address, port, user, password, cloud: false);

    private unsafe (int Handle, DeviceDetails Device) LoginCore(string target, int port, string user, string password, bool cloud)
    {
        IntPtr pId = Marshal.StringToHGlobalAnsi(target);
        IntPtr pUser = Marshal.StringToHGlobalAnsi(user);
        IntPtr pPass = Marshal.StringToHGlobalAnsi(password);
        IntPtr info = Marshal.AllocHGlobal(DeviceInfoBytes);
        IntPtr err = Marshal.AllocHGlobal(4);
        try
        {
            new Span<byte>((void*)info, DeviceInfoBytes).Clear();
            Marshal.WriteInt32(err, 0);
            int handle;
            if (cloud)
            {
                // In NetSdk, H264_DVR_Login_Cloud is a wrapper for H264_DVR_Login with socketStyle = 2 (TCPSOCKET_Nat / Cloud).
                handle = _loginCloud is not null
                    ? _loginCloud(pId, 0, pUser, pPass, info, err, 0)
                    : _login(pId, 0, pUser, pPass, info, err, 2);
            }
            else
            {
                handle = _login(pId, port, pUser, pPass, info, err, 0); // style 0 = direct TCP
            }

            if (handle <= 0)
            {
                int code = Marshal.ReadInt32(err);
                throw new SdkException("Login", code != 0 ? code : _getLastError());
            }

            var details = DeviceDetails.Parse(new ReadOnlySpan<byte>((void*)info, DeviceInfoBytes));
            return (handle, details);
        }
        finally
        {
            Marshal.FreeHGlobal(pId);
            Marshal.FreeHGlobal(pUser);
            for (int i = 0; i < password.Length; i++) Marshal.WriteByte(pPass, i, 0);
            Marshal.FreeHGlobal(pPass);
            Marshal.FreeHGlobal(info);
            Marshal.FreeHGlobal(err);
        }
    }

    public void Logout(int loginId)
    {
        if (loginId > 0) _logout(loginId);
    }

    public unsafe List<RemoteFile> FindFiles(int loginId, int channel, DateTime from, DateTime to,
        int maxFiles = 64, int waitMs = 8000)
    {
        var info = new XmFindInfo
        {
            Channel = channel,
            FileType = 0,                 // SDK_RECORD_ALL
            StartTime = XmTime.From(from),
            EndTime = XmTime.From(to),
            StreamType = 0,
        };
        int bytes = sizeof(XmFileData) * maxFiles;
        IntPtr pInfo = Marshal.AllocHGlobal(sizeof(XmFindInfo));
        IntPtr pFiles = Marshal.AllocHGlobal(bytes);
        IntPtr pFound = Marshal.AllocHGlobal(4);
        try
        {
            new Span<byte>((void*)pFiles, bytes).Clear();
            Marshal.StructureToPtr(info, pInfo, false);
            Marshal.WriteInt32(pFound, 0);

            int ret = _findFile(loginId, pInfo, pFiles, maxFiles, pFound, waitMs);
            int found = Marshal.ReadInt32(pFound);
            if (ret <= 0 && found <= 0)
            {
                int code = _getLastError();
                if (code == 0 || code == -11501) return new();
                throw new SdkException("Find recorded files", code);
            }

            var list = new List<RemoteFile>(Math.Max(0, found));
            for (int i = 0; i < Math.Min(found, maxFiles); i++)
            {
                var f = Marshal.PtrToStructure<XmFileData>(pFiles + i * sizeof(XmFileData));
                list.Add(new RemoteFile(f.Channel, f.GetFileName(), f.BeginTime.ToDateTime(),
                    f.EndTime.ToDateTime(), f.Size, f));
            }
            return list;
        }
        finally
        {
            Marshal.FreeHGlobal(pInfo);
            Marshal.FreeHGlobal(pFiles);
            Marshal.FreeHGlobal(pFound);
        }
    }

    public sealed class Download : IDisposable
    {
        private readonly XmSdk _sdk;
        private IntPtr _fileData, _path;
        public int Handle { get; }

        internal Download(XmSdk sdk, int handle, IntPtr fileData, IntPtr path)
        {
            _sdk = sdk; Handle = handle; _fileData = fileData; _path = path;
        }

        public int Position => _sdk._getDownloadPos(Handle);

        public void Dispose()
        {
            if (_fileData == IntPtr.Zero) return;
            _sdk._stopGetFile(Handle);
            Marshal.FreeHGlobal(_fileData);
            Marshal.FreeHGlobal(_path);
            _fileData = _path = IntPtr.Zero;
        }
    }

    public unsafe Download StartDownload(int loginId, RemoteFile file, string savePath)
    {
        IntPtr pData = Marshal.AllocHGlobal(sizeof(XmFileData));
        Marshal.StructureToPtr(file.Raw, pData, false);
        IntPtr pPath = Marshal.StringToHGlobalAnsi(savePath);
        int h = _getFileByName(loginId, pData, pPath, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (h <= 0)
        {
            int code = _getLastError();
            Marshal.FreeHGlobal(pData);
            Marshal.FreeHGlobal(pPath);
            throw new SdkException("Start download", code);
        }
        return new Download(this, h, pData, pPath);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cleanup(); } catch { /* best effort */ }
        NativeLibrary.Free(_lib);
        GC.KeepAlive(_disconnectCallback);
    }
}
