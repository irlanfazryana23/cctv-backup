using System.Text.Json.Serialization;
using CctvBackup.Core.Localization;

namespace CctvBackup.Core.Branches;

/// <summary>One camera on a DVR. <see cref="Channel"/> is 0-based (as the SDK expects).</summary>
public sealed record Camera(int Channel, string Name)
{
    /// <summary>1-based label used in folder names, e.g. "Cam01".</summary>
    public string FolderName => $"Cam{Channel + 1:00}";

    /// <summary>Cameras 1..count for a branch (count 0 gives an empty list = "not detected yet").</summary>
    public static List<Camera> Range(string branchName, int count) =>
        Enumerable.Range(0, Math.Max(0, count)).Select(i => new Camera(i, $"{branchName}_{i + 1}")).ToList();
}

/// <summary>How the program reaches a DVR (the CMS calls this "Login Type").</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LoginMode
{
    /// <summary>Through the cloud relay, using the DVR's Serial ID.</summary>
    Cloud = 0,

    /// <summary>Directly, using the DVR's IP address (or domain name) and port. The PC must be able to reach it.</summary>
    Ip = 1,
}

/// <summary>
/// A branch office DVR. No password is stored here.
/// For <see cref="LoginMode.Cloud"/> the <paramref name="CloudId"/> is used; for <see cref="LoginMode.Ip"/> the
/// <paramref name="Address"/> and <paramref name="Port"/> are used. Branches saved before IP login existed have
/// no Mode or Address in the file and load as Cloud.
/// </summary>
public sealed record Branch(
    string Name,
    string Area,
    string CloudId,
    int Port,
    string Username,
    IReadOnlyList<Camera> Cameras,
    LoginMode Mode = LoginMode.Cloud,
    string Address = "")
{
    public const int DefaultPort = 34567;

    /// <summary>True once the cameras have been read from the DVR itself.</summary>
    public bool CamerasDetected => Cameras.Count > 0;

    /// <summary>Identifies where this branch connects to. Two branches with the same key are the same DVR.</summary>
    [JsonIgnore]
    public string ConnectionKey => Mode == LoginMode.Ip
        ? $"ip|{Address.Trim().ToLowerInvariant()}|{Port}"
        : $"cloud|{CloudId.Trim().ToLowerInvariant()}";

    /// <summary>For the activity log, e.g. "connecting through the cloud..." or "connecting to 192.168.1.20:34567...".</summary>
    [JsonIgnore]
    public string ConnectingText => Mode == LoginMode.Ip ? Loc.F("connecting to {0}:{1}...", Address, Port) : Loc.T("connecting through the cloud...");

    /// <summary>"Cloud ID xxxx" or "192.168.1.20:34567", for messages.</summary>
    [JsonIgnore]
    public string ConnectionText => Mode == LoginMode.Ip ? $"{Address}:{Port}" : Loc.F("cloud ID {0}", CloudId);
}