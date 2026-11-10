using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CctvBackup.Core.Credentials;

public sealed record Credential(string Username, string Password);

/// <summary>Encrypts/decrypts the credential file. Swappable so tests do not depend on the OS.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

/// <summary>Windows DPAPI, bound to the current Windows user.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DpapiProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CctvBackup.v1");

    public byte[] Protect(byte[] data) =>
        ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) =>
        ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>
/// One login per branch, kept in a single DPAPI-encrypted file. Nothing sensitive is written in clear text.
/// </summary>
public sealed class CredentialStore
{
    private sealed class Payload
    {
        // Property name kept from the first version so existing files still load.
        public Dictionary<string, Credential> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly string _path;
    private readonly ISecretProtector _protector;
    private Payload _data = new();

    public CredentialStore(string path, ISecretProtector? protector = null)
    {
        _path = path;
        _protector = protector ?? (OperatingSystem.IsWindows()
            ? new DpapiProtector()
            : throw new PlatformNotSupportedException("Credential encryption requires Windows."));
        Load();
    }

    public Credential? Get(string branchName) =>
        _data.Overrides.TryGetValue(branchName, out var c) ? c : null;

    public void Set(string branchName, Credential credential)
    {
        _data.Overrides[branchName] = credential;
        Save();
    }

    public bool Remove(string branchName)
    {
        bool removed = _data.Overrides.Remove(branchName);
        if (removed) Save();
        return removed;
    }

    /// <summary>Moves a saved login to a new branch name (used when a branch is renamed).</summary>
    public void Rename(string oldName, string newName)
    {
        if (_data.Overrides.Remove(oldName, out var c))
        {
            _data.Overrides[newName] = c;
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var plain = _protector.Unprotect(File.ReadAllBytes(_path));
            _data = JsonSerializer.Deserialize<Payload>(plain) ?? new();
            _data.Overrides = new Dictionary<string, Credential>(_data.Overrides, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or JsonException)
        {
            // Unreadable (e.g. file copied from another Windows user): start empty rather than crash.
            _data = new();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(_data);
        var tmp = _path + ".tmp";
        File.WriteAllBytes(tmp, _protector.Protect(plain));
        File.Move(tmp, _path, overwrite: true);
    }
}
