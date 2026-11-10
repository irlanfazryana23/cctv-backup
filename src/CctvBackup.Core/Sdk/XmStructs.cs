using System.Runtime.InteropServices;

namespace CctvBackup.Core.Sdk;

// Layouts below were read from the CMS's own debug symbols (DownloadPlan.pdb / CMS.pdb),
// not guessed. Sizes: H264_DVR_TIME=24, SDK_SYSTEM_TIME=32, H264_DVR_FINDINFO=96,
// H264_DVR_FILE_DATA=188. All fields are 4-byte aligned, so Pack does not matter on x86.

/// <summary>H264_DVR_TIME (used in search requests).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct XmTime
{
    public int Year, Month, Day, Hour, Minute, Second;

    public static XmTime From(DateTime t) => new()
    {
        Year = t.Year, Month = t.Month, Day = t.Day,
        Hour = t.Hour, Minute = t.Minute, Second = t.Second,
    };
}

/// <summary>SDK_SYSTEM_TIME (used in file-list results).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct XmSystemTime
{
    public int Year, Month, Day, WeekDay, Hour, Minute, Second, IsDst;

    public readonly DateTime ToDateTime()
    {
        try { return new DateTime(Year, Month, Day, Hour, Minute, Second, DateTimeKind.Unspecified); }
        catch (ArgumentOutOfRangeException) { return DateTime.MinValue; }
    }

    public static XmSystemTime From(DateTime t) => new()
    {
        Year = t.Year, Month = t.Month, Day = t.Day, WeekDay = (int)t.DayOfWeek,
        Hour = t.Hour, Minute = t.Minute, Second = t.Second, IsDst = 0,
    };
}

/// <summary>H264_DVR_FINDINFO - 96 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct XmFindInfo
{
    public int Channel;          // nChannelN0 (0-based)
    public int FileType;         // SDK_File_Type; 0 = SDK_RECORD_ALL
    public XmTime StartTime;
    public XmTime EndTime;
    public fixed byte FileName[32];
    public IntPtr HWnd;
    public int StreamType;       // 0 = main stream
}

/// <summary>H264_DVR_FILE_DATA - 188 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct XmFileData
{
    public int Channel;          // ch
    public int Size;             // size, in KB as reported by the DVR
    public fixed byte FileName[108];
    public XmSystemTime BeginTime;
    public XmSystemTime EndTime;
    public IntPtr HWnd;
    public int StreamType;

    public readonly string GetFileName()
    {
        fixed (byte* p = FileName)
        {
            int len = 0;
            while (len < 108 && p[len] != 0) len++;
            return System.Text.Encoding.ASCII.GetString(p, len);
        }
    }
}

/// <summary>Managed view of one recorded file reported by the DVR.</summary>
public sealed record RemoteFile(
    int Channel,
    string RemoteName,
    DateTime Begin,
    DateTime End,
    long SizeKb,
    XmFileData Raw);
