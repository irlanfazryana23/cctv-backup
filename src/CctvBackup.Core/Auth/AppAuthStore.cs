using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CctvBackup.Core.Settings;

namespace CctvBackup.Core.Auth;

/// <summary>
/// Handles application-level authentication (username, password, recovery key, and branch IT challenge-response OTP)
/// using PBKDF2 with SHA-256 and salted cryptographic hashing.
/// </summary>
public static partial class AppAuthStore
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    // Default development key placeholder. Supply a partial implementation of OverrideMasterSecret
    // inside a gitignored *.local.cs file before building production releases.
    private const string DefaultDevSecret = "CctvBackup-Dev-Sample-Key-Change-Before-Release";
    static partial void OverrideMasterSecret(ref string secret);

    private static byte[] GetMasterSecretBytes()
    {
        string secret = DefaultDevSecret;
        OverrideMasterSecret(ref secret);
        return Encoding.UTF8.GetBytes(secret);
    }

    private sealed class AuthPayload
    {
        public string Username { get; set; } = "admin";
        public string SaltBase64 { get; set; } = string.Empty;
        public string HashBase64 { get; set; } = string.Empty;
        public string RecoverySaltBase64 { get; set; } = string.Empty;
        public string RecoveryHashBase64 { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastPasswordChangedUtc { get; set; }
    }

    private static string GetFilePath(string? customPath) =>
        customPath ?? AppPaths.AuthFile;

    public static bool IsConfigured(string? customPath = null)
    {
        string file = GetFilePath(customPath);
        if (!File.Exists(file)) return false;
        try
        {
            var payload = LoadPayload(file);
            return payload != null &&
                   !string.IsNullOrWhiteSpace(payload.Username) &&
                   !string.IsNullOrWhiteSpace(payload.HashBase64) &&
                   !string.IsNullOrWhiteSpace(payload.RecoveryHashBase64);
        }
        catch
        {
            return false;
        }
    }

    public static string? GetUsername(string? customPath = null)
    {
        string file = GetFilePath(customPath);
        if (!File.Exists(file)) return null;
        try
        {
            var payload = LoadPayload(file);
            return payload?.Username;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Configures the initial username and password, generates a new Master Recovery Key,
    /// and saves to the auth configuration file.
    /// </summary>
    /// <returns>The generated Master Recovery Key (e.g. CCTV-8924-1832).</returns>
    public static string Configure(string username, string password, string? customPath = null)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username cannot be empty.", nameof(username));
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password cannot be empty.", nameof(password));

        string recoveryKey = GenerateRecoveryKey();

        var (pwdSalt, pwdHash) = HashSecret(password);
        var (recSalt, recHash) = HashSecret(NormalizeSecret(recoveryKey));

        var payload = new AuthPayload
        {
            Username = username.Trim(),
            SaltBase64 = Convert.ToBase64String(pwdSalt),
            HashBase64 = Convert.ToBase64String(pwdHash),
            RecoverySaltBase64 = Convert.ToBase64String(recSalt),
            RecoveryHashBase64 = Convert.ToBase64String(recHash),
            CreatedAtUtc = DateTime.UtcNow,
            LastPasswordChangedUtc = DateTime.UtcNow
        };

        SavePayload(payload, GetFilePath(customPath));
        return recoveryKey;
    }

    /// <summary>
    /// Verifies username and password against the stored credentials.
    /// </summary>
    public static bool VerifyLogin(string username, string password, string? customPath = null)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return false;

        string file = GetFilePath(customPath);
        if (!File.Exists(file)) return false;

        try
        {
            var payload = LoadPayload(file);
            if (payload == null) return false;

            if (!string.Equals(payload.Username.Trim(), username.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;

            byte[] salt = Convert.FromBase64String(payload.SaltBase64);
            byte[] expectedHash = Convert.FromBase64String(payload.HashBase64);

            return VerifyHash(password, salt, expectedHash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resets the password using the Master Recovery Key.
    /// </summary>
    public static bool ResetWithRecoveryKey(string recoveryKey, string newPassword, string? newUsername = null, string? customPath = null)
    {
        if (string.IsNullOrWhiteSpace(recoveryKey) || string.IsNullOrEmpty(newPassword))
            return false;

        string file = GetFilePath(customPath);
        if (!File.Exists(file)) return false;

        try
        {
            var payload = LoadPayload(file);
            if (payload == null) return false;

            byte[] recSalt = Convert.FromBase64String(payload.RecoverySaltBase64);
            byte[] expectedRecHash = Convert.FromBase64String(payload.RecoveryHashBase64);

            string normalizedKey = NormalizeSecret(recoveryKey);
            if (!VerifyHash(normalizedKey, recSalt, expectedRecHash))
                return false;

            var (newPwdSalt, newPwdHash) = HashSecret(newPassword);
            payload.SaltBase64 = Convert.ToBase64String(newPwdSalt);
            payload.HashBase64 = Convert.ToBase64String(newPwdHash);
            payload.LastPasswordChangedUtc = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(newUsername))
            {
                payload.Username = newUsername.Trim();
            }

            SavePayload(payload, file);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Generates a 6-digit challenge code that the branch user can read to the Main Office IT admin over the phone.
    /// </summary>
    public static string GenerateBranchChallenge()
    {
        int code = RandomNumberGenerator.GetInt32(100_000, 1_000_000);
        return $"{code / 1000:D3}-{code % 1000:D3}";
    }

    /// <summary>
    /// Computes the expected Response OTP for a branch challenge during verification.
    /// </summary>
    internal static string GenerateOtpResponse(string challenge)
    {
        string clean = challenge.Replace("-", "").Trim();
        using var hmac = new HMACSHA256(GetMasterSecretBytes());
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(clean));
        uint num = BitConverter.ToUInt32(hash, 0);
        uint otp = (num % 900_000) + 100_000;
        return $"{otp / 1000:D3}-{otp % 1000:D3}";
    }

    /// <summary>
    /// Verifies if the Admin Response OTP matches the branch challenge.
    /// </summary>
    public static bool VerifyOtpResponse(string challenge, string responseOtp)
    {
        if (string.IsNullOrWhiteSpace(challenge) || string.IsNullOrWhiteSpace(responseOtp))
            return false;

        string expected = GenerateOtpResponse(challenge).Replace("-", "").Trim();
        string actual = responseOtp.Replace("-", "").Trim();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>
    /// Resets the password using the Remote Branch Challenge-Response OTP verified with Main Office IT.
    /// </summary>
    public static bool ResetWithOtpResponse(string challenge, string responseOtp, string newPassword, string? newUsername = null, string? customPath = null)
    {
        if (!VerifyOtpResponse(challenge, responseOtp))
            return false;

        if (string.IsNullOrEmpty(newPassword))
            return false;

        string file = GetFilePath(customPath);
        if (!File.Exists(file)) return false;

        try
        {
            var payload = LoadPayload(file);
            if (payload == null) return false;

            var (newPwdSalt, newPwdHash) = HashSecret(newPassword);
            payload.SaltBase64 = Convert.ToBase64String(newPwdSalt);
            payload.HashBase64 = Convert.ToBase64String(newPwdHash);
            payload.LastPasswordChangedUtc = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(newUsername))
            {
                payload.Username = newUsername.Trim();
            }

            SavePayload(payload, file);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Changes the password after confirming the current password.
    /// </summary>
    public static bool ChangePassword(string currentPassword, string newPassword, string? newUsername = null, string? customPath = null)
    {
        if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
            return false;

        string file = GetFilePath(customPath);
        if (!File.Exists(file)) return false;

        try
        {
            var payload = LoadPayload(file);
            if (payload == null) return false;

            byte[] salt = Convert.FromBase64String(payload.SaltBase64);
            byte[] expectedHash = Convert.FromBase64String(payload.HashBase64);

            if (!VerifyHash(currentPassword, salt, expectedHash))
                return false;

            var (newPwdSalt, newPwdHash) = HashSecret(newPassword);
            payload.SaltBase64 = Convert.ToBase64String(newPwdSalt);
            payload.HashBase64 = Convert.ToBase64String(newPwdHash);
            payload.LastPasswordChangedUtc = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(newUsername))
            {
                payload.Username = newUsername.Trim();
            }

            SavePayload(payload, file);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Clear(string? customPath = null)
    {
        string file = GetFilePath(customPath);
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// Interactive CLI reset when invoked with --reset-password on the terminal.
    /// </summary>
    public static void ResetFromConsole()
    {
        Console.WriteLine();
        Console.WriteLine("==================================================");
        Console.WriteLine("     CCTV Backup — Admin Account Reset Tool       ");
        Console.WriteLine("==================================================");
        Console.WriteLine();

        string currentUsername = GetUsername() ?? "admin";
        Console.Write($"Enter Admin Username [{currentUsername}]: ");
        string? inputUser = Console.ReadLine();
        string username = string.IsNullOrWhiteSpace(inputUser) ? currentUsername : inputUser.Trim();

        string password = ReadPasswordFromConsole("Enter New Password: ");
        if (string.IsNullOrEmpty(password))
        {
            Console.WriteLine("\nError: Password cannot be empty. Aborted.");
            return;
        }

        string confirm = ReadPasswordFromConsole("Confirm New Password: ");
        if (password != confirm)
        {
            Console.WriteLine("\nError: Passwords do not match. Aborted.");
            return;
        }

        string recoveryKey = Configure(username, password);

        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine("Success! Account configured successfully.");
        Console.WriteLine($"Username: {username}");
        Console.WriteLine($"Master Recovery Key: {recoveryKey}");
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine("IMPORTANT: Store your Recovery Key safely.");
        Console.WriteLine("You can now start CCTV Backup.");
        Console.WriteLine();
    }

    private static string ReadPasswordFromConsole(string prompt)
    {
        Console.Write(prompt);
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                {
                    sb.Remove(sb.Length - 1, 1);
                    Console.Write("\b \b");
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
                Console.Write("*");
            }
        }
        return sb.ToString();
    }

    private static (byte[] Salt, byte[] Hash) HashSecret(string secret)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret),
            salt,
            Iterations,
            HashAlgorithm,
            HashBytes);
        return (salt, hash);
    }

    private static bool VerifyHash(string secret, byte[] salt, byte[] expectedHash)
    {
        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret),
            salt,
            Iterations,
            HashAlgorithm,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static string NormalizeSecret(string secret) =>
        secret.Trim().Replace("-", "").ToUpperInvariant();

    private static string GenerateRecoveryKey()
    {
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"; // 32 characters, no ambiguous chars
        byte[] random = RandomNumberGenerator.GetBytes(8);
        var part1 = new char[4];
        var part2 = new char[4];
        for (int i = 0; i < 4; i++)
        {
            part1[i] = alphabet[random[i] % alphabet.Length];
            part2[i] = alphabet[random[i + 4] % alphabet.Length];
        }
        return $"CCTV-{new string(part1)}-{new string(part2)}";
    }

    private static AuthPayload? LoadPayload(string file)
    {
        string json = File.ReadAllText(file);
        return JsonSerializer.Deserialize<AuthPayload>(json);
    }

    private static void SavePayload(AuthPayload payload, string targetFile)
    {
        string dir = Path.GetDirectoryName(targetFile)!;
        Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        string tmp = targetFile + ".tmp";
        File.WriteAllText(tmp, json);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { }
        }

        File.Move(tmp, targetFile, overwrite: true);
    }
}
