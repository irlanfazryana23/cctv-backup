using System.Globalization;
using System.Text;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Sdk;

// Task 1 spike: proves cloud login, remote file search and download through the CMS's NetSdk.dll.
//
//   CctvBackup.Spike branches
//   CctvBackup.Spike probe  <branch>
//   CctvBackup.Spike files  <branch> <user> <cam#> "<yyyy-MM-dd HH:mm>" <minutes>
//   CctvBackup.Spike get    <branch> <user> <cam#> "<yyyy-MM-dd HH:mm>" <minutes> <outFolder>
//
// The password is read from the CCTV_PASSWORD environment variable or typed (hidden) at the prompt.
// Passwords are never read from the CMS files and never printed.

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0) return Usage();

    if (args[0].Equals("selftest", StringComparison.OrdinalIgnoreCase))
    {
        // Loads the vendor driver and initialises it. No network, no login. Used to check that a
        // bundled "sdk" folder is complete (works on a PC without the CMS).
        string? dir = args.Length > 1 ? args[1] : SdkLocator.Find(null);
        if (dir is null) { Console.Error.WriteLine("NetSdk.dll not found."); return 2; }
        try
        {
            using var s = XmSdk.Load(dir);
            Console.WriteLine($"OK: driver loaded and initialised from {s.SdkDirectory}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAILED to load the driver from {dir}: {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }

    var branches = CmsBranchImporter.ImportFile(CmsBranchImporter.DefaultDataFile);
    string cmd = args[0].ToLowerInvariant();

    if (cmd == "branches")
    {
        foreach (var b in branches)
            Console.WriteLine($"{b.Name,-20} id={b.CloudId,-22} port={b.Port,-6} cams={b.Cameras.Count,-3} user={b.Username}");
        Console.WriteLine($"{branches.Count} branches");
        return 0;
    }

    if (args.Length < 2) return Usage();
    var branch = branches.FirstOrDefault(b => b.Name.Equals(args[1], StringComparison.OrdinalIgnoreCase));
    if (branch is null)
    {
        Console.Error.WriteLine($"No branch named '{args[1]}'. Run 'branches' to list them.");
        return 2;
    }

    using var sdk = XmSdk.Load(SdkLocator.Find(null) ?? XmSdk.DefaultSdkDirectory);
    Console.WriteLine($"SDK loaded from {sdk.SdkDirectory}");

    if (cmd == "probe")
    {
        // Deliberately uses a user name that cannot exist, so a real account is never locked.
        // Any answer that comes back from the DVR proves the cloud route works.
        Console.WriteLine($"Probing {branch.Name} ({branch.CloudId}) through the cloud...");
        try
        {
            int h = sdk.LoginCloud(branch.CloudId, "probe_nouser", "probe_nopass");
            Console.WriteLine($"Unexpected: login succeeded, handle {h}");
            sdk.Logout(h);
        }
        catch (SdkException ex)
        {
            Console.WriteLine(ex.Message);
        }
        return 0;
    }

    if (args.Length < 6) return Usage();
    string user = args[2];
    int camNo = int.Parse(args[3], CultureInfo.InvariantCulture);
    DateTime from = DateTime.ParseExact(args[4], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    DateTime to = from.AddMinutes(int.Parse(args[5], CultureInfo.InvariantCulture));
    int channel = camNo - 1;

    string password = Environment.GetEnvironmentVariable("CCTV_PASSWORD") ?? ReadHidden($"Password for {user}: ");

    int login = sdk.LoginCloud(branch.CloudId, user, password);
    Console.WriteLine($"Logged in to {branch.Name}, handle {login}");
    try
    {
        var files = sdk.FindFiles(login, channel, from, to);
        Console.WriteLine($"{files.Count} file(s) for cam {camNo} between {from:u} and {to:u}");
        foreach (var f in files)
            Console.WriteLine($"  {f.Begin:yyyy-MM-dd HH:mm:ss} -> {f.End:HH:mm:ss}  {f.SizeKb,10} KB  {f.RemoteName}");

        if (cmd == "get" && files.Count > 0)
        {
            string outDir = args[6];
            Directory.CreateDirectory(outDir);
            var file = files[0];
            string target = Path.Combine(outDir, $"{file.Begin:yyyyMMdd_HHmmss}_cam{camNo:00}.h264");
            Console.WriteLine($"Downloading first file to {target}");
            using var dl = sdk.StartDownload(login, file, target);
            int pos;
            do
            {
                Thread.Sleep(1000);
                pos = dl.Position;
                Console.Write($"\r  progress: {pos}%   ");
            } while (pos >= 0 && pos < 100);
            Console.WriteLine();
            Console.WriteLine(pos == 100
                ? $"Done. {new FileInfo(target).Length:N0} bytes on disk."
                : $"Download ended with position {pos} (error). Last SDK error: {SdkErrors.Describe(sdk.LastError)}");
        }
    }
    finally
    {
        sdk.Logout(login);
    }
    return 0;
}

static string ReadHidden(string prompt)
{
    Console.Write(prompt);
    var sb = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
        sb.Append(key.KeyChar);
    }
    Console.WriteLine();
    return sb.ToString();
}

static int Usage()
{
    Console.WriteLine("""
        Usage:
          CctvBackup.Spike branches
          CctvBackup.Spike probe <branch>
          CctvBackup.Spike files <branch> <user> <cam#> "<yyyy-MM-dd HH:mm>" <minutes>
          CctvBackup.Spike get   <branch> <user> <cam#> "<yyyy-MM-dd HH:mm>" <minutes> <outFolder>
        Password: set CCTV_PASSWORD or type it when asked.
        """);
    return 1;
}
