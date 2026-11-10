using CctvBackup.Core.Playback;

namespace CctvBackup.Tests;

public class DecoderLocatorTests
{
    private static string MakeDecoder(string root, string name)
    {
        string dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, DecoderLocator.DllName), "x");
        return dir;
    }

    [Fact]
    public void Prefers_the_decoder_bundled_with_the_program()
    {
        using var t = new TempDir();
        string bundled = MakeDecoder(t.Path, "bundled");
        string installed = MakeDecoder(t.Path, "installed");
        Assert.Equal(bundled, DecoderLocator.Find(bundled, installed));
    }

    [Fact]
    public void Falls_back_to_an_installed_player()
    {
        using var t = new TempDir();
        string installed = MakeDecoder(t.Path, "installed");
        Assert.Equal(installed, DecoderLocator.Find(Path.Combine(t.Path, "none"), installed));
    }

    [Fact]
    public void Returns_null_when_there_is_no_decoder()
    {
        using var t = new TempDir();
        Assert.Null(DecoderLocator.Find(Path.Combine(t.Path, "a"), Path.Combine(t.Path, "b")));
    }
}
