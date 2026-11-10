using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

/// <summary>A row of the 演出表 the streamer can turn off on the 桌宠 page, with when it plays as the thresholds now say.</summary>
public sealed partial class DesktopPetShowRow(Show show) : ObservableObject
{
    public Show Show => show;
    [ObservableProperty] private bool on;
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private string note = "";
}

/// <summary>A group of 演出 in the 演出 tab's list, and how many of its rows play.</summary>
public sealed class DesktopPetShowGroup : ObservableObject
{
    public DesktopPetShowGroup(ShowGroup group, IReadOnlyList<DesktopPetShowRow> rows, bool hasThresholds)
    {
        Group = group;
        Rows = rows;
        HasThresholds = hasThresholds;
        foreach (var row in rows)
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(DesktopPetShowRow.On)) return;
                OnPropertyChanged(nameof(Count));
                OnPropertyChanged(nameof(AnyOn));
            };
    }

    public ShowGroup Group { get; }
    public IReadOnlyList<DesktopPetShowRow> Rows { get; }
    /// <summary>Whether the 中礼物 and 大礼物 thresholds are set here.</summary>
    public bool HasThresholds { get; }
    public bool AnyOn => Rows.Any(row => row.On);

    public string Count => (Rows.Count(row => row.On), Rows.Count) switch
    {
        (var on, 1) => on == 1 ? "开" : "关",
        (var on, var all) when on == all => "全开",
        (var on, var all) => $"{on}/{all} 开"
    };
}

/// <summary>A 试演 the streamer can pick: a row of the 演出表, one of its tests, and how the list names it.</summary>
public sealed record DesktopPetTest(string Show, string? Variant, string Label)
{
    public override string ToString() => Label;
}
