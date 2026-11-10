using System.Text.Json;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Backup;

namespace CctvBackup.Core.Branches;

/// <summary>The branches the user has added, saved in branches.json. Passwords are never stored here.</summary>
public sealed class BranchBook
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly List<Branch> _items = new();

    public BranchBook(string path)
    {
        _path = path;
        Load();
    }

    public IReadOnlyList<Branch> All => _items.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public Branch? Find(string name) =>
        _items.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void Add(Branch branch)
    {
        if (Find(branch.Name) is not null)
            throw new InvalidOperationException($"A branch called '{branch.Name}' already exists.");
        _items.Add(branch);
        Save();
    }

    /// <summary>Replaces the branch currently called <paramref name="oldName"/> (the name may change).</summary>
    public void Update(string oldName, Branch updated)
    {
        var existing = Find(oldName) ?? throw new KeyNotFoundException($"No branch called '{oldName}'.");
        var clash = Find(updated.Name);
        if (clash is not null && !ReferenceEquals(clash, existing))
            throw new InvalidOperationException($"A branch called '{updated.Name}' already exists.");

        _items[_items.IndexOf(existing)] = updated;
        Save();
    }

    public bool Remove(string name)
    {
        var existing = Find(name);
        if (existing is null) return false;
        _items.Remove(existing);
        Save();
        return true;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<List<Branch>>(File.ReadAllText(_path));
            if (loaded is not null) _items.AddRange(loaded);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable file: start with an empty list rather than refuse to start.
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_items, Json));
        File.Move(tmp, _path, overwrite: true);
    }
}

/// <summary>Checks the "Add branch" form. Returns plain-language problems; empty means valid.</summary>
public static class BranchValidator
{
    /// <param name="editingName">The branch being edited (so its own name does not count as a duplicate), or null when adding.</param>
    /// <param name="passwordRequired">True when no password is saved yet for this branch.</param>
    public static List<string> Validate(string name, LoginMode mode, string cloudId, string address, int port,
        string user, string password, IEnumerable<string> existingNames, string? editingName, bool passwordRequired)
    {
        var problems = new List<string>();
        name = name.Trim();
        cloudId = cloudId.Trim();
        address = address.Trim();

        if (name.Length == 0)
            problems.Add(Loc.T("Enter a name for the branch."));
        else if (name.Length > 60)
            problems.Add(Loc.T("The branch name is too long (60 characters at most)."));
        else
        {
            string folder = PathBuilder.Sanitize(name);
            bool clash = existingNames
                .Where(n => editingName is null || !n.Equals(editingName, StringComparison.OrdinalIgnoreCase))
                .Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)
                       || PathBuilder.Sanitize(n).Equals(folder, StringComparison.OrdinalIgnoreCase));
            if (clash) problems.Add(Loc.F("A branch called '{0}' already exists.", name));
        }

        if (mode == LoginMode.Cloud)
        {
            if (cloudId.Length == 0)
                problems.Add(Loc.T("Enter the Serial ID (cloud ID) of the DVR."));
            else if (!cloudId.All(char.IsAsciiLetterOrDigit) || cloudId.Length is < 8 or > 32)
                problems.Add(Loc.T("The Serial ID should be 8 to 32 letters and digits, with no spaces."));
        }
        else
        {
            if (address.Length == 0)
                problems.Add(Loc.T("Enter the IP address (or domain name) of the DVR."));
            else if (!IsValidAddress(address))
                problems.Add(Loc.T("The IP address is not valid. Use four numbers such as 192.168.1.20, or a domain name."));

            if (port is < 1 or > 65535)
                problems.Add(Loc.T("The port must be a number from 1 to 65535 (the DVR's Media Port, usually 34567)."));
        }

        if (user.Trim().Length == 0)
            problems.Add(Loc.T("Enter the DVR user name."));

        if (passwordRequired && password.Length == 0)
            problems.Add(Loc.T("Enter the DVR password."));

        return problems;
    }

    /// <summary>A dotted IPv4 address (four numbers 0-255) or a plain host / domain name.</summary>
    public static bool IsValidAddress(string address)
    {
        address = address.Trim();
        if (address.Length is 0 or > 253) return false;

        // Looks like an address made of digits and dots: it must then be a real IPv4 address.
        if (address.All(c => char.IsAsciiDigit(c) || c == '.'))
        {
            var parts = address.Split('.');
            return parts.Length == 4 && parts.All(p => p.Length is >= 1 and <= 3 && int.TryParse(p, out int v) && v is >= 0 and <= 255);
        }

        // Host name: labels of letters, digits and hyphens separated by dots.
        return address.Split('.').All(label =>
            label.Length is >= 1 and <= 63 && !label.StartsWith('-') && !label.EndsWith('-')
            && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
    }
}
