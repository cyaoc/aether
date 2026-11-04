using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Aether.Gui;

/// <summary>One line of the 当前榜单, in the order the 礼物榜页 shows its rows: place, nickname, gift and the row's 价值 in 电池.</summary>
public sealed record GiftBoardListRow(int Rank, string Nickname, string Gift, string Value);

/// <summary>What the footer asks the streamer to confirm.</summary>
public enum GiftBoardConfirmation { None, Clear, Restore }

/// <summary>The 礼物榜 section's view of the connected room's 礼物榜: what Core reports, and the clear and restore it offers.
/// It lists the rows Core reports and does no ranking of its own.</summary>
public partial class GiftBoardViewModel(AetherClient client, ILogger logger, TimeProvider clock, CancellationToken shutdown) : ObservableObject
{
    private readonly LatestRun updates = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows), nameof(CanClear), nameof(CountText))]
    private IReadOnlyList<GiftBoardListRow> rows = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodText), nameof(ConfirmText))]
    private GiftBoardPeriod? period;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestore), nameof(ConfirmText))]
    private GiftBoardPeriod? previous;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Confirming), nameof(ConfirmingClear), nameof(ConfirmingRestore), nameof(ConfirmText))]
    private GiftBoardConfirmation confirmation;

    public bool HasRows => Rows.Count > 0;
    public bool CanClear => HasRows;
    public bool CanRestore => Previous is not null;
    public bool Confirming => Confirmation != GiftBoardConfirmation.None;
    public bool ConfirmingClear => Confirmation == GiftBoardConfirmation.Clear;
    public bool ConfirmingRestore => Confirmation == GiftBoardConfirmation.Restore;
    public string CountText => HasRows ? $"共 {Rows.Count} 位" : "";
    public string PeriodText => Period is { } current ? $"本期从 {LocalDateText.DateAndTime(current.StartedAt, clock.GetLocalNow())} 开始" : "";

    public string ConfirmText => Confirmation switch
    {
        GiftBoardConfirmation.Clear => "清空后直播画面上的礼物榜也会变空，确定吗？",
        GiftBoardConfirmation.Restore when Previous is { } earlier =>
            $"上一期（{LocalDateText.Date(earlier.StartedAt, clock.GetLocalNow())}，{earlier.Viewers} 位）会{(Period is { Viewers: 0 } ? "恢复" : "合并")}回来，确定吗？",
        _ => "",
    };

    private bool CanConfirm => Confirmation switch
    {
        GiftBoardConfirmation.Clear => CanClear,
        GiftBoardConfirmation.Restore => CanRestore,
        _ => false,
    };

    internal void Observe()
    {
        if (shutdown.IsCancellationRequested || !updates.Current.IsCompleted) return;
        updates.Follow(client.GiftBoard.GetUpdatesAsync, Apply, logger, "礼物榜状态订阅中断");
    }

    internal Task StopAsync() => updates.StopAsync();

    internal void Apply(GiftBoardState state)
    {
        // A row's value is in 金瓜子, and a 大航海 averaged over its months can be a fraction of a 电池.
        Rows = [.. state.Rows.Select((row, index) => new GiftBoardListRow(index + 1, row.Nickname, $"{row.GiftName} ×{row.Num}",
            $"{(decimal)row.Value / GiftBoardController.CoinsPerBattery:0.##} 电池"))];
        Period = state.Period;
        Previous = state.Previous;
        // What was asked for can no longer be done, e.g. the room was left.
        if (!CanConfirm) Confirmation = GiftBoardConfirmation.None;
    }

    internal void RequestClear()
    {
        if (CanClear) Confirmation = GiftBoardConfirmation.Clear;
    }

    internal void RequestRestore()
    {
        if (CanRestore) Confirmation = GiftBoardConfirmation.Restore;
    }

    internal void CancelConfirmation() => Confirmation = GiftBoardConfirmation.None;

    /// <summary>Does what the footer asked, if it can still be done; returns what was done.</summary>
    internal GiftBoardConfirmation Confirm()
    {
        var done = CanConfirm ? Confirmation : GiftBoardConfirmation.None;
        Confirmation = GiftBoardConfirmation.None;
        // shortcut: runs the SQLite write on the UI thread, as Save writes the settings file here; move it off if restoring a very long 总榜 stalls the window.
        if (done == GiftBoardConfirmation.Clear) client.GiftBoard.Clear();
        if (done == GiftBoardConfirmation.Restore) client.GiftBoard.Restore();
        return done;
    }
}
