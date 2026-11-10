using System.Xml.Linq;
using CctvBackup.Core.Branches;
namespace CctvBackup.Tests;

public class CmsImportTests
{
    private static CmsImport Parse(string devices) =>
        CmsBranchImporter.ImportDetailed(XDocument.Parse($"<DATAROOT><DEVINFO>{devices}</DEVINFO></DATAROOT>"));

    private static Branch Cloud(string name, string id) =>
        new(Name: name, Area: "", CloudId: id, Port: Branch.DefaultPort, Username: "u", Cameras: new List<Camera>());

    [Fact]
    public void Ip_login_type_0_becomes_ip_branch_with_address_and_port()
    {
        var r = Parse("""<DEV ip="0" name="A" host="192.168.1.10" port="34568" username="u" password="SECRET"/>""");
        var b = Assert.Single(r.Branches);
        Assert.Equal(LoginMode.Ip, b.Mode);
        Assert.Equal("192.168.1.10", b.Address);
        Assert.Equal(34568, b.Port);
        Assert.Equal("", b.CloudId);
    }

    [Fact]
    public void Domain_login_type_1_becomes_ip_branch()
    {
        var r = Parse("""<DEV ip="1" name="A" host="dvr.example.com" port="34567"/>""");
        var b = Assert.Single(r.Branches);
        Assert.Equal(LoginMode.Ip, b.Mode);
        Assert.Equal("dvr.example.com", b.Address);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("4")]
    public void Arsp_and_ipv6_are_skipped_with_a_reason(string type)
    {
        var r = Parse($"""<DEV ip="{type}" name="A" host="x"/>""");
        Assert.Empty(r.Branches);
        var s = Assert.Single(r.Skipped);
        Assert.Equal("A", s.Name);
        Assert.False(string.IsNullOrWhiteSpace(s.Reason));
    }

    [Fact]
    public void Invalid_ip_address_is_skipped()
    {
        var r = Parse("""<DEV ip="0" name="A" host="not a host!"/>""");
        Assert.Empty(r.Branches);
        Assert.Single(r.Skipped);
    }

    [Fact]
    public void Missing_ip_attribute_is_treated_as_cloud()
    {
        var b = Assert.Single(Parse("""<DEV name="A" host="aaaa" port="1234"/>""").Branches);
        Assert.Equal(LoginMode.Cloud, b.Mode);
        Assert.Equal("aaaa", b.CloudId);
    }

    [Fact]
    public void Entry_without_host_is_skipped_with_a_reason()
    {
        var r = Parse("""<DEV ip="3" name="A"/>""");
        Assert.Empty(r.Branches);
        Assert.Single(r.Skipped);
    }

    [Fact]
    public void Password_is_never_exposed_in_branches_or_skipped()
    {
        var r = Parse("""
            <DEV ip="3" name="A" host="aaaa" password="SECRET-1"/>
            <DEV ip="0" name="B" host="10.0.0.1" password="SECRET-2"/>
            <DEV ip="2" name="C" host="x" password="SECRET-3"/>
            """);
        Assert.DoesNotContain("SECRET", System.Text.Json.JsonSerializer.Serialize(r));
    }

    [Fact]
    public void Planner_lists_new_branches_and_ticks_them_by_default()
    {
        var plan = CmsImportPlanner.Plan(Parse("""<DEV ip="3" name="New" host="nnnn"/>"""), new List<Branch>());
        var c = Assert.Single(plan.Candidates);
        Assert.Null(c.Note);
        Assert.True(c.DefaultChecked);
    }

    [Fact]
    public void Planner_leaves_out_a_branch_with_the_same_name_ignoring_case()
    {
        var plan = CmsImportPlanner.Plan(
            Parse("""<DEV ip="3" name="Main" host="nnnn"/>"""),
            new[] { Cloud("MAIN", "other") });
        Assert.Empty(plan.Candidates);
        Assert.Equal(new[] { "Main" }, plan.AlreadyPresent);
    }

    [Fact]
    public void Planner_reports_a_name_that_shares_a_backup_folder_with_an_existing_branch()
    {
        // "A:B" and "A_B" sanitize to the same folder.
        var plan = CmsImportPlanner.Plan(
            Parse("""<DEV ip="3" name="A:B" host="nnnn"/>"""),
            new[] { Cloud("A_B", "other") });
        Assert.Empty(plan.Candidates);
        Assert.Equal("A:B", Assert.Single(plan.Unsupported).Name);
    }

    [Fact]
    public void Planner_notes_the_same_dvr_under_another_name_and_leaves_it_unticked()
    {
        var plan = CmsImportPlanner.Plan(
            Parse("""<DEV ip="3" name="New name" host="SAMEID"/>"""),
            new[] { Cloud("Old name", "sameid") });
        var c = Assert.Single(plan.Candidates);
        Assert.NotNull(c.Note);
        Assert.Contains("Old name", c.Note);
        Assert.False(c.DefaultChecked);
    }

    [Fact]
    public void Planner_keeps_only_one_of_two_names_that_share_a_folder_inside_the_file()
    {
        var plan = CmsImportPlanner.Plan(
            Parse("""
                <DEV ip="3" name="X:Y" host="aaaa"/>
                <DEV ip="3" name="X_Y" host="bbbb"/>
                """),
            new List<Branch>());
        Assert.Single(plan.Candidates);
        Assert.Single(plan.Unsupported);
    }

    [Fact]
    public void Planner_includes_importer_skips_in_the_unsupported_list()
    {
        var plan = CmsImportPlanner.Plan(Parse("""<DEV ip="2" name="Z" host="x"/>"""), new List<Branch>());
        Assert.Equal("Z", Assert.Single(plan.Unsupported).Name);
    }
}
