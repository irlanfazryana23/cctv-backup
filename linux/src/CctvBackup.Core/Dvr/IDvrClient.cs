using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Sdk;

namespace CctvBackup.Core.Dvr;

/// <summary>What the DVR reports about itself at login. <see cref="ChannelCount"/> is 0 when it could not be read.</summary>
public sealed record DvrInfo(int ChannelCount, string SerialNumber, string Model);

/// <summary>An open session with one branch DVR.</summary>
public interface IDvrConnection : IDisposable
{
    /// <summary>Details reported by the DVR, including how many cameras it has.</summary>
    DvrInfo Info { get; }

    /// <summary>All recordings of a channel (0-based) overlapping [from, to], in time order.</summary>
    Task<IReadOnlyList<RemoteFile>> FindFilesAsync(int channel, DateTime from, DateTime to, CancellationToken ct);

    /// <summary>Downloads one recording, in the DVR's native format, to <paramref name="destPath"/>.</summary>
    Task DownloadAsync(RemoteFile file, string destPath, IProgress<int>? progress, CancellationToken ct);
}

/// <summary>Opens sessions to branch DVRs. The real one goes through the vendor SDK; tests use a fake.</summary>
public interface IDvrClient
{
    Task<IDvrConnection> ConnectAsync(Branch branch, Credential credential, CancellationToken ct);

    /// <summary>
    /// Throws away any state the vendor driver keeps between sessions (the same as restarting the program).
    /// Does nothing while a session is still open. Clients without such state can ignore it.
    /// </summary>
    void ResetDriver() { }
}

/// <summary>A failure talking to a DVR that has a message safe to show to the user.</summary>
public sealed class DvrException : Exception
{
    public DvrException(string message, Exception? inner = null) : base(message, inner) { }
}
