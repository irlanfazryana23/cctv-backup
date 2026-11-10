using CctvBackup.Core.Credentials;

namespace CctvBackup.Tests;

public class CredentialStoreTests
{
    private static CredentialStore Open(string path) => new(path, new XorProtector());

    [Fact]
    public void Login_round_trips_across_instances()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "c.dat");

        Open(path).Set("Branch A", new Credential("alice", "pw1"));

        Assert.Equal(new Credential("alice", "pw1"), Open(path).Get("Branch A"));
    }

    [Fact]
    public void Each_branch_has_its_own_login_and_lookup_ignores_case()
    {
        using var dir = new TempDir();
        var store = Open(Path.Combine(dir.Path, "c.dat"));
        store.Set("Branch A", new Credential("a", "1"));
        store.Set("Branch B", new Credential("b", "2"));

        Assert.Equal("a", store.Get("branch a")!.Username);
        Assert.Equal("b", store.Get("Branch B")!.Username);
        Assert.Null(store.Get("Branch C"));
    }

    [Fact]
    public void Remove_deletes_only_that_branch()
    {
        using var dir = new TempDir();
        var store = Open(Path.Combine(dir.Path, "c.dat"));
        store.Set("A", new Credential("a", "1"));
        store.Set("B", new Credential("b", "2"));

        Assert.True(store.Remove("A"));
        Assert.False(store.Remove("A"));
        Assert.Null(store.Get("A"));
        Assert.NotNull(store.Get("B"));
    }

    [Fact]
    public void Rename_moves_the_login_to_the_new_name()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "c.dat");
        var store = Open(path);
        store.Set("Old", new Credential("a", "1"));

        store.Rename("Old", "New");

        Assert.Null(store.Get("Old"));
        Assert.Equal("1", Open(path).Get("New")!.Password);
    }

    [Fact]
    public void Rename_of_an_unknown_branch_does_nothing()
    {
        using var dir = new TempDir();
        var store = Open(Path.Combine(dir.Path, "c.dat"));

        store.Rename("Nope", "New");

        Assert.Null(store.Get("New"));
    }

    [Fact]
    public void Empty_store_returns_null()
    {
        using var dir = new TempDir();
        Assert.Null(Open(Path.Combine(dir.Path, "c.dat")).Get("A"));
    }

    [Fact]
    public void File_on_disk_does_not_contain_the_password_in_clear_text()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "c.dat");
        Open(path).Set("A", new Credential("alice", "Sup3rSecretPw"));

        var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path));
        Assert.DoesNotContain("Sup3rSecretPw", text);
    }

    [Fact]
    public void Unreadable_file_starts_empty_instead_of_throwing()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "c.dat");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });

        Assert.Null(Open(path).Get("A"));
    }

    [Fact]
    public void File_from_the_first_version_still_loads_its_branch_logins()
    {
        // v1 stored { "Shared": ..., "Overrides": { name: credential } }; the new store keeps Overrides.
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "c.dat");
        var json = """{"Shared":{"Username":"s","Password":"p"},"Overrides":{"Old Branch":{"Username":"u","Password":"pw"}}}""";
        File.WriteAllBytes(path, new XorProtector().Protect(System.Text.Encoding.UTF8.GetBytes(json)));

        Assert.Equal("u", Open(path).Get("old branch")!.Username);
    }

    [Fact]
    public void Real_dpapi_round_trip_works_on_this_machine()
    {
        Assert.True(OperatingSystem.IsWindows());
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "c.dat");

        new CredentialStore(path).Set("A", new Credential("alice", "pw1"));

        Assert.Equal("pw1", new CredentialStore(path).Get("A")!.Password);
    }
}
