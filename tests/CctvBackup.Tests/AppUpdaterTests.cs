using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CctvBackup.Core.Updates;

namespace CctvBackup.Tests;

public class AppUpdaterTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<(Uri Uri, AuthenticationHeaderValue? Auth, string? Accept)> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((
                request.RequestUri!,
                request.Headers.Authorization,
                string.Join(",", request.Headers.Accept.Select(a => a.MediaType))));
            return Task.FromResult(_responder(request));
        }
    }

    [Theory]
    [InlineData("v1.0.1", 1, 0, 1, 0)]
    [InlineData("V2.3.4", 2, 3, 4, 0)]
    [InlineData("1.5", 1, 5, 0, 0)]
    [InlineData("v1.2.3+build99", 1, 2, 3, 0)]
    public void TryParseReleaseVersion_ParsesValidTags(string tag, int maj, int min, int bld, int rev)
    {
        var v = AppUpdater.TryParseReleaseVersion(tag);
        Assert.Equal(new Version(maj, min, bld, rev), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1.0.1-beta")]
    [InlineData("release-latest")]
    public void TryParseReleaseVersion_RejectsInvalidOrPrereleaseTags(string? tag)
    {
        Assert.Null(AppUpdater.TryParseReleaseVersion(tag));
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNewerReleaseWithAuthHeader()
    {
        const string json = """
        {
          "tag_name": "v1.0.2",
          "draft": false,
          "prerelease": false,
          "body": "Fixed branch sync and UI alignment.",
          "assets": [
            {
              "name": "notes.txt",
              "url": "https://api.github.com/repos/owner/private-rel/releases/assets/10",
              "size": 120
            },
            {
              "name": "CCTV-Backup-Setup-1.0.2.exe",
              "url": "https://api.github.com/repos/owner/private-rel/releases/assets/99",
              "size": 50000000,
              "digest": "sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"
            }
          ]
        }
        """;

        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var release = await AppUpdater.CheckForUpdateAsync(
            "owner/private-rel",
            "github_pat_test_secret",
            new Version(1, 0, 1),
            handler);

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 0, 2, 0), release!.Version);
        Assert.Equal("v1.0.2", release.TagName);
        Assert.Equal("CCTV-Backup-Setup-1.0.2.exe", release.AssetName);
        Assert.Equal("https://api.github.com/repos/owner/private-rel/releases/assets/99", release.AssetApiUrl);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", release.Sha256Digest);

        Assert.Single(handler.Requests);
        Assert.Equal("Bearer", handler.Requests[0].Auth?.Scheme);
        Assert.Equal("github_pat_test_secret", handler.Requests[0].Auth?.Parameter);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNullWhenCurrentVersionMatches()
    {
        const string json = """
        {
          "tag_name": "v1.0.1",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "CCTV-Backup-Setup-1.0.1.exe", "url": "https://api.github.com/repos/o/r/releases/assets/1", "size": 100 }
          ]
        }
        """;

        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var release = await AppUpdater.CheckForUpdateAsync("o/r", "tok", new Version(1, 0, 1, 0), handler);
        Assert.Null(release);
    }

    [Fact]
    public async Task CheckForUpdateAsync_SkipsHttpWhenPlaceholderOrEmpty()
    {
        using var handler = new StubHandler(_ => throw new InvalidOperationException("Should not call HTTP"));

        Assert.Null(await AppUpdater.CheckForUpdateAsync("", "", new Version(1, 0, 0), handler));
        Assert.Null(await AppUpdater.CheckForUpdateAsync("o/r", "github_pat_CHANGE-ME", new Version(1, 0, 0), handler));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DownloadInstallerAsync_StripsAuthOnRedirectAndVerifiesSha256()
    {
        byte[] payload = Encoding.UTF8.GetBytes("FAKE-INNO-SETUP-PAYLOAD-DATA-1234567890");
        string shaHex = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        using var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.Host == "api.github.com")
            {
                var redir = new HttpResponseMessage(HttpStatusCode.Found);
                redir.Headers.Location = new Uri("https://release-assets.githubusercontent.com/signed-object?sig=abc");
                return redir;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
        });

        using var dir = new TempDir();
        string dest = Path.Combine(dir.Path, "CCTV-Backup-Setup-1.0.2.exe");
        var release = new ReleaseInfo(
            new Version(1, 0, 2, 0),
            "v1.0.2",
            "CCTV-Backup-Setup-1.0.2.exe",
            "https://api.github.com/repos/o/r/releases/assets/77",
            payload.Length,
            shaHex,
            "notes");

        await AppUpdater.DownloadInstallerAsync(release, "github_pat_secret", dest, null, handler);

        Assert.True(File.Exists(dest));
        Assert.Equal(payload, File.ReadAllBytes(dest));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("github_pat_secret", handler.Requests[0].Auth?.Parameter);
        Assert.Contains("application/octet-stream", handler.Requests[0].Accept ?? "");
        Assert.Null(handler.Requests[1].Auth); // Never leak GitHub PAT to storage redirect target
    }

    [Fact]
    public async Task DownloadInstallerAsync_ThrowsAndCleansUpOnShaMismatch()
    {
        byte[] payload = Encoding.UTF8.GetBytes("corrupted-installer-bytes");
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });

        using var dir = new TempDir();
        string dest = Path.Combine(dir.Path, "CCTV-Backup-Setup-1.0.2.exe");
        var release = new ReleaseInfo(
            new Version(1, 0, 2, 0),
            "v1.0.2",
            "CCTV-Backup-Setup-1.0.2.exe",
            "https://api.github.com/repos/o/r/releases/assets/77",
            payload.Length,
            new string('0', 64),
            "notes");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AppUpdater.DownloadInstallerAsync(release, "tok", dest, null, handler));

        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".part"));
    }
}
