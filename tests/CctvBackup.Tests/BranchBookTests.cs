using CctvBackup.Core.Branches;
using CctvBackup.Core.Sdk;

namespace CctvBackup.Tests;

public class BranchBookTests
{
    private static string PathIn(TempDir d) => Path.Combine(d.Path, "branches.json");

    [Fact]
    public void Starts_empty_when_there_is_no_file()
    {
        using var dir = new TempDir();
        Assert.Empty(new BranchBook(PathIn(dir)).All);
    }

    [Fact]
    public void Added_branches_persist_and_are_listed_alphabetically()
    {
        using var dir = new TempDir();
        var book = new BranchBook(PathIn(dir));
        book.Add(Make.Branch("Zeta", 8, "zzzzzzzzzzzzzzzz"));
        book.Add(Make.Branch("alpha", 16, "aaaaaaaaaaaaaaaa"));

        var reloaded = new BranchBook(PathIn(dir)).All;

        Assert.Equal(new[] { "alpha", "Zeta" }, reloaded.Select(b => b.Name));
        Assert.Equal(16, reloaded[0].Cameras.Count);
        Assert.Equal("aaaaaaaaaaaaaaaa", reloaded[0].CloudId);
    }

    [Fact]
    public void Adding_a_duplicate_name_is_rejected_ignoring_case()
    {
        using var dir = new TempDir();
        var book = new BranchBook(PathIn(dir));
        book.Add(Make.Branch("Alpha"));

        Assert.Throws<InvalidOperationException>(() => book.Add(Make.Branch("ALPHA")));
    }

    [Fact]
    public void Update_can_rename_and_change_the_camera_count()
    {
        using var dir = new TempDir();
        var book = new BranchBook(PathIn(dir));
        book.Add(Make.Branch("Old", 8));

        book.Update("Old", Make.Branch("New", 16));

        Assert.Null(book.Find("Old"));
        Assert.Equal(16, book.Find("New")!.Cameras.Count);
        Assert.Single(new BranchBook(PathIn(dir)).All);
    }

    [Fact]
    public void Update_cannot_take_the_name_of_another_branch()
    {
        using var dir = new TempDir();
        var book = new BranchBook(PathIn(dir));
        book.Add(Make.Branch("A"));
        book.Add(Make.Branch("B"));

        Assert.Throws<InvalidOperationException>(() => book.Update("A", Make.Branch("B")));
    }

    [Fact]
    public void Remove_deletes_the_branch()
    {
        using var dir = new TempDir();
        var book = new BranchBook(PathIn(dir));
        book.Add(Make.Branch("A"));

        Assert.True(book.Remove("a"));
        Assert.False(book.Remove("A"));
        Assert.Empty(new BranchBook(PathIn(dir)).All);
    }

    [Fact]
    public void Ip_branches_keep_their_address_and_port_after_saving()
    {
        using var dir = new TempDir();
        var ip = new Branch("Local", string.Empty, string.Empty, 35000, "u", Camera.Range("Local", 4), LoginMode.Ip, "192.168.1.20");
        new BranchBook(PathIn(dir)).Add(ip);

        var loaded = new BranchBook(PathIn(dir)).Find("Local")!;

        Assert.Equal(LoginMode.Ip, loaded.Mode);
        Assert.Equal("192.168.1.20", loaded.Address);
        Assert.Equal(35000, loaded.Port);
        Assert.Equal(4, loaded.Cameras.Count);
        Assert.Contains("\"Ip\"", File.ReadAllText(PathIn(dir)));      // readable name, not a bare number
    }

    [Fact]
    public void Branches_saved_before_ip_login_existed_load_as_cloud_branches()
    {
        using var dir = new TempDir();
        File.WriteAllText(PathIn(dir),
            """[{"Name":"Old","Area":"","CloudId":"efcefcc5ee927b1d","Port":34567,"Username":"u","Cameras":[{"Channel":0,"Name":"Old_1"}]}]""");

        var old = new BranchBook(PathIn(dir)).Find("Old")!;

        Assert.Equal(LoginMode.Cloud, old.Mode);
        Assert.Equal(string.Empty, old.Address);
        Assert.Equal("efcefcc5ee927b1d", old.CloudId);
        Assert.Single(old.Cameras);
    }

    [Fact]
    public void Connection_key_tells_apart_cloud_and_ip_and_ignores_letter_case()
    {
        var cloud = Make.Branch("A", 1, "EFCEFCC5EE927B1D");
        var cloudLower = Make.Branch("B", 1, "efcefcc5ee927b1d");
        var ip = new Branch("C", "", "", 34567, "u", Array.Empty<Camera>(), LoginMode.Ip, "DVR.Local");
        var ipLower = new Branch("D", "", "", 34567, "u", Array.Empty<Camera>(), LoginMode.Ip, "dvr.local");
        var otherPort = ip with { Port = 35000 };

        Assert.Equal(cloud.ConnectionKey, cloudLower.ConnectionKey);
        Assert.Equal(ip.ConnectionKey, ipLower.ConnectionKey);
        Assert.NotEqual(cloud.ConnectionKey, ip.ConnectionKey);
        Assert.NotEqual(ip.ConnectionKey, otherPort.ConnectionKey);
    }

