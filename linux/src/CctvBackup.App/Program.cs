using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CctvBackup.Core.Backup;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Sdk;
using CctvBackup.Core.Settings;

namespace CctvBackup.App;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        if (args.Contains("--cli"))
        {
            return await RunCliAsync(args);
        }

        int port = 0;
        int portIdx = Array.IndexOf(args, "--port");
        if (portIdx >= 0 && portIdx + 1 < args.Length && int.TryParse(args[portIdx + 1], out int parsedPort))
            port = parsedPort;

        bool noWindow = args.Contains("--no-window") || args.Contains("--no-browser");

        using var appState = new AppState();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        int boundPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        string url = $"http://127.0.0.1:{boundPort}";

        Console.WriteLine($"CCTV Backup (Linux Edition) running at: {url}");
        Console.WriteLine($"Active DVR Backend: {appState.DvrClient.ActiveBackendDescription}");
        Console.WriteLine($"Config Folder:      {AppPaths.DataDirectory}");

        if (!noWindow)
        {
            DesktopLauncher.OpenAppWindow(url);
        }

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cts.Token);
                _ = Task.Run(() => HandleHttpClientAsync(client, appState, cts.Token));
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }

        return 0;
    }

    private static async Task HandleHttpClientAsync(TcpClient client, AppState appState, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                var (method, path, body) = await ReadHttpRequestAsync(stream, ct);
                if (string.IsNullOrEmpty(method)) return;

                if (method == "GET" && path == "/")
                {
                    await WriteHttpResponseAsync(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(WebUiHtml.Page), ct);
                    return;
                }

                object responseObj = await RouteApiAsync(method, path, body, appState);
                byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(responseObj, JsonOpts);
                await WriteHttpResponseAsync(stream, 200, "application/json; charset=utf-8", jsonBytes, ct);
            }
            catch
            {
                // Ignore client disconnect errors
            }
        }
    }

    private static async Task<object> RouteApiAsync(string method, string path, string body, AppState appState)
    {
        T Deserialize<T>() where T : new() =>
            string.IsNullOrWhiteSpace(body)
                ? new T()
                : (JsonSerializer.Deserialize<T>(body, JsonOpts) ?? new T());

        if (method == "GET" && path == "/api/state")
            return appState.GetSnapshot();

        if (method == "POST")
        {
            switch (path)
            {
                case "/api/language":
                {
                    var dto = Deserialize<LanguageDto>();
                    appState.SetLanguage(dto.Language ?? "en");
                    return new { ok = true };
                }
                case "/api/sdk-folder":
                {
                    var dto = Deserialize<PathDto>();
                    appState.SetSdkDirectory(dto.Path ?? string.Empty);
                    return new { ok = true };
                }
                case "/api/driver/reset":
                {
                    appState.DvrClient.ResetDriver();
                    appState.AppendLog($"{DateTime.Now:HH:mm:ss}  DVR driver reset.");
                    return new { ok = true };
                }
                case "/api/branches/select":
                {
                    var dto = Deserialize<SelectBranchesDto>();
                    appState.SetBranchSelection(dto.Names ?? Array.Empty<string>());
                    return new { ok = true };
                }
                case "/api/cameras/select":
                {
                    var dto = Deserialize<SelectCamerasDto>();
                    if (!string.IsNullOrWhiteSpace(dto.Branch))
                        appState.SetCameraSelection(dto.Branch, dto.Channels ?? Array.Empty<int>());
                    return new { ok = true };
                }
                case "/api/branches/save":
                {
                    var dto = Deserialize<SaveBranchDto>();
                    var mode = string.Equals(dto.Mode, "Ip", StringComparison.OrdinalIgnoreCase) ? LoginMode.Ip : LoginMode.Cloud;
                    var (ok, errors) = appState.SaveBranch(
                        dto.EditingName,
                        dto.Name ?? string.Empty,
                        dto.Area ?? string.Empty,
                        mode,
                        dto.SerialId ?? string.Empty,
                        dto.Address ?? string.Empty,
                        dto.Port,
                        dto.Username ?? string.Empty,
                        dto.Password ?? string.Empty,
                        dto.CameraCount);
                    return new { ok, errors };
                }
                case "/api/branches/delete":
                {
                    var dto = Deserialize<NameDto>();
                    bool ok = !string.IsNullOrWhiteSpace(dto.Name) && appState.DeleteBranch(dto.Name);
                    return new { ok };
                }
                case "/api/branches/test":
                {
                    var dto = Deserialize<NameDto>();
                    if (string.IsNullOrWhiteSpace(dto.Name))
                        return new { ok = false, message = "Branch name required." };
                    var (ok, message, channels, model, serial) = await appState.TestConnectionAsync(dto.Name);
                    return new { ok, message, channels, model, serial };
                }
                case "/api/period":
                {
                    var dto = Deserialize<PeriodDto>();
                    if (DateTime.TryParse(dto.From, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) &&
                        DateTime.TryParse(dto.To, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
                    {
                        appState.SetPeriod(from, to);
                    }
                    return new { ok = true };
                }
                case "/api/period/preset":
                {
                    var dto = Deserialize<PresetDto>();
                    var now = DateTime.Now;
                    var today = now.Date;
                    var nowMin = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
                    switch (dto.Preset?.ToLowerInvariant())
                    {
                        case "yesterday":
                            appState.SetPeriod(today.AddDays(-1), today);
                            break;
                        case "today":
                            appState.SetPeriod(today, nowMin > today ? nowMin : today.AddHours(1));
                            break;
                        case "week":
                            appState.SetPeriod(today.AddDays(-7), nowMin > today ? nowMin : today);
                            break;
                    }
                    return new { ok = true };
                }
                case "/api/destination":
                {
                    var dto = Deserialize<PathDto>();
                    appState.SetDestination(dto.Path ?? string.Empty);
                    return new { ok = true };
                }
                case "/api/destination/browse":
                {
                    if (DesktopLauncher.TryPickDirectoryNative(appState.Settings.LastDestination, out string? picked))
                    {
                        if (!string.IsNullOrWhiteSpace(picked))
                            appState.SetDestination(picked);
                        return new { picked = !string.IsNullOrWhiteSpace(picked), fallbackModal = false };
                    }

                    string defaultDir = !string.IsNullOrWhiteSpace(appState.Settings.LastDestination) && Directory.Exists(appState.Settings.LastDestination)
                        ? appState.Settings.LastDestination
                        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    return new { picked = false, fallbackModal = true, defaultDir };
                }
                case "/api/destination/open":
                {
                    bool ok = DesktopLauncher.TryOpenFolderInFileManager(appState.Settings.LastDestination ?? string.Empty);
                    return new { ok };
                }
                case "/api/find":
                {
                    var (ok, errors) = await appState.StartFindAsync();
                    return new { ok, errors };
                }
                case "/api/download":
                {
                    var (ok, errors) = await appState.StartDownloadAsync();
                    return new { ok, errors };
                }
                case "/api/cancel":
                {
                    appState.Cancel();
                    return new { ok = true };
                }
                case "/api/jobs/select":
                {
                    var dto = Deserialize<SelectJobDto>();
                    appState.SetJobSelection(dto.Index, dto.Selected);
                    return new { ok = true };
                }
                case "/api/jobs/select-all":
                {
                    var dto = Deserialize<SelectAllJobsDto>();
                    appState.SetAllJobsSelection(dto.Selected);
                    return new { ok = true };
                }
                case "/api/cms/preview":
                {
                    var dto = Deserialize<CmsPreviewDto>();
                    try
                    {
                        var plan = appState.PreviewCmsXml(dto.Xml ?? string.Empty);
                        return new
                        {
                            ok = true,
                            candidates = plan.Candidates.Select(c => new
                            {
                                name = c.Branch.Name,
                                connection = c.Branch.ConnectionText,
                                note = c.Note,
                                defaultChecked = c.DefaultChecked,
                            }),
                            alreadyPresent = plan.AlreadyPresent,
                            unsupported = plan.Unsupported.Select(u => new { name = u.Name, reason = u.Reason }),
                        };
                    }
                    catch (Exception ex)
                    {
                        return new { ok = false, error = ex.Message };
                    }
                }
                case "/api/cms/import":
                {
                    var dto = Deserialize<CmsImportDto>();
                    try
                    {
                        int count = appState.ApplyCmsImport(dto.Xml ?? string.Empty, dto.SelectedNames ?? Array.Empty<string>(), dto.Password);
                        return new { ok = true, count };
                    }
                    catch (Exception ex)
                    {
                        return new { ok = false, error = ex.Message };
                    }
                }
                case "/api/fs/list":
                {
                    var dto = Deserialize<PathDto>();
                    string target = !string.IsNullOrWhiteSpace(dto.Path) && Directory.Exists(dto.Path)
                        ? Path.GetFullPath(dto.Path)
                        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                    var roots = new List<object>
                    {
                        new { label = "Home (~)", path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) },
                    };

                    if (!OperatingSystem.IsWindows())
                    {
                        foreach (string linuxMount in new[] { "/media", "/run/media", "/mnt", "/" })
                        {
                            if (Directory.Exists(linuxMount))
                                roots.Add(new { label = linuxMount, path = linuxMount });
                        }
                    }

                    foreach (var d in DriveInfo.GetDrives())
                    {
                        try
                        {
                            if (d.IsReady && Directory.Exists(d.RootDirectory.FullName))
                                roots.Add(new { label = $"{d.Name} ({DiskCheck.Format(d.AvailableFreeSpace)} free)", path = d.RootDirectory.FullName });
                        }
                        catch { }
                    }

                    var dirs = new List<object>();
                    try
                    {
                        foreach (var sub in Directory.GetDirectories(target).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                        {
                            string name = Path.GetFileName(sub);
                            if (name.StartsWith('.')) continue;
                            dirs.Add(new { name, fullPath = sub });
                        }
                    }
                    catch { }

                    string? parent = Directory.GetParent(target)?.FullName;
                    return new { currentPath = target, parentPath = parent, roots, directories = dirs };
                }
                case "/api/fs/mkdir":
                {
                    var dto = Deserialize<MkdirDto>();
                    try
                    {
                        string parent = dto.Parent ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        string cleanName = PathBuilder.Sanitize(dto.Name ?? "CCTV-Backup");
                        string created = Path.Combine(parent, cleanName);
                        Directory.CreateDirectory(created);
                        return new { ok = true, path = created };
                    }
                    catch (Exception ex)
                    {
                        return new { ok = false, error = ex.Message };
                    }
                }
            }
        }

        return new { ok = false, error = "Not found" };
    }

    private static async Task<(string Method, string Path, string Body)> ReadHttpRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var headerBytes = new List<byte>(1024);
        byte[] one = new byte[1];
        while (headerBytes.Count < 65536)
        {
            int n = await stream.ReadAsync(one.AsMemory(0, 1), ct);
            if (n == 0) break;
            headerBytes.Add(one[0]);
            int count = headerBytes.Count;
            if (count >= 4 &&
                headerBytes[count - 4] == '\r' &&
                headerBytes[count - 3] == '\n' &&
                headerBytes[count - 2] == '\r' &&
                headerBytes[count - 1] == '\n')
            {
                break;
            }
        }

        if (headerBytes.Count == 0)
            return (string.Empty, string.Empty, string.Empty);

        string headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        string[] lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return (string.Empty, string.Empty, string.Empty);

        string[] firstLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (firstLine.Length < 2)
            return (string.Empty, string.Empty, string.Empty);

        string method = firstLine[0].ToUpperInvariant();
        string rawPath = firstLine[1];
        int qIdx = rawPath.IndexOf('?');
        string path = qIdx >= 0 ? rawPath[..qIdx] : rawPath;

        int contentLength = 0;
        foreach (string line in lines.Skip(1))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
            }
        }

        string body = string.Empty;
        if (contentLength > 0 && contentLength <= 16 * 1024 * 1024)
        {
            byte[] bodyBuf = new byte[contentLength];
            int offset = 0;
            while (offset < contentLength)
            {
                int r = await stream.ReadAsync(bodyBuf.AsMemory(offset, contentLength - offset), ct);
                if (r == 0) break;
                offset += r;
            }
            body = Encoding.UTF8.GetString(bodyBuf, 0, offset);
        }

        return (method, path, body);
    }

    private static async Task WriteHttpResponseAsync(NetworkStream stream, int statusCode, string contentType, byte[] body, CancellationToken ct)
    {
        string statusText = statusCode == 200 ? "OK" : "Error";
        string header =
            $"HTTP/1.1 {statusCode} {statusText}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n";

        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, ct);
        if (body.Length > 0)
            await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<int> RunCliAsync(string[] args)
    {
        using var state = new AppState();
        Console.WriteLine($"CCTV Backup CLI (Linux) — Backend: {state.DvrClient.ActiveBackendDescription}");

        string? branchName = GetArgValue(args, "--branch");
        string? dest = GetArgValue(args, "--dest") ?? state.Settings.LastDestination;
        string? fromStr = GetArgValue(args, "--from");
        string? toStr = GetArgValue(args, "--to");

        if (args.Contains("--list-branches"))
        {
            foreach (var b in state.Book.All)
                Console.WriteLine($"- {b.Name} [{b.Mode}: {b.ConnectionText}] ({b.Cameras.Count} cameras)");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(branchName))
        {
            Console.Error.WriteLine("Error: --branch <name> (or 'all') is required in --cli mode.");
            return 1;
        }

        if (args.Contains("--test"))
        {
            var (ok, msg, _, _, _) = await state.TestConnectionAsync(branchName);
            Console.WriteLine(msg);
            return ok ? 0 : 1;
        }

        if (string.IsNullOrWhiteSpace(dest))
        {
            Console.Error.WriteLine("Error: --dest <folder> is required.");
            return 1;
        }

        DateTime from = fromStr is not null ? DateTime.Parse(fromStr, CultureInfo.InvariantCulture) : DateTime.Today.AddDays(-1);
        DateTime to = toStr is not null ? DateTime.Parse(toStr, CultureInfo.InvariantCulture) : DateTime.Today;

        var selectedBranches = string.Equals(branchName, "all", StringComparison.OrdinalIgnoreCase)
            ? state.Book.All.ToList()
            : state.Book.All.Where(b => string.Equals(b.Name, branchName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (selectedBranches.Count == 0)
        {
            Console.Error.WriteLine($"No branch matching '{branchName}' found.");
            return 1;
        }

        var requests = selectedBranches
            .Select(b => new BackupRequest(b, b.Cameras, from, to))
            .ToList();

        var engine = new BackupEngine(state.DvrClient, name => state.Credentials.Get(name));
        engine.Log += Console.WriteLine;

        var summary = await engine.RunAsync(requests, dest, CancellationToken.None);
        return summary.Branches.All(b => b.Succeeded) && !summary.Cancelled ? 0 : 2;
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            CCTV Backup — Linux Edition
            Usage:
              cctv-backup [options]

            GUI Mode (default):
              cctv-backup                    Start desktop UI window
              cctv-backup --port 8080        Bind desktop UI to a specific localhost port
              cctv-backup --no-window        Start local UI server without opening a browser window

            Headless CLI Mode:
              cctv-backup --cli --list-branches
              cctv-backup --cli --branch "BranchName" --test
              cctv-backup --cli --branch "BranchName" --from "2025-01-15 08:00" --to "2025-01-15 17:00" --dest "/media/usb/Backup"
            """);
    }

    private sealed record LanguageDto
    {
        public string? Language { get; init; }
    }

    private sealed record PathDto
    {
        public string? Path { get; init; }
    }

    private sealed record NameDto
    {
        public string? Name { get; init; }
    }

    private sealed record SelectBranchesDto
    {
        public string[]? Names { get; init; }
    }

    private sealed record SelectCamerasDto
    {
        public string? Branch { get; init; }
        public int[]? Channels { get; init; }
    }

    private sealed record SaveBranchDto
    {
        public string? EditingName { get; init; }
        public string? Name { get; init; }
        public string? Area { get; init; }
        public string? Mode { get; init; }
        public string? SerialId { get; init; }
        public string? Address { get; init; }
        public int Port { get; init; }
        public string? Username { get; init; }
        public string? Password { get; init; }
        public int CameraCount { get; init; }
    }

    private sealed record PeriodDto
    {
        public string? From { get; init; }
        public string? To { get; init; }
    }

    private sealed record PresetDto
    {
        public string? Preset { get; init; }
    }

    private sealed record SelectJobDto
    {
        public int Index { get; init; }
        public bool Selected { get; init; }
    }

    private sealed record SelectAllJobsDto
    {
        public bool Selected { get; init; }
    }

    private sealed record CmsPreviewDto
    {
        public string? Xml { get; init; }
    }

    private sealed record CmsImportDto
    {
        public string? Xml { get; init; }
        public string[]? SelectedNames { get; init; }
        public string? Password { get; init; }
    }

    private sealed record MkdirDto
    {
        public string? Parent { get; init; }
        public string? Name { get; init; }
    }
}
