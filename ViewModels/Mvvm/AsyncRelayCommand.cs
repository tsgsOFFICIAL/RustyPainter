using System.Threading.Tasks;
using System.Windows.Input;
using System;

namespace RustyPainter.ViewModels.Mvvm;

/// <summary>
/// An <see cref="ICommand"/> that invokes an async delegate, disabling itself (via
/// <see cref="CanExecute"/>) for the duration of the call so a slow operation can't be re-triggered
/// while already in flight.
/// </summary>
public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _isRunning;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isRunning && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        _isRunning = true;
        RaiseCanExecuteChanged();

        try
        {
            await execute();
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}