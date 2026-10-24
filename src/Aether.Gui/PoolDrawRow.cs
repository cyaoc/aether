using Aether.Core;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Gui;

public sealed record PoolDrawRow(string Viewer, bool StoppedByHost, Grade Grade, string Prize, string Time, bool Fresh,
    bool HiddenFromStream, bool CanToggleVisibility, IAsyncRelayCommand ToggleVisibilityCommand)
{
    public string VisibilityName => DrawVisibility.Name($"{Viewer}的{Prize}");
    public string VisibilityHint => DrawVisibility.Hint(HiddenFromStream);
}
