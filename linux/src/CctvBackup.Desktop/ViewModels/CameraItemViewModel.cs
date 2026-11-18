using CctvBackup.Core.Branches;

namespace CctvBackup.Desktop.ViewModels;

public sealed class CameraItemViewModel : ViewModelBase
{
    private bool _isChecked;
    private readonly Camera _camera;

    public CameraItemViewModel(Camera camera, bool isChecked = true)
    {
        _camera = camera;
        _isChecked = isChecked;
    }

    public int Channel => _camera.Channel;
    public string Name => _camera.Name;

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}
