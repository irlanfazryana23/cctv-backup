using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace CctvBackup.Core.Updates;

public sealed record ReleaseInfo(
    Version Version,
    string TagName,
    string AssetName,
    string AssetApiUrl,
    long AssetSizeBytes,
    string? Sha256Digest,
    string ReleaseNotes);

/// <summary>
/// Checks for newer releases in a configured GitHub repository (including private repositories
/// using a read-only fine-grained Personal Access Token) and downloads the Windows installer safely.
/// </summary>
public static partial class AppUpdater
{
    private const string GitHubApiVersion = "2022-11-28";

    // Supply a partial implementation of OverrideGitHubConfig inside a gitignored *.local.cs file
    // (e.g. AppUpdater.Config.local.cs) before building installers for branch distribution.
    static partial void OverrideGitHubConfig(ref string ownerRepo, ref string token);

    public static (string OwnerRepo, string Token) GetGitHubConfig()
    {
        string repo = string.Empty;
        string token = string.Empty;
        OverrideGitHubConfig(ref repo, ref token);
        return (repo.Trim().Trim('/'), token.Trim());
    }

    public static bool IsConfigured()
    {
        var (repo, token) = GetGitHubConfig();
        return !string.IsNullOrWhiteSpace(repo) &&
               repo.Contains('/') &&
               !repo.Contains("CHANGE-ME", StringComparison.OrdinalIgnoreCase) &&
               !token.Contains("CHANGE-ME", StringComparison.OrdinalIgnoreCase);
    }

    public static Version CurrentAppVersion()
    {
        var v = Assembly.GetEntryAssembly()?.GetName().Version
                ?? typeof(AppUpdater).Assembly.GetName().Version
                ?? new Version(1, 0, 0, 0);
        return NormalizeVersion(v);
    }

