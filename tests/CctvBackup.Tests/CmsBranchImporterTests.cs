using System.Xml.Linq;
using CctvBackup.Core.Branches;

namespace CctvBackup.Tests;

public class CmsBranchImporterTests
{
    // Sanitized sample: fake ids and fake (non-secret) password values.
    private const string Sample = """
        <DATAROOT>
          <DEVINFO>
            <DEV id="1" ip="3" area="AREA X" name="Big Branch" host="aaaaaaaaaaaaaaaa" port="52841" cameras="16" username="userA" password="SECRET-ONE">
              <DEV id="2" type="0" name="Big Branch_1" channel="0"/>
              <DEV id="3" type="0" name="Big Branch_2" channel="1"/>
              <DEV id="4" type="1" name="Big Branch_alarm1" channel="0"/>
            </DEV>
            <DEV id="10" ip="3" area="AREA X" name="Small Branch" host="bbbbbbbbbbbbbbbb" port="34567" cameras="8" username="userB" password="SECRET-TWO">
              <DEV id="11" type="0" name="Small Branch_1" channel="0"/>
              <DEV id="12" type="1" name="Small Branch_alarm1" channel="0"/>
              <DEV id="13" type="1" name="Small Branch_alarm2" channel="1"/>
            </DEV>
            <DEV id="20" name="No Host Branch" port="34567"/>
          </DEVINFO>
        </DATAROOT>
        """;

    [Fact]
    public void Imports_branches_sorted_by_name_and_skips_entries_without_host()
    {
        var branches = CmsBranchImporter.Import(XDocument.Parse(Sample));

        Assert.Equal(new[] { "Big Branch", "Small Branch" }, branches.Select(b => b.Name));
    }

    [Fact]
    public void Reads_cloud_id_port_area_and_username()
    {
        var big = CmsBranchImporter.Import(XDocument.Parse(Sample)).First(b => b.Name == "Big Branch");

        Assert.Equal("aaaaaaaaaaaaaaaa", big.CloudId);
        Assert.Equal(Branch.DefaultPort, big.Port);   // cloud login ignores the CMS port
        Assert.Equal("AREA X", big.Area);
        Assert.Equal("userA", big.Username);
    }

    [Fact]
    public void Keeps_cameras_and_ignores_alarm_inputs()
    {
        var branches = CmsBranchImporter.Import(XDocument.Parse(Sample));

        var big = branches.First(b => b.Name == "Big Branch");
        Assert.Equal(new[] { 0, 1 }, big.Cameras.Select(c => c.Channel));
        Assert.Single(branches.First(b => b.Name == "Small Branch").Cameras);
    }

    [Fact]
    public void Never_exposes_passwords()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(CmsBranchImporter.Import(XDocument.Parse(Sample)));

        Assert.DoesNotContain("SECRET", json);
    }

    [Fact]
    public void Camera_folder_name_is_one_based()
    {
        Assert.Equal("Cam01", new Camera(0, "x").FolderName);
        Assert.Equal("Cam16", new Camera(15, "x").FolderName);
    }

    [Fact]
    public void Empty_document_gives_empty_list()
    {
        Assert.Empty(CmsBranchImporter.Import(XDocument.Parse("<DATAROOT/>")));
    }

}
