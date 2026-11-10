namespace CctvBackup.Core.Settings;

/// <summary>Appends log lines to one file per day and removes old ones. Thread-safe. Never receives secrets.</summary>
public sealed class FileLog
{
    private readonly string _dir;
    private readonly object _gate = new();

    public FileLog(string directory, int keepDays = 30)
    {
        _dir = directory;
        try
        {
            Directory.CreateDirectory(_dir);
            foreach (var f in Directory.GetFiles(_dir, "*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-keepDays)) File.Delete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Write(string line)
    {
        try
        {
            lock (_gate)
                File.AppendAllText(Path.Combine(_dir, $"{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // logging must never take the app down
        }
    }
}
