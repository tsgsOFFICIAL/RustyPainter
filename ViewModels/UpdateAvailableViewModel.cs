using RustyPainter.Localization;
using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Service.Logging;
using RustyPainter.Interface;
using System.Threading.Tasks;
using System;

namespace RustyPainter.ViewModels;

/// <summary>
/// Backs the "update available" prompt: the offered version, its changelog, and applying or
/// dismissing the update. <see cref="CloseRequested"/> lets the hosting
/// <c>UpdateAvailableWindow</c> close itself without this view model referencing <c>Window</c>
/// directly.
/// </summary>
public sealed class UpdateAvailableViewModel : ViewModelBase
{
    private readonly IUpdateService _updates;
    private readonly UpdateInfo _update;

    private string _statusText = "";
    private bool _isStatusVisible;
    private bool _isBusy;
    private bool _isFailed;
    private int _progressPercent;
    private bool _isProgressIndeterminate = true;

    public UpdateAvailableViewModel(IUpdateService updates, UpdateInfo update, string changelog)
    {
        _updates = updates;
        _update = update;

        Version = update.Version;
        Changelog = changelog;
        HasChangelog = changelog.Length > 0;

        LaterCommand = new RelayCommand(() => CloseRequested?.Invoke(), () => !IsBusy);
        UpdateNowCommand = new AsyncRelayCommand(UpdateNowAsync, () => !IsBusy);
    }

    public event Action? CloseRequested;

    public string Version { get; }
    public string Changelog { get; }
    public bool HasChangelog { get; }

    public RelayCommand LaterCommand { get; }
    public AsyncRelayCommand UpdateNowCommand { get; }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool IsStatusVisible { get => _isStatusVisible; private set => SetProperty(ref _isStatusVisible, value); }

    /// <summary>Whether the last attempt failed, so the status line can be styled as an error and the button reads "Retry".</summary>
    public bool IsFailed
    {
        get => _isFailed;
        private set
        {
            if (SetProperty(ref _isFailed, value))
                OnPropertyChanged(nameof(UpdateButtonText));
        }
    }

    public string UpdateButtonText => IsFailed ? Loc.Get("Retry") : Loc.Get("UpdateNow");

    /// <summary>Download completion, 0-100.</summary>
    public int ProgressPercent { get => _progressPercent; private set => SetProperty(ref _progressPercent, value); }

    /// <summary>True until the total download size is known (or while verifying/restarting), so the bar animates instead of sitting at 0.</summary>
    public bool IsProgressIndeterminate { get => _isProgressIndeterminate; private set => SetProperty(ref _isProgressIndeterminate, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            LaterCommand.RaiseCanExecuteChanged();
            UpdateNowCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task UpdateNowAsync()
    {
        IsBusy = true;
        IsFailed = false;
        IsStatusVisible = true;
        IsProgressIndeterminate = true;
        ProgressPercent = 0;
        StatusText = Loc.Get("UpdateStarting");

        // Progress<T> captures the UI synchronization context here, so callbacks land on the UI thread.
        Progress<UpdateProgress> progress = new(Report);

        try
        {
            // On success this exits the process directly, so nothing after this call runs on the happy path.
            await _updates.ApplyUpdateAsync(_update, progress);
        }
        catch (Exception ex)
        {
            AppLog.Error("UpdateAvailableViewModel", "Applying the update failed.", ex);
            StatusText = Loc.Format("UpdateFailed", ex.Message);
            IsFailed = true;
            IsBusy = false;
        }
    }

    private void Report(UpdateProgress p)
    {
        switch (p.Stage)
        {
            case UpdateStage.Downloading:
                IsProgressIndeterminate = p.BytesTotal <= 0;
                ProgressPercent = p.Percent;
                StatusText = p.Retry
                    ?? (p.BytesTotal > 0
                        ? Loc.Format("UpdateDownloadingOf", FormatBytes(p.BytesDone), FormatBytes(p.BytesTotal), p.Percent)
                        : Loc.Format("UpdateDownloading", FormatBytes(p.BytesDone)));
                break;

            case UpdateStage.Verifying:
                IsProgressIndeterminate = true;
                StatusText = Loc.Get("UpdateVerifying");
                break;

            case UpdateStage.Restarting:
                IsProgressIndeterminate = true;
                StatusText = Loc.Get("UpdateRestarting");
                break;
        }
    }

    /// <summary>Formats a byte count for display (for example "12.3 MB").</summary>
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
