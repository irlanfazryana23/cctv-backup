using System.Runtime.InteropServices;
using System.Text;
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
/// Thin wrapper over the Xiongmai (XM) NetSdk.dll that ships with the CMS.
/// Everything here is stdcall / x86. Signatures were derived by disassembling the exports
/// (argument byte counts from each function's "ret N") plus struct layouts from the CMS PDBs.
/// </summary>
public sealed class XmSdk : IDisposable
{
    // ---- native signatures (stdcall, 32-bit; every argument occupies one 4-byte slot) ----
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitFn(IntPtr disconnectCb, IntPtr user);          // ret 8
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte CleanupFn();                                      // ret
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetLastErrorFn();                                  // ret
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetConnectTimeFn(int waitMs, int tries);           // ret 8
    // Login_Cloud(serial, port(ignored), user, password, DEVICEINFO*, int* error, socketStyle(ignored)) ret 0x1c
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LoginCloudFn(IntPtr serial, int port, IntPtr user, IntPtr pass, IntPtr deviceInfo, IntPtr error, int socketStyle);
    // Login(ip, port, user, password, DEVICEINFO*, int* error, socketStyle) ret 0x1c. socketStyle 0 = direct TCP.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LoginFn(IntPtr address, int port, IntPtr user, IntPtr pass, IntPtr deviceInfo, IntPtr error, int socketStyle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte LogoutFn(int loginId);                            // ret 4
    // FindFile(loginId, FINDINFO*, FILE_DATA*, maxCount, int* found, waitMs) ret 0x18
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FindFileFn(int loginId, IntPtr findInfo, IntPtr fileData, int maxCount, IntPtr found, int waitMs);
    // GetFileByName(loginId, FILE_DATA*, char* savePath, posCb, user, dataCb) ret 0x18
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetFileByNameFn(int loginId, IntPtr fileData, IntPtr savePath, IntPtr posCb, IntPtr user, IntPtr dataCb);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDownloadPosFn(int handle);                      // ret 4
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte StopGetFileFn(int handle);                        // ret 4
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DisconnectCb(int loginId, IntPtr ip, int port, uint user);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string? path);

    // H264_DVR_DEVICEINFO is 832 bytes in this SDK; hand it a generously sized zeroed buffer.
    private const int DeviceInfoBytes = 4096;

    private readonly IntPtr _lib;
    private readonly InitFn _init;
    private readonly CleanupFn _cleanup;
    private readonly GetLastErrorFn _getLastError;
    private readonly SetConnectTimeFn _setConnectTime;
    private readonly LoginCloudFn _loginCloud;
    private readonly LoginFn _login;
    private readonly LogoutFn _logout;
    private readonly FindFileFn _findFile;
    private readonly GetFileByNameFn _getFileByName;
    private readonly GetDownloadPosFn _getDownloadPos;
    private readonly StopGetFileFn _stopGetFile;
    private readonly DisconnectCb _disconnectCallback;   // keep alive: the SDK holds the raw pointer
    private bool _disposed;

    public string SdkDirectory { get; }

    private XmSdk(string sdkDir)
    {
        if (Environment.Is64BitProcess)
            throw new SdkException(Loc.T("NetSdk.dll is 32-bit; this process must run as x86."));

        SdkDirectory = sdkDir;
        string dll = Path.Combine(sdkDir, "NetSdk.dll");
        if (!File.Exists(dll))
            throw new SdkException(Loc.F("NetSdk.dll not found in '{0}'. Install the CMS or set the SDK folder.", sdkDir));

        // NetSdk.dll depends on sibling DLLs, so let Windows search the SDK folder.
        SetDllDirectory(sdkDir);
        _lib = NativeLibrary.Load(dll);

        T Get<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, name));

        _init = Get<InitFn>("H264_DVR_Init");
        _cleanup = Get<CleanupFn>("H264_DVR_Cleanup");
        _getLastError = Get<GetLastErrorFn>("H264_DVR_GetLastError");
        _setConnectTime = Get<SetConnectTimeFn>("H264_DVR_SetConnectTime");
        _loginCloud = Get<LoginCloudFn>("H264_DVR_Login_Cloud");
        _login = Get<LoginFn>("H264_DVR_Login");
        _logout = Get<LogoutFn>("H264_DVR_Logout");
        _findFile = Get<FindFileFn>("H264_DVR_FindFile");
        _getFileByName = Get<GetFileByNameFn>("H264_DVR_GetFileByName");
        _getDownloadPos = Get<GetDownloadPosFn>("H264_DVR_GetDownloadPos");
        _stopGetFile = Get<StopGetFileFn>("H264_DVR_StopGetFile");

        _disconnectCallback = (_, _, _, _) => { };
        int ok = _init(Marshal.GetFunctionPointerForDelegate(_disconnectCallback), IntPtr.Zero);
        if (ok == 0)
            throw new SdkException("H264_DVR_Init", _getLastError());
    }

    /// <summary>Loads NetSdk.dll from the given folder (normally the CMS install folder).</summary>
    public static XmSdk Load(string sdkDir) => new(sdkDir);

    public static string DefaultSdkDirectory => @"C:\Program Files (x86)\CMS";

    public int LastError => _getLastError();

    /// <summary>Connect timeout in ms and number of tries (applies to later logins).</summary>
    public void SetConnectTime(int waitMs, int tries) => _setConnectTime(waitMs, tries);

    /// <summary>
    /// Logs in through the cloud relay using the device's cloud serial ID.
    /// Returns the login handle, or throws <see cref="SdkException"/> with the SDK's error code.
    /// </summary>
    public int LoginCloud(string cloudId, string user, string password) =>
        LoginCloudDetailed(cloudId, user, password).Handle;

    /// <summary>Same as <see cref="LoginCloud"/>, and also returns what the DVR says about itself (e.g. channel count).</summary>
    public (int Handle, DeviceDetails Device) LoginCloudDetailed(string cloudId, string user, string password) =>
        LoginCore(cloudId, 0, user, password, cloud: true);

    /// <summary>
    /// Logs in directly to a DVR by IP address (or domain name) and port, without the cloud.
    /// This PC must be able to reach the address (same network, VPN or leased line).
    /// </summary>
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
            int handle = cloud
                ? _loginCloud(pId, 0, pUser, pPass, info, err, 0)
                : _login(pId, port, pUser, pPass, info, err, 0);   // style 0 = direct TCP (verified in the driver: 2 would mean cloud)
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
            // do not leave the password lingering in unmanaged memory
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

    /// <summary>
    /// Lists recorded files for one channel (0-based) in [from, to]. The DVR returns at most
    /// <paramref name="maxFiles"/> entries per call, so callers should page by time window.
    /// </summary>
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
                // "nothing recorded" is not an error: return an empty list unless the SDK gave a real error.
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

    /// <summary>An in-flight file download. Dispose to stop it and free native buffers.</summary>
    public sealed class Download : IDisposable
    {
        private readonly XmSdk _sdk;
        private IntPtr _fileData, _path;
        public int Handle { get; }

        internal Download(XmSdk sdk, int handle, IntPtr fileData, IntPtr path)
        {
            _sdk = sdk; Handle = handle; _fileData = fileData; _path = path;
        }

        /// <summary>0-100 while running; 100 when finished; negative/over 100 on failure.</summary>
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

    /// <summary>Starts downloading one remote file into <paramref name="savePath"/> (raw DVR format).</summary>
    public unsafe Download StartDownload(int loginId, RemoteFile file, string savePath)
    {
        IntPtr pData = Marshal.AllocHGlobal(sizeof(XmFileData));
        Marshal.StructureToPtr(file.Raw, pData, false);
        // The SDK takes an ANSI path; callers should therefore download into an ASCII-only folder.
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
