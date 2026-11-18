using CctvBackup.Core.Branches;

namespace CctvBackup.Desktop.ViewModels;

public sealed class BranchItemViewModel : ViewModelBase
{
    private bool _isChecked;
    private Branch _branch;

    public BranchItemViewModel(Branch branch, bool isChecked = false)
    {
        _branch = branch;
        _isChecked = isChecked;
    }

    public Branch Branch
    {
        get => _branch;
        set
        {
            if (SetProperty(ref _branch, value))
            {
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(Area));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(CameraCount));
                OnPropertyChanged(nameof(CameraCountText));
            }
        }
    }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    public string Name => _branch.Name;
    public string Area => _branch.Area;
    public int CameraCount => _branch.Cameras.Count;
    public string CameraCountText => $"{_branch.Cameras.Count} cam{(_branch.Cameras.Count == 1 ? "" : "s")}";

    public string Subtitle => _branch.Mode == LoginMode.Cloud
        ? $"Cloud: {_branch.CloudId}"
        : $"IP: {_branch.Address}:{(_branch.Port > 0 ? _branch.Port : Branch.DefaultPort)}";
}
