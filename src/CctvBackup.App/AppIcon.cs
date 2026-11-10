namespace CctvBackup.App;

/// <summary>The program icon (from the logo), taken from the .exe itself so it is stored in only one place.</summary>
internal static class AppIcon
{
    private static Icon? _icon;

    public static Icon? Get()
    {
        try { return _icon ??= Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch (Exception) { return null; }     // a missing icon must never stop the program
    }
}
