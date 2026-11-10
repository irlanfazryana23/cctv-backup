using System.Globalization;
using System.Xml.Linq;
using CctvBackup.Core.Backup;
using CctvBackup.Core.Localization;

namespace CctvBackup.Core.Branches;

/// <summary>A CMS entry that cannot be imported, and why (already translated for display).</summary>
public sealed record CmsSkipped(string Name, string Reason);

/// <summary>What was found in the CMS file.</summary>
public sealed record CmsImport(IReadOnlyList<Branch> Branches, IReadOnlyList<CmsSkipped> Skipped);

/// <summary>
/// Reads the branch list from the CMS's XML\Data.xml. Only names, cloud IDs / addresses, ports, user names
/// and camera channels are taken. The password attribute is deliberately never read: the CMS stores it in a
/// form this program does not use, so passwords are entered here instead.
/// </summary>
public static class CmsBranchImporter
{
    public static string DefaultDataFile => @"C:\Program Files (x86)\CMS\XML\Data.xml";

    // The CMS's own "Login Type" boxes are, in order: IP Address, Domain, ARSP, Cloud, IPV6, and Data.xml stores
    // the position in the ip="" attribute (the cloud branches in a real file carry ip="3", which is the fourth box).
    private const int LoginTypeIp = 0, LoginTypeDomain = 1, LoginTypeCloud = 3;

    public static IReadOnlyList<Branch> ImportFile(string path) => ImportFileDetailed(path).Branches;

    public static CmsImport ImportFileDetailed(string path)
    {
        using var stream = File.OpenRead(path);
        return ImportDetailed(XDocument.Load(stream));
    }

    public static IReadOnlyList<Branch> Import(XDocument doc) => ImportDetailed(doc).Branches;

    public static CmsImport ImportDetailed(XDocument doc)
    {
        var branches = new List<Branch>();
        var skipped = new List<CmsSkipped>();
        var devices = doc.Root?.Element("DEVINFO")?.Elements("DEV");
        if (devices is null) return new CmsImport(branches, skipped);

        foreach (var dev in devices)
        {
            string? host = ((string?)dev.Attribute("host"))?.Trim();
            string name = (((string?)dev.Attribute("name")) ?? string.Empty).Trim();
            if (name.Length == 0) continue;                       // not a device entry at all

            if (string.IsNullOrEmpty(host))
            {
                skipped.Add(new CmsSkipped(name, Loc.T("It has no Serial ID or address in the CMS.")));
                continue;
            }

            int port = ParseInt((string?)dev.Attribute("port"), Branch.DefaultPort);
            int loginType = ParseInt((string?)dev.Attribute("ip"), LoginTypeCloud);   // no attribute: treat as cloud

            LoginMode mode;
            switch (loginType)
            {
                case LoginTypeCloud:
                    mode = LoginMode.Cloud;
                    break;
                case LoginTypeIp or LoginTypeDomain:
                    mode = LoginMode.Ip;
                    if (!BranchValidator.IsValidAddress(host))
                    {
                        skipped.Add(new CmsSkipped(name, Loc.F("The address \"{0}\" is not valid.", host)));
                        continue;
                    }
                    if (port is < 1 or > 65535) port = Branch.DefaultPort;
                    break;
                default:
                    skipped.Add(new CmsSkipped(name, Loc.F("Login type {0} (ARSP or IPv6) is not supported; only Cloud and IP address are.", loginType)));
                    continue;
            }

            // type="0" = video camera, type="1" = alarm input (ignored).
            var cameras = dev.Elements("DEV")
                .Where(c => ((string?)c.Attribute("type") ?? "0") == "0")
                .Select(c => new Camera(
                    ParseInt((string?)c.Attribute("channel"), -1),
                    (string?)c.Attribute("name") ?? string.Empty))
                .Where(c => c.Channel >= 0)
                .OrderBy(c => c.Channel)
                .ToList();

            branches.Add(new Branch(
                Name: name,
                Area: ((string?)dev.Attribute("area") ?? string.Empty).Trim(),
                CloudId: mode == LoginMode.Cloud ? host : string.Empty,
                Port: mode == LoginMode.Cloud ? Branch.DefaultPort : port,
                Username: ((string?)dev.Attribute("username") ?? string.Empty).Trim(),
                Cameras: cameras,
                Mode: mode,
                Address: mode == LoginMode.Ip ? host : string.Empty));
        }

        return new CmsImport(branches.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList(), skipped);
    }

    private static int ParseInt(string? s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}

/// <summary>One CMS branch that can be imported, with what the user should know before ticking it.</summary>
/// <param name="Note">Null when there is nothing to warn about.</param>
public sealed record ImportCandidate(Branch Branch, string? Note)
{
    /// <summary>Ticked by default unless it looks like a DVR that is already in the program under another name.</summary>
    public bool DefaultChecked => Note is null;
}

/// <summary>What the import screen shows: what can be imported, and what is left out.</summary>
public sealed record ImportPlan(
    IReadOnlyList<ImportCandidate> Candidates,
    IReadOnlyList<string> AlreadyPresent,
    IReadOnlyList<CmsSkipped> Unsupported);

public static class CmsImportPlanner
{
    /// <summary>Sorts the CMS branches into: new, already in the program (same name), or not importable.</summary>
    public static ImportPlan Plan(CmsImport import, IEnumerable<Branch> existing)
    {
        var current = existing.ToList();
        var candidates = new List<ImportCandidate>();
        var already = new List<string>();
        var unsupported = new List<CmsSkipped>(import.Skipped);
        var takenFolders = new HashSet<string>(current.Select(b => PathBuilder.Sanitize(b.Name)), StringComparer.OrdinalIgnoreCase);

        foreach (var branch in import.Branches)
        {
            string folder = PathBuilder.Sanitize(branch.Name);

            if (current.Any(b => b.Name.Equals(branch.Name, StringComparison.OrdinalIgnoreCase)))
            {
                already.Add(branch.Name);
                continue;
            }

            // Two different names that would share one backup folder cannot both be used.
            if (!takenFolders.Add(folder))
            {
                unsupported.Add(new CmsSkipped(branch.Name, Loc.T("Another branch already uses the same name or backup folder.")));
                continue;
            }

            var sameDvr = current.FirstOrDefault(b => b.ConnectionKey == branch.ConnectionKey);
            string? note = sameDvr is null ? null : Loc.F("Same DVR as \"{0}\"", sameDvr.Name);
            candidates.Add(new ImportCandidate(branch, note));
        }

        return new ImportPlan(candidates, already, unsupported);
    }
}
