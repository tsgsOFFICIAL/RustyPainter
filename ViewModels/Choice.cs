using RustyPainter.Localization;
using RustyPainter.ViewModels.Mvvm;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RustyPainter.ViewModels;

/// <summary>One button of a group where exactly one can be selected.</summary>
public sealed class Choice : ViewModelBase
{
    private bool _isSelected;

    internal Choice(string label, object value, Action<Choice> select)
    {
        _label = label;
        Value = value;
        SelectCommand = new RelayCommand(() => select(this));
    }

    private readonly string _label;

    /// <summary>The text of the button. The label given is a key of the texts of the app, or a text that is used as it is, such as a number.</summary>
    public string Label => Loc.Get(_label);
    public object Value { get; }

    public RelayCommand SelectCommand { get; }

    public bool IsSelected { get => _isSelected; internal set => SetProperty(ref _isSelected, value); }
}

/// <summary>A row of buttons of which exactly one is selected, such as Fit, Fill and Stretch.</summary>
public sealed class ChoiceGroup<T> where T : notnull
{
    public ChoiceGroup(IEnumerable<(string Label, T Value)> items, T initial)
    {
        Choices = items.Select(item => new Choice(item.Label, item.Value, Pick)).ToList();
        Selected = initial;
        Mark();
    }

    /// <summary>The buttons, in order.</summary>
    public IReadOnlyList<Choice> Choices { get; }

    /// <summary>The selected value.</summary>
    public T Selected { get; private set; }

    /// <summary>Raised after the selection changed.</summary>
    public event Action? Changed;

    /// <summary>Selects the button holding <paramref name="value"/>. Does nothing if there is none.</summary>
    public void Select(T value)
    {
        if (Choices.All(choice => !Equals(choice.Value, value)) || Equals(Selected, value))
            return;

        Selected = value;
        Mark();
        Changed?.Invoke();
    }

    private void Pick(Choice choice) => Select((T)choice.Value);

    private void Mark()
    {
        foreach (Choice choice in Choices)
            choice.IsSelected = Equals(choice.Value, Selected);
    }
}
