using CctvBackup.Core.Auth;
using Xunit;

namespace CctvBackup.Tests;

public class AppAuthStoreTests
{
    [Fact]
    public void Configure_CreatesValidStore_AndGeneratesRecoveryKey()
    {
        using var dir = new TempDir();
        string authFile = Path.Combine(dir.Path, "auth.json");

        Assert.False(AppAuthStore.IsConfigured(authFile));

        string recoveryKey = AppAuthStore.Configure("admin", "secret123", authFile);

        Assert.True(AppAuthStore.IsConfigured(authFile));
        Assert.Equal("admin", AppAuthStore.GetUsername(authFile));
        Assert.StartsWith("CCTV-", recoveryKey);
        Assert.Matches(@"^CCTV-[2-9A-Z]{4}-[2-9A-Z]{4}$", recoveryKey);
    }

    [Fact]
    public void VerifyLogin_SucceedsWithCorrectCredentials_AndFailsWithIncorrect()
    {
        using var dir = new TempDir();
        string authFile = Path.Combine(dir.Path, "auth.json");

        AppAuthStore.Configure("admin", "correctPassword", authFile);

        Assert.True(AppAuthStore.VerifyLogin("admin", "correctPassword", authFile));
        Assert.True(AppAuthStore.VerifyLogin("ADMIN", "correctPassword", authFile)); // Case-insensitive username
        Assert.False(AppAuthStore.VerifyLogin("admin", "wrongPassword", authFile));
        Assert.False(AppAuthStore.VerifyLogin("otherUser", "correctPassword", authFile));
        Assert.False(AppAuthStore.VerifyLogin("", "", authFile));
    }

    [Fact]
    public void ResetWithRecoveryKey_ResetsPasswordSuccessfully()
    {
        using var dir = new TempDir();
        string authFile = Path.Combine(dir.Path, "auth.json");

        string recoveryKey = AppAuthStore.Configure("admin", "oldPass", authFile);

        // Fail with wrong key
        Assert.False(AppAuthStore.ResetWithRecoveryKey("CCTV-0000-0000", "newPass", customPath: authFile));
        Assert.True(AppAuthStore.VerifyLogin("admin", "oldPass", authFile));

        // Succeed with correct key
        Assert.True(AppAuthStore.ResetWithRecoveryKey(recoveryKey, "newPass", "admin", authFile));
        Assert.False(AppAuthStore.VerifyLogin("admin", "oldPass", authFile));
        Assert.True(AppAuthStore.VerifyLogin("admin", "newPass", authFile));
    }

    [Fact]
    public void BranchChallengeResponse_ComputesAndVerifiesOtpSuccessfully()
    {
        using var dir = new TempDir();
        string authFile = Path.Combine(dir.Path, "auth.json");

        AppAuthStore.Configure("branchUser", "initialPass", authFile);

        string challenge = AppAuthStore.GenerateBranchChallenge();
        Assert.Matches(@"^\d{3}-\d{3}$", challenge);

        string otp = AppAuthStore.GenerateOtpResponse(challenge);
        Assert.Matches(@"^\d{3}-\d{3}$", otp);

        // Verify valid OTP
        Assert.True(AppAuthStore.VerifyOtpResponse(challenge, otp));
        Assert.True(AppAuthStore.VerifyOtpResponse(challenge, otp.Replace("-", "")));

        // Reject invalid OTP
        Assert.False(AppAuthStore.VerifyOtpResponse(challenge, "999-999"));

        // Reset with OTP
        Assert.True(AppAuthStore.ResetWithOtpResponse(challenge, otp, "brandNewPass", "branchUser", authFile));
        Assert.True(AppAuthStore.VerifyLogin("branchUser", "brandNewPass", authFile));
    }

    [Fact]
    public void ChangePassword_RequiresCurrentPassword()
    {
        using var dir = new TempDir();
        string authFile = Path.Combine(dir.Path, "auth.json");

        AppAuthStore.Configure("admin", "myCurrentPass", authFile);

        // Fail with wrong current password
        Assert.False(AppAuthStore.ChangePassword("wrongPass", "updatedPass", customPath: authFile));
        Assert.True(AppAuthStore.VerifyLogin("admin", "myCurrentPass", authFile));

        // Succeed with correct current password
        Assert.True(AppAuthStore.ChangePassword("myCurrentPass", "updatedPass", customPath: authFile));
        Assert.False(AppAuthStore.VerifyLogin("admin", "myCurrentPass", authFile));
        Assert.True(AppAuthStore.VerifyLogin("admin", "updatedPass", authFile));
    }
}
