using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.ComponentModel;

namespace RustyPainter.ViewModels.Mvvm;

/// <summary>
/// Base class for view models: raises <see cref="INotifyPropertyChanged.PropertyChanged"/> via
/// <see cref="SetProperty{T}"/> so properties can be written as plain auto-backed get/set pairs.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Sets <paramref name="field"/> to <paramref name="value"/> and raises
    /// <see cref="PropertyChanged"/> for <paramref name="propertyName"/>, unless the value is unchanged.
    /// </summary>
    /// <returns><see langword="true"/> if the value changed, <see langword="false"/> otherwise.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