    [Fact]
    public void Connecting_text_names_the_way_the_branch_is_reached()
    {
        Assert.Equal("connecting through the cloud...", Make.Branch("A").ConnectingText);
        var ip = new Branch("C", "", "", 34567, "u", Array.Empty<Camera>(), LoginMode.Ip, "192.168.1.20");
        Assert.Equal("connecting to 192.168.1.20:34567...", ip.ConnectingText);
    }

    [Fact]
    public void Corrupt_file_gives_an_empty_book()
    {
        using var dir = new TempDir();
        File.WriteAllText(PathIn(dir), "{ not json");

        Assert.Empty(new BranchBook(PathIn(dir)).All);
    }

    [Fact]
    public void Saved_file_never_contains_a_password_field()
    {
        using var dir = new TempDir();
        new BranchBook(PathIn(dir)).Add(Make.Branch("A"));

        Assert.DoesNotContain("assword", File.ReadAllText(PathIn(dir)));
    }
}

public class BranchValidatorTests
{
    private static List<string> Check(string name = "My Branch", string serial = "6ead603325d32dd8rejd", string user = "teamti",
        string pass = "secret", string[]? existing = null, string? editing = null, bool passwordRequired = true,
        LoginMode mode = LoginMode.Cloud, string address = "192.168.1.20", int port = 34567) =>
        BranchValidator.Validate(name, mode, serial, address, port, user, pass, existing ?? Array.Empty<string>(), editing, passwordRequired);

    [Fact]
    public void Valid_input_has_no_problems() => Assert.Empty(Check());

    [Fact]
    public void Name_is_required_and_limited() 
    {
        Assert.Contains(Check(name: "  "), p => p.Contains("name for the branch"));
        Assert.Contains(Check(name: new string('x', 61)), p => p.Contains("too long"));
    }

    [Fact]
    public void Duplicate_name_is_rejected_ignoring_case_and_folder_lookalikes()
    {
        Assert.Contains(Check(name: "alpha", existing: new[] { "Alpha" }), p => p.Contains("already exists"));
        // "A/B" and "A_B" would share one backup folder, so they count as the same branch.
        Assert.Contains(Check(name: "A/B", existing: new[] { "A_B" }), p => p.Contains("already exists"));
    }

