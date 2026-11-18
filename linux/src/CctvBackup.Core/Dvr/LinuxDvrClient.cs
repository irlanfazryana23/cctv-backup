using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Sdk;

namespace CctvBackup.Core.Dvr;

/// <summary>
/// 100% Native Linux DVR client (zero Wine dependency):
/// 1. Pure Native C# Cloud + DVRIP (<see cref="DvripDvrClient"/> + <see cref="XmCloudNatClient"/>):
///    Works out-of-the-box on any Linux system for both Cloud (Serial ID via libeznat P2P/Proxy + XMIP reliable UDP)
///    and Direct IP / Domain connections (TCP port 34567).
/// 2. Optional Native Linux SDK (<see cref="XmDvrClient"/>):
///    Used automatically when a native Linux <c>libnetsdk.so</c> is placed in the <c>sdk/</c> directory.
/// </summary>
public sealed class LinuxDvrClient : IDvrClient, IDisposable
{
    private readonly string _sdkDir;
    private readonly XmDvrClient _nativeClient;
    private readonly DvripDvrClient _dvripClient;

    public event Action<string>? Notice;

    public LinuxDvrClient(string sdkDirectory, XmCloudNatClient.NatClientOptions? natOptions = null)
    {
        _sdkDir = sdkDirectory;
        _nativeClient = new XmDvrClient(sdkDirectory);
        _nativeClient.Notice += msg => Notice?.Invoke(msg);
        _dvripClient = new DvripDvrClient(natOptions);
    }

    public string ActiveBackendDescription =>
        CanUseNativeSdkInProcess()
            ? (SdkLocator.HasNativeLinuxSdk(_sdkDir)
                ? "Native Linux SDK (libnetsdk.so) + Native C# Cloud/DVRIP"
                : "Native Windows SDK (NetSdk.dll)")
            : "Native Linux C# Engine (Cloud Serial ID NAT/XMIP + Direct TCP DVRIP)";

    public bool IsDriverReadyForCloud => true;

    private bool CanUseNativeSdkInProcess()
    {
        if (!OperatingSystem.IsWindows() && SdkLocator.HasNativeLinuxSdk(_sdkDir))
            return true;
        if (OperatingSystem.IsWindows() && !Environment.Is64BitProcess && SdkLocator.HasWindowsSdk(_sdkDir))
            return true;
        return false;
    }

    public async Task<IDvrConnection> ConnectAsync(Branch branch, Credential credential, CancellationToken ct)
    {
        if (CanUseNativeSdkInProcess())
        {
            try
            {
                return await _nativeClient.ConnectAsync(branch, credential, ct);
            }
            catch (DllNotFoundException)
            {
                // Fall back to pure native C# Cloud/DVRIP engine if libnetsdk.so fails to load
            }
            catch (BadImageFormatException)
            {
                // Fall back to pure native C# Cloud/DVRIP engine if libnetsdk.so has wrong ELF architecture
            }
        }

        return await _dvripClient.ConnectAsync(branch, credential, ct);
    }

    public void ResetDriver()
    {
        _nativeClient.ResetDriver();
    }

    public void Dispose()
    {
        _nativeClient.Dispose();
    }
}