    public static Version NormalizeVersion(Version v) =>
        new(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));

    public static Version? TryParseReleaseVersion(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return null;
        string clean = tagName.Trim().TrimStart('v', 'V');

        // Ignore pre-release suffixes (e.g. -beta) and strip +metadata if present.
        int plusIdx = clean.IndexOf('+');
        if (plusIdx >= 0) clean = clean[..plusIdx];
        if (clean.Contains('-')) return null;

        return Version.TryParse(clean, out var parsed) ? NormalizeVersion(parsed) : null;
    }

    public static string FormatVersion(Version v)
    {
        var n = NormalizeVersion(v);
        return n.Revision > 0
            ? $"{n.Major}.{n.Minor}.{n.Build}.{n.Revision}"
            : $"{n.Major}.{n.Minor}.{n.Build}";
    }

    /// <summary>Background-safe update check: returns null silently if offline, unconfigured, or up to date.</summary>
    public static async Task<ReleaseInfo?> TryCheckForUpdateSilentAsync(Version? currentVersion = null, CancellationToken ct = default)
    {
        try
        {
            return await CheckForUpdateAsync(currentVersion, null, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<ReleaseInfo?> CheckForUpdateAsync(
        Version? currentVersion = null,
        HttpMessageHandler? handler = null,
        CancellationToken ct = default)
    {
        var (ownerRepo, token) = GetGitHubConfig();
        return await CheckForUpdateAsync(ownerRepo, token, currentVersion ?? CurrentAppVersion(), handler, ct).ConfigureAwait(false);
    }

    internal static async Task<ReleaseInfo?> CheckForUpdateAsync(
        string ownerRepo,
        string token,
        Version currentVersion,
        HttpMessageHandler? handler = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerRepo) ||
            !ownerRepo.Contains('/') ||
            ownerRepo.Contains("CHANGE-ME", StringComparison.OrdinalIgnoreCase) ||
            token.Contains("CHANGE-ME", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Version current = NormalizeVersion(currentVersion);

        using var http = CreateGitHubClient(handler, current, TimeSpan.FromSeconds(8));
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{ownerRepo}/releases/latest");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        return ParseLatestRelease(doc.RootElement, current);
    }

    internal static ReleaseInfo? ParseLatestRelease(JsonElement root, Version currentVersion)
    {
        if (root.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var preProp) && preProp.GetBoolean()) return null;

        string tagName = root.TryGetProperty("tag_name", out var tagProp) ? (tagProp.GetString() ?? string.Empty) : string.Empty;
        Version? latest = TryParseReleaseVersion(tagName);
        if (latest is null || latest <= NormalizeVersion(currentVersion)) return null;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        ReleaseInfo? candidate = null;
        string notes = root.TryGetProperty("body", out var bodyProp) ? (bodyProp.GetString() ?? string.Empty).Trim() : string.Empty;

        foreach (var asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? string.Empty) : string.Empty;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            string apiUrl = asset.TryGetProperty("url", out var urlProp) ? (urlProp.GetString() ?? string.Empty) : string.Empty;
            if (string.IsNullOrWhiteSpace(apiUrl) && asset.TryGetProperty("browser_download_url", out var bProp))
                apiUrl = bProp.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiUrl)) continue;

            long size = asset.TryGetProperty("size", out var sProp) && sProp.TryGetInt64(out long sz) ? sz : 0;
            string? digest = asset.TryGetProperty("digest", out var dProp) ? dProp.GetString() : null;

            var info = new ReleaseInfo(latest, tagName, name, apiUrl, size, NormalizeSha256(digest), notes);
            if (name.StartsWith("CCTV-Backup-Setup-", StringComparison.OrdinalIgnoreCase))
                return info;

            candidate ??= info;
        }

        return candidate;
    }

    public static Task DownloadInstallerAsync(
        ReleaseInfo release,
        string destinationPath,
        IProgress<(long BytesReceived, long TotalBytes)>? progress = null,
        HttpMessageHandler? handler = null,
        CancellationToken ct = default)
    {
        var (_, token) = GetGitHubConfig();
        return DownloadInstallerAsync(release, token, destinationPath, progress, handler, ct);
    }

    internal static async Task DownloadInstallerAsync(
        ReleaseInfo release,
        string token,
        string destinationPath,
        IProgress<(long BytesReceived, long TotalBytes)>? progress = null,
        HttpMessageHandler? handler = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("Destination path required.", nameof(destinationPath));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        string partPath = destinationPath + ".part";
        if (File.Exists(partPath)) File.Delete(partPath);

        using var ownHandler = handler is null ? new HttpClientHandler { AllowAutoRedirect = false } : null;
        using var http = new HttpClient(handler ?? ownHandler!, disposeHandler: false)
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"CctvBackup/{FormatVersion(CurrentAppVersion())}");

        try
        {
            using var resp = await SendAssetDownloadRequestAsync(http, release.AssetApiUrl, token, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? release.AssetSizeBytes;
            long received = 0;

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var remote = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var local = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
            {
                byte[] buffer = new byte[65536];
                int read;
                while ((read = await remote.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await local.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    sha.AppendData(buffer, 0, read);
                    received += read;
                    progress?.Report((received, total));
                }
            }

            if (!string.IsNullOrWhiteSpace(release.Sha256Digest))
            {
                string actualHex = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actualHex, release.Sha256Digest, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Downloaded installer failed SHA-256 verification.");
                }
            }

            if (File.Exists(destinationPath)) File.Delete(destinationPath);
            File.Move(partPath, destinationPath);
        }
        catch
        {
            try { if (File.Exists(partPath)) File.Delete(partPath); } catch { }
            throw;
        }
    }

    private static async Task<HttpResponseMessage> SendAssetDownloadRequestAsync(
        HttpClient http,
        string initialUrl,
        string token,
        CancellationToken ct)
    {
        Uri currentUri = new(initialUrl);
        for (int hops = 0; hops < 6; hops++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, currentUri);
            if (currentUri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
            {
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                req.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }

            var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (IsRedirect(resp.StatusCode) && resp.Headers.Location is { } location)
            {
                Uri nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                resp.Dispose();
                currentUri = nextUri;
                continue;
            }

            return resp;
        }

        throw new HttpRequestException("Too many redirects while downloading update.");
    }

    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.MovedPermanently
             or HttpStatusCode.Found
             or HttpStatusCode.SeeOther
             or HttpStatusCode.TemporaryRedirect
             or HttpStatusCode.PermanentRedirect;

    private static string? NormalizeSha256(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        string trimmed = digest.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[7..];
        return trimmed.Length == 64 ? trimmed.ToLowerInvariant() : null;
    }

    private static HttpClient CreateGitHubClient(HttpMessageHandler? handler, Version version, TimeSpan timeout)
    {
        var client = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: false);
        client.Timeout = timeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"CctvBackup/{FormatVersion(version)}");
        return client;
    }
}
