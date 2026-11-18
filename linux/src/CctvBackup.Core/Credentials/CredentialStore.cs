using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CctvBackup.Core.Settings;

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
/// Cross-platform AES-256-GCM credential encryption for Linux (and macOS).
/// Uses a per-user 256-bit key stored in ~/.config/CctvBackup/.key with Unix 0600 permissions (owner read/write only).
/// </summary>
public sealed class LinuxFileProtector : ISecretProtector
{
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("CctvBackup.Linux.v1");
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly string _keyPath;

    public LinuxFileProtector(string? keyPath = null)
    {
        _keyPath = keyPath ?? AppPaths.KeyFile;
    }

    public byte[] Protect(byte[] data)
    {
        byte[] key = GetOrCreateKey();
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        byte[] cipher = new byte[data.Length];
        byte[] tag = new byte[TagBytes];

        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, data, cipher, tag, AssociatedData);

        byte[] output = new byte[NonceBytes + TagBytes + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceBytes);
        Buffer.BlockCopy(tag, 0, output, NonceBytes, TagBytes);
        Buffer.BlockCopy(cipher, 0, output, NonceBytes + TagBytes, cipher.Length);
        CryptographicOperations.ZeroMemory(key);
        return output;
    }

    public byte[] Unprotect(byte[] data)
    {
        if (data.Length < NonceBytes + TagBytes)
            throw new CryptographicException("Encrypted credential payload is truncated.");

        byte[] key = GetOrCreateKey();
        try
        {
            var nonce = data.AsSpan(0, NonceBytes);
            var tag = data.AsSpan(NonceBytes, TagBytes);
            var cipher = data.AsSpan(NonceBytes + TagBytes);
            byte[] plain = new byte[cipher.Length];

            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(nonce, cipher, tag, plain, AssociatedData);
            return plain;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] GetOrCreateKey()
    {
        string? dir = Path.GetDirectoryName(_keyPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (File.Exists(_keyPath))
        {
            byte[] existing = File.ReadAllBytes(_keyPath);
            if (existing.Length == KeyBytes) return existing;
        }

        byte[] key = RandomNumberGenerator.GetBytes(KeyBytes);
        string tmp = _keyPath + ".tmp";
        File.WriteAllBytes(tmp, key);
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch (Exception) { /* filesystem may not support Unix modes */ }
        }
        File.Move(tmp, _keyPath, overwrite: true);
        return key;
    }
}

/// <summary>
/// One login per branch, kept in a single encrypted file. Nothing sensitive is written in clear text.
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
        _protector = protector ?? CreateDefaultProtector(path);
        Load();
    }

    private static ISecretProtector CreateDefaultProtector(string credentialPath)
    {
        if (OperatingSystem.IsWindows())
            return new DpapiProtector();
        string dir = Path.GetDirectoryName(Path.GetFullPath(credentialPath)) ?? AppPaths.DataDirectory;
        return new LinuxFileProtector(Path.Combine(dir, ".key"));
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
            // Unreadable (e.g. file copied from another user): start empty rather than crash.
            _data = new();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(_data);
        var tmp = _path + ".tmp";
        File.WriteAllBytes(tmp, _protector.Protect(plain));
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch (Exception) { }
        }
        File.Move(tmp, _path, overwrite: true);
    }
}
