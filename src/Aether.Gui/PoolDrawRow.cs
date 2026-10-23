using Aether.Core;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Gui;

public sealed record PoolDrawRow(string Viewer, bool StoppedByHost, Grade Grade, string Prize, string Time, bool Fresh,
    bool HiddenFromStream, bool CanToggleVisibility, IAsyncRelayCommand ToggleVisibilityCommand)
{
    public string VisibilityName => $"不挂出：{Viewer}的{Prize}";
    public string VisibilityHint => HiddenFromStream ? "重新挂到直播画面" : "不挂到直播画面";
}
