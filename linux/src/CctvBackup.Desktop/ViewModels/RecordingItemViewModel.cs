using Avalonia.Media;
using CctvBackup.Core.Backup;

namespace CctvBackup.Desktop.ViewModels;

public sealed class RecordingItemViewModel : ViewModelBase
{
    private static readonly IBrush GreenBg = Brush.Parse("#dcfce7");
    private static readonly IBrush GreenFg = Brush.Parse("#15803d");
    private static readonly IBrush BlueBg = Brush.Parse("#dbeafe");
    private static readonly IBrush BlueFg = Brush.Parse("#1d4ed8");
    private static readonly IBrush RedBg = Brush.Parse("#fee2e2");
    private static readonly IBrush RedFg = Brush.Parse("#b91c1c");
    private static readonly IBrush AmberBg = Brush.Parse("#fef3c7");
    private static readonly IBrush AmberFg = Brush.Parse("#b45309");
    private static readonly IBrush GrayBg = Brush.Parse("#f1f5f9");
    private static readonly IBrush GrayFg = Brush.Parse("#64748b");
    private static readonly IBrush IndigoBg = Brush.Parse("#eef2ff");
    private static readonly IBrush IndigoFg = Brush.Parse("#4338ca");
    private static readonly IBrush ProgressGreen = Brush.Parse("#16a34a");
    private static readonly IBrush ProgressBlue = Brush.Parse("#2563eb");
    private static readonly IBrush ProgressRed = Brush.Parse("#dc2626");

    private readonly DownloadJob _job;

    public RecordingItemViewModel(DownloadJob job)
    {
        _job = job;
    }

    public DownloadJob Job => _job;

    public bool IsSelected
    {
        get => _job.Selected;
        set
        {
            if (_job.CanChangeSelection && _job.Selected != value)
            {
                _job.Selected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBadgeBackground));
                OnPropertyChanged(nameof(StatusBadgeForeground));
            }
        }
    }

    public bool CanChangeSelection => _job.CanChangeSelection;
    public string BranchName => _job.BranchName;
    public string CameraName => _job.CameraName;
    public string Period => _job.Period;
    public string DateText => _job.File.Begin.ToString("ddd, yyyy-MM-dd");
    public string TimeRangeText => $"{_job.File.Begin:HH:mm} – {_job.File.End:HH:mm}";
    public string SizeText => _job.SizeText;
    public int Percent => _job.Percent;
    public string PercentText => HasProgress ? $"{_job.Percent}%" : "";
    public bool HasProgress => _job.Status is JobStatus.Downloading or JobStatus.Done or JobStatus.Failed;
    public string StatusText => _job.StatusText;
    public string SpeedText => _job.SpeedText;
    public string LeftText => _job.LeftText;
    public string? Error => _job.Error;
    public bool HasError => !string.IsNullOrWhiteSpace(_job.Error) && _job.Status != JobStatus.Done;

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(PercentText));
        OnPropertyChanged(nameof(HasProgress));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadgeBackground));
        OnPropertyChanged(nameof(StatusBadgeForeground));
        OnPropertyChanged(nameof(ProgressBrush));
        OnPropertyChanged(nameof(SpeedText));
        OnPropertyChanged(nameof(LeftText));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(CanChangeSelection));
    }

    public IBrush StatusBadgeBackground => _job.Status switch
    {
        JobStatus.Done => GreenBg,
        JobStatus.Downloading => BlueBg,
        JobStatus.Failed => RedBg,
        JobStatus.Cancelled => AmberBg,
        JobStatus.Skipped => GreenBg,
        _ => _job.Selected ? IndigoBg : GrayBg
    };

    public IBrush StatusBadgeForeground => _job.Status switch
    {
        JobStatus.Done => GreenFg,
        JobStatus.Downloading => BlueFg,
        JobStatus.Failed => RedFg,
        JobStatus.Cancelled => AmberFg,
        JobStatus.Skipped => GreenFg,
        _ => _job.Selected ? IndigoFg : GrayFg
    };

    public IBrush ProgressBrush => _job.Status switch
    {
        JobStatus.Done => ProgressGreen,
        JobStatus.Failed => ProgressRed,
        _ => ProgressBlue
    };
}
