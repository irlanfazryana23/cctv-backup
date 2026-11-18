using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Desktop.ViewModels;

namespace CctvBackup.Desktop.Views;

public sealed class CmsCandidateItem : ViewModelBase
{
    private bool _isSelected;
    public Branch Branch { get; }
    public string Name => Branch.Name;
    public string Subtitle => Branch.Mode == LoginMode.Cloud ? $"Cloud: {Branch.CloudId}" : $"IP: {Branch.Address}:{Branch.Port}";
    public string StatusText { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public CmsCandidateItem(Branch branch, bool isSelected, string statusText)
    {
        Branch = branch;
        _isSelected = isSelected;
        StatusText = statusText;
    }
}

public partial class CmsImportWindow : Window
{
    private readonly BranchBook? _book;
    private readonly CredentialStore? _credentials;
    public ObservableCollection<CmsCandidateItem> Candidates { get; } = new();
    public int ImportedCount { get; private set; }

    public CmsImportWindow()
    {
        InitializeComponent();
    }

    public CmsImportWindow(BranchBook book, CredentialStore credentials) : this()
    {
        _book = book;
        _credentials = credentials;

        var listCandidates = this.FindControl<ListBox>("ListCandidates");
        if (listCandidates != null) listCandidates.ItemsSource = Candidates;

        // Auto-detect default CMS file if present
        if (File.Exists(CmsBranchImporter.DefaultDataFile))
        {
            try
            {
                var txtXml = this.FindControl<TextBox>("TxtXml");
                if (txtXml != null) txtXml.Text = File.ReadAllText(CmsBranchImporter.DefaultDataFile);
                ParseXml();
            }
            catch { }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        var btnBrowse = this.FindControl<Button>("BtnBrowseXml");
        if (btnBrowse != null) btnBrowse.Click += async (s, e) => await BrowseXmlAsync();

        var btnParse = this.FindControl<Button>("BtnParseXml");
        if (btnParse != null) btnParse.Click += (s, e) => ParseXml();

        var btnSelectAll = this.FindControl<Button>("BtnSelectAll");
        if (btnSelectAll != null) btnSelectAll.Click += (s, e) => SetSelection(true);

        var btnSelectNone = this.FindControl<Button>("BtnSelectNone");
        if (btnSelectNone != null) btnSelectNone.Click += (s, e) => SetSelection(false);

        var btnCancel = this.FindControl<Button>("BtnCancel");
        if (btnCancel != null) btnCancel.Click += (s, e) => Close(false);

        var btnImport = this.FindControl<Button>("BtnImport");
        if (btnImport != null) btnImport.Click += (s, e) => OnImport();
    }

    private async Task BrowseXmlAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select CMS XML File (Data.xml or device.xml)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("XML Files") { Patterns = new[] { "*.xml" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            string content = await reader.ReadToEndAsync();
            var txtXml = this.FindControl<TextBox>("TxtXml");
            if (txtXml != null) txtXml.Text = content;
            ParseXml();
        }
    }

    private void ParseXml()
    {
        if (_book == null) return;
        Candidates.Clear();
        var txtXml = this.FindControl<TextBox>("TxtXml");
        var txtStatus = this.FindControl<TextBlock>("TxtStatus");
        string xml = txtXml?.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(xml))
        {
            if (txtStatus != null) txtStatus.Text = "Please enter or select CMS XML content.";
            return;
        }

        try
        {
            var doc = XDocument.Parse(xml);
            var import = CmsBranchImporter.ImportDetailed(doc);
            var plan = CmsImportPlanner.Plan(import, _book.All);

            foreach (var cand in plan.Candidates)
            {
                Candidates.Add(new CmsCandidateItem(cand.Branch, cand.DefaultChecked, cand.Note ?? "New"));
            }

            if (txtStatus != null)
            {
                txtStatus.Text = $"Found {import.Branches.Count} total branches ({plan.Candidates.Count} new, {plan.AlreadyPresent.Count} already present, {import.Skipped.Count} skipped).";
            }
        }
        catch (Exception ex)
        {
            if (txtStatus != null) txtStatus.Text = $"XML parsing error: {ex.Message}";
        }
    }

    private void SetSelection(bool select)
    {
        foreach (var c in Candidates) c.IsSelected = select;
    }

    private void OnImport()
    {
        if (_book == null || _credentials == null) return;
        var selected = Candidates.Where(c => c.IsSelected).ToList();
        var txtStatus = this.FindControl<TextBlock>("TxtStatus");
        var txtPassword = this.FindControl<TextBox>("TxtDefaultPassword");
        string defaultPass = txtPassword?.Text ?? "";

        if (selected.Count == 0)
        {
            if (txtStatus != null) txtStatus.Text = "Please select at least one branch to import.";
            return;
        }

        int count = 0;
        foreach (var item in selected)
        {
            try
            {
                _book.Add(item.Branch);
                if (!string.IsNullOrEmpty(defaultPass))
                {
                    _credentials.Set(item.Branch.Name, new Credential(item.Branch.Username, defaultPass));
                }
                count++;
            }
            catch { }
        }

        ImportedCount = count;
        Close(true);
    }
}
