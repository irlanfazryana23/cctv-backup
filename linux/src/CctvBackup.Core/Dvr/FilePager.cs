using CctvBackup.Core.Sdk;

namespace CctvBackup.Core.Dvr;

/// <summary>
/// The DVR returns a limited number of files per search. This walks forward through time until
/// the whole range has been listed, removing duplicates.
/// </summary>
public static class FilePager
{
    public static List<RemoteFile> FindAll(
        Func<DateTime, DateTime, IReadOnlyList<RemoteFile>> query,
        DateTime from, DateTime to, int pageSize, CancellationToken ct = default)
    {
        var all = new List<RemoteFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        DateTime cursor = from;

        // Hard cap so a misbehaving DVR can never trap us in an endless loop.
        for (int guard = 0; guard < 10_000; guard++)
        {
            ct.ThrowIfCancellationRequested();
            var page = query(cursor, to);
            int added = 0;
            foreach (var f in page)
            {
                if (seen.Add(f.RemoteName + "|" + f.Begin.Ticks)) { all.Add(f); added++; }
            }

            // A short page means we reached the end of the range.
            if (page.Count < pageSize || added == 0) break;

            DateTime next = page.Max(f => f.End);
            // Always move forward, even if the DVR reports odd end times.
            cursor = next > cursor ? next : cursor.AddSeconds(1);
            if (cursor >= to) break;
        }

        return all.OrderBy(f => f.Begin).ThenBy(f => f.RemoteName, StringComparer.Ordinal).ToList();
    }
}
