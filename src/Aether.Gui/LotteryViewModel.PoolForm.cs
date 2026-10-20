using System.Globalization;
using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class LotteryViewModel
{
    private string? loadedPoolId;
    private long? loadedPoolRoom;
    private bool loadingPool;
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string? uid = "";
    [ObservableProperty] private string? stopWord = LotteryController.DefaultStopWord;
    [ObservableProperty] private string? poolLotteryError;
    public EditableRows<PoolPrizeRow> Prizes { get; } = new(1) { new(LotteryController.DefaultPrize, isNew: true) };

    private bool CanBeginOperation => !Busy && LotteryState.Connected;
    private bool CanControlOperation => !Busy && LotteryState.CanControl;
    public bool PoolSpinning => LotteryState.Lottery is PoolLottery { Phase: PoolLotteryPhase.Spinning };
    public bool PoolDrawing => LotteryState.Lottery is PoolLottery { IsDrawing: true };
    public bool PoolIdle => LotteryState.Lottery is not EntryLottery && !PoolSpinning && !PoolDrawing;
    public bool CanEditPool => PoolIdle && !Busy;
    public bool CanAddPrize => CanEditPool && Prizes.Count < LotteryController.MaxPrizes;
    public bool CanStart => PoolIdle && CanBeginOperation && Prizes.All(row => row.Error is null) && UidError is null && StopWordError is null;
    public bool CanStop => PoolSpinning && CanControlOperation;
    public bool CanCancel => CanStop;
    public bool CanNew => CanBeginOperation && (LotteryState.Lottery is EntryLottery entry
        ? entry.Phase is EntryLotteryPhase.Preparing or EntryLotteryPhase.Ended : PoolIdle);
    public string? UidError => LotteryController.ValidateUid(Uid);
    public string? StopWordError => LotteryController.ValidateStopWord(StopWord);
    private long? ParsedUid => Viewer.TryParseUid(Uid, out var parsed) ? parsed : null;
    private string? PoolViewer => ParsedUid is { } uid ? (LotteryState.Lottery as PoolLottery)?.Viewers.GetValueOrDefault(uid) : null;
    public bool PoolViewerKnown => PoolViewer is not null;
    public string PoolViewerHint => UidError is not null ? "" : ParsedUid is null ? "不填就只能主播点“停”"
        : PoolViewer is { } viewer ? $"{viewer} · 这场发过弹幕" : "这场还没发过弹幕，喊停后显示昵称";
    public string PoolStatusText
    {
        get
        {
            var lottery = LotteryState.Lottery as PoolLottery;
            var word = PoolSpinning || PoolDrawing ? lottery!.StopWord : StopWord?.Trim();
            var caller = (PoolSpinning || PoolDrawing ? lottery!.Uid : ParsedUid) is { } uid
                ? lottery?.Viewers.GetValueOrDefault(uid) ?? $"UID {uid}" : null;
            return lottery?.Phase switch
            {
                PoolLotteryPhase.Spinning => caller is null ? "点“停”开奖" : $"正在等 {caller} 发「{word}」，也可以直接点“停”",
                _ when PoolDrawing => "直播画面正在播开奖特效",
                _ when !LotteryState.Connected => "先连直播间，才能开始抽奖",
                _ => caller is null ? "没填 UID，开始后要主播点“停”" : $"点“开始”，直播画面上的奖品转起来；只认 {caller} 发的「{word}」",
            };
        }
    }

    partial void OnLotteryStateChanged(LotteryState value) => RefreshPoolForm();
    partial void OnBusyChanged(bool value) => RefreshPoolInputs();
    partial void OnUidChanged(string? value) => RefreshPoolInputs();
    partial void OnStopWordChanged(string? value) => RefreshPoolInputs();

    private void RefreshPoolForm()
    {
        if (LotteryState.Lottery is not EntryLottery)
        {
            var lottery = LotteryState.Lottery as PoolLottery;
            if (loadedPoolId != lottery?.Id || loadedPoolRoom is not null && loadedPoolRoom != LotteryState.RoomId)
            {
                loadingPool = true;
                loadedPoolId = lottery?.Id;
                Prizes.Clear();
                if (lottery is not null)
                    foreach (var prize in lottery.Prizes) Prizes.Add(new(prize));
                else Prizes.Add(new(LotteryController.DefaultPrize, isNew: true));
                Uid = lottery?.Uid?.ToString(CultureInfo.InvariantCulture) ?? "";
                StopWord = lottery?.StopWord ?? LotteryController.DefaultStopWord;
                loadingPool = false;
            }
            loadedPoolRoom = LotteryState.RoomId;
        }
        RefreshPoolInputs();
    }

    private void RefreshPoolInputs()
    {
        if (loadingPool) return;
        var total = Prizes.Sum(row => (long)Math.Max(0, row.Value.Weight));
        foreach (var row in Prizes)
        {
            var value = row.Value;
            row.Error = LotteryController.ValidatePrize(value);
            row.NameInvalid = row.VisibleError is not null && string.IsNullOrWhiteSpace(value.Name);
            row.WeightInvalid = row.VisibleError is not null && value.Weight <= 0;
            row.CanEdit = CanEditPool;
            row.Percentage = total > 0 && value.Weight > 0 ? $"{100d * value.Weight / total:0.#}%" : "—";
        }
        foreach (var property in new[] { nameof(PoolSpinning), nameof(PoolDrawing), nameof(PoolIdle), nameof(CanEditPool),
            nameof(CanAddPrize), nameof(CanStart), nameof(CanStop), nameof(CanCancel), nameof(CanNew), nameof(UidError),
            nameof(StopWordError), nameof(PoolViewerKnown), nameof(PoolViewerHint), nameof(PoolStatusText) })
            OnPropertyChanged(property);
    }

    public void AddPrize()
    {
        if (CanAddPrize) Prizes.AddNew(new(new("", 1, Grade.Third), isNew: true));
    }

    internal Task StartPoolAsync()
    {
        if (!CanStart) return Task.CompletedTask;
        var prizes = Prizes.Select(row => row.Value).ToArray();
        var word = StopWord ?? "";
        var uid = ParsedUid;
        return ChangeLotteryAsync(lottery => lottery.Start(prizes, word, uid));
    }

    internal Task StopPoolAsync() => CanStop ? ChangeLotteryAsync(lottery => lottery.Stop()) : Task.CompletedTask;
    internal Task CancelPoolAsync() => CanCancel ? ChangeLotteryAsync(lottery => lottery.Cancel()) : Task.CompletedTask;
    internal Task NewLotteryAsync(LotteryMode mode) => CanNew ? ChangeLotteryAsync(lottery => lottery.New(mode)) : Task.CompletedTask;
}
