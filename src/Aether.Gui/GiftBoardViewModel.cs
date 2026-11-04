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
    private Task change = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows), nameof(CanClear), nameof(CountText))]
    private IReadOnlyList<GiftBoardListRow> rows = [];

    // The viewers on the board: one viewer can have a row for each kind of gift.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int viewers;

    /// <summary>Whether the board is on in the settings Core last applied, which a draft on the page does not change.</summary>
    [ObservableProperty]
    private bool enabled;

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
    public string CountText => HasRows ? $"共 {Viewers} 位" : "";
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

    internal async Task StopAsync()
    {
        // The page reports failures; closing still waits until a clear or restore has finished writing.
        try { await change; }
        catch { }
        await updates.StopAsync();
    }

    internal void Apply(GiftBoardState state)
    {
        Viewers = state.Rows.Select(row => row.Uid).Distinct().Count();
        // A row's value is in 金瓜子, and a 大航海 averaged over its months can be a fraction of a 电池.
        Rows = [.. state.Rows.Select((row, index) => new GiftBoardListRow(index + 1, row.Nickname, $"{row.GiftName} ×{row.Num}",
            Battery.Text(row.Value)))];
        Enabled = state.Enabled;
        Period = state.Period;
        Previous = state.Previous;
        // What was asked for can no longer be done, e.g. the room was left.
        if (!CanConfirm) Confirmation = GiftBoardConfirmation.None;
    }

    // Their buttons are only enabled while there is something to clear or to restore.
    internal void RequestClear() => Confirmation = GiftBoardConfirmation.Clear;

    internal void RequestRestore() => Confirmation = GiftBoardConfirmation.Restore;

    internal void CancelConfirmation() => Confirmation = GiftBoardConfirmation.None;

    /// <summary>Does what the footer asked, if it can still be done, off the UI thread. Returns what was done, and why it
    /// failed if it did: the database could not be written, or the room was left in the meantime.</summary>
    internal async Task<(GiftBoardConfirmation Done, string? Error)> ConfirmAsync()
    {
        var done = CanConfirm ? Confirmation : GiftBoardConfirmation.None;
        Confirmation = GiftBoardConfirmation.None;
        if (done == GiftBoardConfirmation.None) return (done, null);
        try
        {
            await (change = Task.Run(done == GiftBoardConfirmation.Clear ? client.GiftBoard.Clear : client.GiftBoard.Restore));
            return (done, null);
        }
        catch (Exception error) { return (done, error.Message); }
    }
}
