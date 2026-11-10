using System.Text;

namespace CctvBackup.Core.Sdk;

/// <summary>
/// What the DVR reports about itself right after login (the SDK's H264_DVR_DEVICEINFO, 832 bytes).
/// Offsets were read from the CMS symbol files: serial number at 224, analog channels (byChanNum) at 288,
/// digital/IP channels (iDigChannel) at 324, model name at 424. Not yet checked against a live DVR.
/// </summary>
public sealed record DeviceDetails(string SerialNumber, int AnalogChannels, int DigitalChannels, string Model)
{
    public const int StructSize = 832;
    private const int SerialOffset = 224, SerialLength = 64;
    private const int AnalogOffset = 288, DigitalOffset = 324;
    private const int ModelOffset = 424, ModelLength = 16;
    public const int MaxSaneChannels = 64;

    /// <summary>Cameras the user can pick: analog inputs plus IP channels (hybrid recorders).</summary>
    public int TotalChannels
    {
        get
        {
            int total = Math.Max(0, AnalogChannels) + Math.Max(0, DigitalChannels);
            // A garbage value (e.g. layout mismatch) must not create hundreds of phantom cameras.
            return total is >= 1 and <= MaxSaneChannels ? total : 0;
        }
    }

    public static DeviceDetails Parse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < StructSize)
            throw new ArgumentException($"Device info buffer must be at least {StructSize} bytes.", nameof(buffer));

        return new DeviceDetails(
            AnsiString(buffer.Slice(SerialOffset, SerialLength)),
            BitConverter.ToInt32(buffer.Slice(AnalogOffset, 4)),
            BitConverter.ToInt32(buffer.Slice(DigitalOffset, 4)),
            AnsiString(buffer.Slice(ModelOffset, ModelLength)));
    }

    private static string AnsiString(ReadOnlySpan<byte> bytes)
    {
        int len = bytes.IndexOf((byte)0);
        if (len < 0) len = bytes.Length;
        return Encoding.ASCII.GetString(bytes[..len]).Trim();
    }
}
