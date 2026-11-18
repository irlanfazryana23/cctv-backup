using System;
using System.Linq;
using Avalonia;
using CctvBackup.Core.Auth;

namespace CctvBackup.Desktop;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            if (args != null && args.Any(a => a is "--reset-password" or "--reset-auth"))
            {
                AppAuthStore.ResetFromConsole();
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args ?? Array.Empty<string>());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Fatal error: " + ex);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
