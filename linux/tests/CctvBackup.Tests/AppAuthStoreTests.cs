using CctvBackup.Core.Auth;
using Xunit;

namespace CctvBackup.Tests;

public sealed class AppAuthStoreTests : IDisposable
{
    private readonly string _testFile;

    public AppAuthStoreTests()
    {
        _testFile = Path.Combine(Path.GetTempPath(), $"cctv_auth_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_testFile))
        {
            try { File.Delete(_testFile); } catch { }
        }
    }

    [Fact]
    public void IsConfigured_ReturnsFalse_WhenFileDoesNotExist()
    {
        Assert.False(AppAuthStore.IsConfigured(_testFile));
    }

    [Fact]
    public void Configure_CreatesValidAuth_AndReturnsRecoveryKey()
    {
        string recoveryKey = AppAuthStore.Configure("admin", "Secret123!", _testFile);

        Assert.True(AppAuthStore.IsConfigured(_testFile));
        Assert.StartsWith("CCTV-", recoveryKey);
        Assert.Equal("admin", AppAuthStore.GetUsername(_testFile));
    }

    [Fact]
    public void VerifyLogin_Succeeds_WithCorrectCredentials()
    {
        AppAuthStore.Configure("admin", "P@ssword1", _testFile);

        Assert.True(AppAuthStore.VerifyLogin("admin", "P@ssword1", _testFile));
        // Case insensitive username
        Assert.True(AppAuthStore.VerifyLogin("ADMIN", "P@ssword1", _testFile));
    }

    [Fact]
    public void VerifyLogin_Fails_WithWrongPasswordOrUsername()
    {
        AppAuthStore.Configure("admin", "P@ssword1", _testFile);

        Assert.False(AppAuthStore.VerifyLogin("admin", "WrongPass", _testFile));
        Assert.False(AppAuthStore.VerifyLogin("wronguser", "P@ssword1", _testFile));
        Assert.False(AppAuthStore.VerifyLogin("", "", _testFile));
    }

    [Fact]
    public void ResetWithRecoveryKey_AllowsResettingPassword()
    {
        string recoveryKey = AppAuthStore.Configure("admin", "OldPass123", _testFile);

        // Reset with correct recovery key (test case-insensitivity and hyphen flexibility)
        string inputKey = recoveryKey.ToLowerInvariant().Replace("-", "");
        bool ok = AppAuthStore.ResetWithRecoveryKey(inputKey, "NewPass456!", customPath: _testFile);
        Assert.True(ok);

        // Old password no longer works
        Assert.False(AppAuthStore.VerifyLogin("admin", "OldPass123", _testFile));
        // New password works
        Assert.True(AppAuthStore.VerifyLogin("admin", "NewPass456!", _testFile));
    }

    [Fact]
    public void ResetWithRecoveryKey_Fails_WithInvalidKey()
    {
        AppAuthStore.Configure("admin", "OldPass123", _testFile);

        bool ok = AppAuthStore.ResetWithRecoveryKey("CCTV-0000-0000", "NewPass456!", customPath: _testFile);
        Assert.False(ok);

        // Old password still works
        Assert.True(AppAuthStore.VerifyLogin("admin", "OldPass123", _testFile));
    }

    [Fact]
    public void ChangePassword_Succeeds_WhenCurrentPasswordIsCorrect()
    {
        AppAuthStore.Configure("admin", "Pass1", _testFile);

        bool changed = AppAuthStore.ChangePassword("Pass1", "Pass2", "superadmin", _testFile);
        Assert.True(changed);

        Assert.True(AppAuthStore.VerifyLogin("superadmin", "Pass2", _testFile));
        Assert.False(AppAuthStore.VerifyLogin("admin", "Pass1", _testFile));
    }
}