    [Fact]
    public void Editing_a_branch_may_keep_its_own_name()
    {
        Assert.Empty(Check(name: "Alpha", existing: new[] { "Alpha", "Beta" }, editing: "Alpha"));
        Assert.Contains(Check(name: "Beta", existing: new[] { "Alpha", "Beta" }, editing: "Alpha"), p => p.Contains("already exists"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has space 12345678")]
    [InlineData("bad-chars-1234567890")]
    public void Bad_serial_ids_are_rejected(string serial) =>
        Assert.NotEmpty(Check(serial: serial));

    [Theory]
    [InlineData("efcefcc5ee927b1d")]
    [InlineData("6ead603325d32dd8rejd")]
    [InlineData("  efcefcc5ee927b1d  ")]
    public void Real_looking_serial_ids_are_accepted(string serial) =>
        Assert.Empty(Check(serial: serial));

    [Fact]
    public void User_is_required() => Assert.Contains(Check(user: " "), p => p.Contains("user name"));

    // ---- IP address login ----

    [Fact]
    public void Ip_login_with_a_good_address_and_port_is_valid() =>
        Assert.Empty(Check(mode: LoginMode.Ip, serial: "", address: "192.168.1.20", port: 34567));

    [Fact]
    public void Ip_login_does_not_need_a_serial_id_and_cloud_login_does_not_need_an_address()
    {
        Assert.Empty(Check(mode: LoginMode.Ip, serial: ""));
        Assert.Empty(Check(mode: LoginMode.Cloud, address: ""));
    }

    [Fact]
    public void Ip_login_requires_an_address() =>
        Assert.Contains(Check(mode: LoginMode.Ip, address: "  "), p => p.Contains("IP address"));

    [Theory]
    [InlineData("300.1.1.1")]
    [InlineData("192.168.1")]
    [InlineData("192.168.1.20.5")]
    [InlineData("192.168.1.")]
    [InlineData("bad host")]
    [InlineData("-bad.example.com")]
    [InlineData("a..b")]
    [InlineData("http://192.168.1.20")]
    public void Bad_addresses_are_rejected(string address)
    {
        Assert.False(BranchValidator.IsValidAddress(address));
        Assert.Contains(Check(mode: LoginMode.Ip, address: address), p => p.Contains("not valid"));
    }

    [Theory]
    [InlineData("192.168.1.20")]
    [InlineData("10.0.0.1")]
    [InlineData("  172.16.5.9  ")]
    [InlineData("dvr.example.com")]
    [InlineData("dvr-1.branch.local")]
    [InlineData("localhost")]
    public void Good_addresses_are_accepted(string address) => Assert.True(BranchValidator.IsValidAddress(address));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(65536)]
    public void Ip_login_port_must_be_in_range(int port) =>
        Assert.Contains(Check(mode: LoginMode.Ip, port: port), p => p.Contains("port"));

    [Fact]
    public void Cloud_login_ignores_the_port() => Assert.Empty(Check(mode: LoginMode.Cloud, port: 0));

    [Fact]
    public void Password_is_required_for_new_branches_but_optional_when_editing_with_a_saved_one()
    {
        Assert.Contains(Check(pass: "", passwordRequired: true), p => p.Contains("password"));
        Assert.Empty(Check(pass: "", passwordRequired: false));
    }

}

public class DeviceDetailsTests
{
    private static byte[] Buffer(int analog, int digital, string serial = "SER123", string model = "XM-8CH")
    {
        var b = new byte[DeviceDetails.StructSize];
        System.Text.Encoding.ASCII.GetBytes(serial).CopyTo(b, 224);
        BitConverter.GetBytes(analog).CopyTo(b, 288);
        BitConverter.GetBytes(digital).CopyTo(b, 324);
        System.Text.Encoding.ASCII.GetBytes(model).CopyTo(b, 424);
        return b;
    }

    [Fact]
    public void Reads_serial_model_and_channel_counts_from_the_documented_offsets()
    {
        var d = DeviceDetails.Parse(Buffer(8, 0));

        Assert.Equal("SER123", d.SerialNumber);
        Assert.Equal("XM-8CH", d.Model);
        Assert.Equal(8, d.AnalogChannels);
        Assert.Equal(8, d.TotalChannels);
    }

    [Fact]
    public void Hybrid_recorders_count_analog_plus_ip_channels() =>
        Assert.Equal(12, DeviceDetails.Parse(Buffer(8, 4)).TotalChannels);

    [Theory]
    [InlineData(0, 0)]        // nothing reported
    [InlineData(-3, 0)]
    [InlineData(500, 0)]      // garbage, e.g. a layout mismatch
    [InlineData(1000000, 0)]
    [InlineData(65, 0)]       // just over the largest believable value
    public void Implausible_channel_counts_are_reported_as_unknown_zero(int analog, int digital) =>
        Assert.Equal(0, DeviceDetails.Parse(Buffer(analog, digital)).TotalChannels);

    [Fact]
    public void The_largest_believable_count_is_accepted() =>
        Assert.Equal(64, DeviceDetails.Parse(Buffer(64, 0)).TotalChannels);

    [Fact]
    public void Too_small_a_buffer_is_rejected() =>
        Assert.Throws<ArgumentException>(() => DeviceDetails.Parse(new byte[100]));

    [Fact]
    public void Camera_range_numbers_from_zero_and_empty_means_not_detected()
    {
        var cams = Camera.Range("Branch", 3);

        Assert.Equal(new[] { 0, 1, 2 }, cams.Select(c => c.Channel));
        Assert.Equal("Cam03", cams[2].FolderName);
        Assert.Empty(Camera.Range("Branch", 0));
        Assert.False(Make.Branch("X", 0).CamerasDetected);
        Assert.True(Make.Branch("X", 2).CamerasDetected);
    }
}

public class SdkLocatorTests
{
    private static string MakeSdk(TempDir parent, string name)
    {
        var dir = Path.Combine(parent.Path, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, SdkLocator.DllName), "x");
        return dir;
    }

    [Fact]
    public void Bundled_copy_wins_over_everything_else()
    {
        using var t = new TempDir();
        string bundled = MakeSdk(t, "bundled"), configured = MakeSdk(t, "configured"), cms = MakeSdk(t, "cms");

        Assert.Equal(bundled, SdkLocator.Find(configured, bundled, cms));
    }

    [Fact]
    public void Falls_back_to_the_configured_folder_then_the_cms_folder()
    {
        using var t = new TempDir();
        string configured = MakeSdk(t, "configured"), cms = MakeSdk(t, "cms");
        string missing = Path.Combine(t.Path, "nothing");

        Assert.Equal(configured, SdkLocator.Find(configured, missing, cms));
        Assert.Equal(cms, SdkLocator.Find(null, missing, cms));
        Assert.Equal(cms, SdkLocator.Find("", missing, cms));
    }

    [Fact]
    public void Returns_null_when_the_driver_is_nowhere()
    {
        using var t = new TempDir();
        string missing = Path.Combine(t.Path, "nothing");

        Assert.Null(SdkLocator.Find(null, missing, missing));
    }
}
