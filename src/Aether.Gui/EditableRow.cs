using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public abstract partial class EditableRow(bool isNew = false, bool canEdit = false) : ObservableObject
{
    private bool errorsRevealed = !isNew;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(VisibleError))] private string? error;
    [ObservableProperty] private int number;
    [ObservableProperty] private bool canRemove;
    [ObservableProperty] private bool canEdit = canEdit;
    [ObservableProperty] private bool isShown = true;
    internal event Action? Changed;
    internal event Action? FocusRequested;
    internal Action? RemoveRow { get; set; }
    internal bool FocusPending { get; set; }
    public bool ErrorsRevealed => errorsRevealed;
    public string? VisibleError => ErrorsRevealed ? Error : null;

    protected bool SetInput<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name)) return false;
        Changed?.Invoke();
        return true;
    }

    public void RevealErrors()
    {
        if (!SetProperty(ref errorsRevealed, true, nameof(ErrorsRevealed))) return;
        OnPropertyChanged(nameof(VisibleError));
        Changed?.Invoke();
    }

    public void Remove()
    {
        if (CanRemove && CanEdit) RemoveRow?.Invoke();
    }

    public void RequestFocus()
    {
        FocusPending = true;
        FocusRequested?.Invoke();
    }
}
