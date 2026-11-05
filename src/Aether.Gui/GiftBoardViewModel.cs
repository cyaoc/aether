using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Aether.Gui;

/// <summary>One line of the 当前榜单, in the order the 礼物榜页 shows its rows: place, nickname, gift and the row's 价值 in 电池.</summary>
public sealed record GiftBoardListRow(int Rank, string Nickname, string Gift, string Value);

/// <summary>What the footer asks the streamer to confirm.</summary>
public enum GiftBoardConfirmation { None, End, NewPeriod }

/// <summary>The 礼物榜 section's view of the connected room's 礼物榜, run in 期 like a lottery (ADR 0014): the state Core reports,
/// the 期 actions it allows, and the options of the latest 期, which apply as soon as they are changed. Core decides the state
/// and the ranking; this only shows them.</summary>
public partial class GiftBoardViewModel(AetherClient client, ILogger logger, TimeProvider clock, CancellationToken shutdown) : ObservableObject
{
    private readonly LatestRun updates = new();
    // Core's writes, one after another off the UI thread, so the last option set is the one that stays.
    private Task change = Task.CompletedTask;
    // The room Core last reported: each write names the room it was asked for in, and Core refuses it once that room is gone.
    private long? roomId;
    private int optionWrites;
    // The options Core last reported, and those the inputs last showed or sent to Core.
    private GiftBoardOptions reported = GiftBoardOptions.Default;
    private GiftBoardOptions shown = GiftBoardOptions.Default;
    private bool showing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows), nameof(CountText))]
    private IReadOnlyList<GiftBoardListRow> rows = [];

    // The viewers on the board: one viewer can have a row for each kind of gift.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int viewers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotStarted), nameof(FooterText), nameof(EmptyText))]
    private bool connected;

    /// <summary>Where the latest 期 stands, as Core reports it; only a running one takes gifts and shows on the 礼物榜页.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Running), nameof(Ended), nameof(NotStarted), nameof(StatusText), nameof(EmptyText))]
    private GiftBoardPhase phase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodText))]
    private GiftBoardPeriod? period;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Confirming), nameof(ConfirmingEnd), nameof(ConfirmingNewPeriod))]
    private GiftBoardConfirmation confirmation;

    /// <summary>What the last action did; the footer shows the hint for the options until there is one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FooterText))]
    private string? message;

    [ObservableProperty]
    private string? error;

    // The inputs start at the options Core starts with.
    [ObservableProperty]
    private decimal? threshold = GiftBoardOptions.Default.Threshold;

    [ObservableProperty]
    private string? thresholdError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ByUnitPrice), nameof(ByTotalPrice))]
    private int thresholdBy = GiftBoardOptions.Default.ThresholdBy;

    [ObservableProperty]
    private bool mascot = GiftBoardOptions.Default.Mascot;

    [ObservableProperty]
    private double volume = GiftBoardOptions.Default.Volume;

    public bool Running => Phase == GiftBoardPhase.Running;
    public bool Ended => Phase == GiftBoardPhase.Ended;
    public bool NotStarted => Connected && Phase == GiftBoardPhase.NotStarted;
    public bool HasRows => Rows.Count > 0;
    public bool Confirming => Confirmation != GiftBoardConfirmation.None;
    public bool ConfirmingEnd => Confirmation == GiftBoardConfirmation.End;
    public bool ConfirmingNewPeriod => Confirmation == GiftBoardConfirmation.NewPeriod;
    public string StatusText => Phase switch { GiftBoardPhase.Running => "统计中", GiftBoardPhase.Ended => "已结束", _ => "还没开始" };
    public string CountText => HasRows ? $"共 {Viewers} 位" : "";

    public bool ByUnitPrice
    {
        get => ThresholdBy == GiftBoardOptions.ByUnitPrice;
        set { if (value) ThresholdBy = GiftBoardOptions.ByUnitPrice; }
    }

    public bool ByTotalPrice
    {
        get => ThresholdBy == GiftBoardOptions.ByTotalPrice;
        set { if (value) ThresholdBy = GiftBoardOptions.ByTotalPrice; }
    }

    public string PeriodText
    {
        get
        {
            var now = clock.GetLocalNow();
            return Period switch
            {
                { EndedAt: { } ended } current => $"本期 {LocalDateText.DateAndTime(current.StartedAt, now)} – " +
                    (ended.ToLocalTime().Date == current.StartedAt.ToLocalTime().Date ? $"{ended.ToLocalTime():HH:mm}" : LocalDateText.DateAndTime(ended, now)) + "，已结束",
                { } current => $"本期从 {LocalDateText.DateAndTime(current.StartedAt, now)} 开始",
                null => "",
            };
        }
    }

    public string FooterText => Message ?? (Connected ? "选项改了立即生效，开始新一期时沿用" : "选项跟着直播间的期，连上直播间后才能改");

    public string EmptyText => !Connected ? "连上直播间后，这里显示这个直播间的礼物榜。" : Phase switch
    {
        GiftBoardPhase.NotStarted => "还没有开始统计。点右上角“开始新一期”，之后收到的礼物会出现在这里和直播画面上。",
        GiftBoardPhase.Running => "还没有人上榜。达到门槛的礼物会出现在这里和直播画面上。",
        _ => "这一期没有人上榜。",
    };

    internal void Observe()
    {
        if (shutdown.IsCancellationRequested || !updates.Current.IsCompleted) return;
        updates.Follow(client.GiftBoard.GetUpdatesAsync, Apply, logger, "礼物榜状态订阅中断");
    }

    internal async Task StopAsync()
    {
        // Each write reports its own failure; closing still waits until the last one has finished.
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
        // A room joined or left: what the last action did belongs to the room before.
        if (Connected != state.Connected) Message = Error = null;
        var roomChanged = roomId != state.RoomId;
        roomId = state.RoomId;
        Connected = state.Connected;
        Phase = state.Phase;
        Period = state.Period;
        reported = state.Options;
        FollowCore(roomChanged);
        // What was asked for can no longer be done, e.g. the room was left.
        if (Confirmation == GiftBoardConfirmation.End && !Running || Confirmation == GiftBoardConfirmation.NewPeriod && !Ended)
            Confirmation = GiftBoardConfirmation.None;
    }

    internal void AskEnd() => Confirmation = GiftBoardConfirmation.End;

    /// <summary>Starts a new 期 at once when the room has none; after an ended one, asks first.</summary>
    internal Task AskNewPeriodAsync(string room)
    {
        if (!Ended) return StartNewPeriodAsync(room);
        Confirmation = GiftBoardConfirmation.NewPeriod;
        return Task.CompletedTask;
    }

    internal void CancelConfirmation() => Confirmation = GiftBoardConfirmation.None;

    internal Task ConfirmAsync(string room)
    {
        var confirmed = Confirmation;
        Confirmation = GiftBoardConfirmation.None;
        return confirmed switch
        {
            GiftBoardConfirmation.End => RunAsync(client.GiftBoard.EndPeriod, "结束这一期失败", "已结束这一期，直播画面上的榜已收起"),
            GiftBoardConfirmation.NewPeriod => StartNewPeriodAsync(room),
            _ => Task.CompletedTask,
        };
    }

    internal Task ContinueAsync() => RunAsync(client.GiftBoard.ContinuePeriod, "继续这一期失败", "已继续这一期，画面上重新显示榜");

    private Task StartNewPeriodAsync(string room) =>
        RunAsync(client.GiftBoard.StartNewPeriod, "开始新一期失败", $"已为直播间 {room} 开始新的一期");

    // Core refuses what the state no longer allows, such as a room left or switched in the meantime, and the failure says why.
    private async Task RunAsync(Action<long> action, string failed, string done)
    {
        if (roomId is not { } room) return;
        Error = null;
        try
        {
            await Write(() => action(room));
            Message = done;
        }
        catch (Exception exception) { Error = $"{failed}：{exception.Message}"; }
    }

    private Task Write(Action write)
    {
        var previous = change;
        return change = Task.Run(async () =>
        {
            // The one before reported its own failure.
            try { await previous; }
            catch { }
            write();
        });
    }

    partial void OnThresholdChanged(decimal? value) => _ = SetOptionsAsync();

    partial void OnThresholdByChanged(int value) => _ = SetOptionsAsync();

    partial void OnMascotChanged(bool value) => _ = SetOptionsAsync();

    partial void OnVolumeChanged(double value) => _ = SetOptionsAsync();

    // While the streamer's writes are on their way, Core reports options the inputs have moved past; once they are all back,
    // the inputs show Core's if they differ, and leave alone a threshold still being typed if not. Another room's options
    // are shown at once, whatever is still on its way to the room before.
    private void FollowCore(bool roomChanged = false)
    {
        if (roomChanged || optionWrites == 0 && reported != shown) ShowOptions(reported);
    }

    private void ShowOptions(GiftBoardOptions options)
    {
        showing = true;
        Threshold = options.Threshold;
        ThresholdBy = options.ThresholdBy;
        Mascot = options.Mascot;
        Volume = options.Volume;
        ThresholdError = null;
        shown = options;
        showing = false;
    }

    // The inputs are only enabled while a room is connected; a threshold that is not a whole number of 电池 stays on the
    // page with its error until it is one.
    private async Task SetOptionsAsync()
    {
        if (showing) return;
        // Not a whole number of 电池 (or none at all) is refused like zero.
        var whole = Threshold is { } value && value == decimal.Truncate(value) && value is >= 0 and <= int.MaxValue ? (int)value : 0;
        var options = new GiftBoardOptions(whole, ThresholdBy, Mascot, (int)Math.Round(Volume));
        ThresholdError = options.Error;
        if (ThresholdError is not null) return;
        if (options == shown || roomId is not { } room) return;
        shown = options;
        optionWrites++;
        try { await Write(() => client.GiftBoard.SetOptions(room, options)); }
        catch (Exception exception) { Error = $"改选项失败：{exception.Message}"; }
        finally
        {
            optionWrites--;
            FollowCore();
        }
    }
}
