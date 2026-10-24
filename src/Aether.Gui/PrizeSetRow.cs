using CommunityToolkit.Mvvm.Input;

namespace Aether.Gui;

/// <summary>A 载入方案 menu entry; its MenuItem container binds these, so the footer is just never enabled.</summary>
public abstract record PrizeSetMenuEntry(bool IsEnabled, IRelayCommand? Command = null, bool Chosen = false, string? AccessibleName = null);

public sealed record PrizeSetRow(string Name, string Count, string Summary, IAsyncRelayCommand Delete,
    bool IsEnabled, IRelayCommand Command, bool Chosen) : PrizeSetMenuEntry(IsEnabled, Command, Chosen, $"载入方案：{Name}")
{
    public string DeleteName => $"删除方案{Name}";
}

public sealed record PrizeSetMenuFooter(string? Empty, string Hint) : PrizeSetMenuEntry(false);
