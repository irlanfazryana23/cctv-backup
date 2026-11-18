using System.Text.Json;

namespace CctvBackup.Core.Backup;

public sealed record ManifestEntry(string RemoteName, string LocalFile, long Bytes, DateTime CompletedAtUtc);

/// <summary>A small record of what was backed up, kept next to the footage (for audit, not for resume).</summary>
public static class BackupManifest
{
    public const string FileName = "_backup-manifest.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static void Append(string branchFolder, ManifestEntry entry)
    {
        string path = Path.Combine(branchFolder, FileName);
        var list = new List<ManifestEntry>();
        try
        {
            if (File.Exists(path))
                list = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(path)) ?? new();
        }
        catch (JsonException) { /* start a fresh manifest rather than lose the backup */ }

        list.RemoveAll(e => e.LocalFile == entry.LocalFile);
        list.Add(entry);

        Directory.CreateDirectory(branchFolder);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
