using CommunityToolkit.Mvvm.Input;

namespace Aether.Gui;

public sealed record WinnerGroupRow(string Title, string Prize, string Progress, IReadOnlyList<WinnerRow> Winners)
{
    public bool IsEmpty => Winners.Count == 0;
}

public sealed record WinnerRow(string Name, string? Hint = null, bool Fresh = false,
    bool HiddenFromStream = false, bool CanToggleVisibility = false, IAsyncRelayCommand? ToggleVisibilityCommand = null)
{
    public string? ToolTip => Hint is null ? null : $"{Name} · {Hint}";
    public bool HasVisibilityCommand => ToggleVisibilityCommand is not null;
    public string VisibilityName => $"不挂出：{Name}";
    public string VisibilityHint => HiddenFromStream ? "重新挂到直播画面" : "不挂到直播画面";
}
