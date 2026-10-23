using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class LotteryViewModel
{
    private string? loadedEntryId;
    private bool loadingEntry;
    [ObservableProperty] private string? keyword;
    [ObservableProperty] private string? entryLotteryError;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowEndEntry))] private bool confirmingEnd;
    public EditableRows<EntryAwardRow> Awards { get; } = new(1);

    public bool EntryPreparing => LotteryState.Lottery is EntryLottery { Phase: EntryLotteryPhase.Preparing };
    public bool EntryOpen => LotteryState.Lottery is EntryLottery { Phase: EntryLotteryPhase.Open };
    public bool EntryClosed => LotteryState.Lottery is EntryLottery { Phase: EntryLotteryPhase.Closed };
    public bool EntryEnded => LotteryState.Lottery is EntryLottery { Phase: EntryLotteryPhase.Ended };
    public bool CanEditEntry => EntryPreparing && !Busy;
    public bool CanAddAward => CanEditEntry && Awards.Count < LotteryController.MaxAwards;
    public bool CanStartEntry => EntryPreparing && CanBeginOperation && KeywordError is null && Awards.All(row => row.Error is null);
    public bool CanCloseEntries => EntryOpen && CanControlOperation;
    public bool CanEndEntry => EntryClosed && CanControlOperation && LotteryState.Lottery is EntryLottery { IsDrawing: false };
    public bool CanToggleHidden => EntryEnded && CanControlOperation;
    public bool ShowEndEntry => EntryClosed && !ConfirmingEnd;
    public string? KeywordError => EntryPreparing ? LotteryController.ValidateKeyword(Keyword) : null;
    public string KeywordHint => EntryPreparing ? "不能靠送礼物报名" : "开始报名后，关键字和奖项不能再改";

    partial void OnKeywordChanged(string? value) => RefreshEntryInputs();

    private void RefreshEntryForm()
    {
        if (LotteryState.Lottery is not EntryLottery lottery) loadedEntryId = null;
        else if (loadedEntryId != lottery.Id)
        {
            loadingEntry = true;
            loadedEntryId = lottery.Id;
            Keyword = lottery.Keyword;
            Awards.Clear();
            foreach (var award in lottery.Awards)
                Awards.Add(new(award, isNew: string.IsNullOrWhiteSpace(award.Prize)));
            EntryLotteryError = null;
            loadingEntry = false;
        }
        RefreshEntryInputs();
    }

    private void RefreshEntryInputs()
    {
        if (loadingEntry) return;
        ConfirmingEnd &= EntryClosed;
        var lottery = LotteryState.Lottery as EntryLottery;
        foreach (var row in Awards)
        {
            var value = row.Value;
            row.Error = EntryPreparing ? LotteryController.ValidateAward(value) : null;
            row.NameInvalid = row.VisibleError is not null && string.IsNullOrWhiteSpace(value.Name);
            row.PrizeInvalid = row.VisibleError is not null && string.IsNullOrWhiteSpace(value.Prize);
            row.CapacityInvalid = row.VisibleError is not null && value.Capacity <= 0;
            row.Preparing = EntryPreparing;
            row.CanEdit = CanEditEntry;
            var drawn = lottery?.Draws.Count(draw => draw.AwardIndex == row.Number - 1) ?? 0;
            row.Full = drawn >= value.Capacity;
            row.DrawCount = $"已抽 {drawn} / {value.Capacity}";
            row.DrawText = row.Full ? "抽完了" : EntryOpen ? "截止后抽" : "抽一个";
            row.DrawAccessibleName = row.Full ? $"{value.Name}已抽完" : $"抽一位{value.Name}";
            row.CanDraw = CanEndEntry && !row.Full && lottery!.Entries.Length > lottery.Draws.Length;
            row.DrawHint = lottery is not null && lottery.Entries.Length <= lottery.Draws.Length ? "没有还没中奖的报名观众可抽" : null;
        }
        foreach (var property in new[] { nameof(EntryPreparing), nameof(EntryOpen), nameof(EntryClosed), nameof(EntryEnded),
            nameof(CanEditEntry), nameof(CanAddAward), nameof(CanStartEntry), nameof(CanCloseEntries), nameof(CanEndEntry),
            nameof(CanToggleHidden), nameof(ShowEndEntry), nameof(KeywordError), nameof(KeywordHint) })
            OnPropertyChanged(property);
    }

    public void AddAward()
    {
        if (CanAddAward)
            Awards.AddNew(new(new("", "", 1, Awards.Count == 0 ? Grade.Grand : Awards.Count == 1 ? Grade.Second : Grade.Third), isNew: true));
    }

    internal Task StartEntryAsync()
    {
        if (!CanStartEntry) return Task.CompletedTask;
        var awards = Awards.Select(row => row.Value).ToArray();
        var keyword = Keyword ?? "";
        return ChangeEntryLotteryAsync(lottery => lottery.StartEntry(awards, keyword));
    }

    internal Task CloseEntriesAsync() => CanCloseEntries ? ChangeEntryLotteryAsync(lottery => lottery.CloseEntries()) : Task.CompletedTask;

    internal void RequestEndEntry()
    {
        if (CanEndEntry) ConfirmingEnd = true;
    }

    internal void CancelEndEntry() => ConfirmingEnd = false;

    internal Task ConfirmEndEntryAsync()
    {
        if (!CanEndEntry) return Task.CompletedTask;
        ConfirmingEnd = false;
        return ChangeEntryLotteryAsync(lottery => lottery.EndEntry());
    }

    internal Task ToggleEntryHiddenAsync()
    {
        if (!CanToggleHidden || LotteryState.Lottery is not EntryLottery entry) return Task.CompletedTask;
        return ChangeEntryLotteryAsync(lottery => lottery.SetEntryHidden(!entry.Hidden));
    }

    public Task DrawEntryAsync(int number)
    {
        var index = number - 1;
        if (index < 0 || index >= Awards.Count || !Awards[index].CanDraw) return Task.CompletedTask;
        return ChangeEntryLotteryAsync(lottery => lottery.DrawEntry(index));
    }

    private async Task ChangeEntryLotteryAsync(Action<LotteryController> change)
    {
        if (Busy) return;
        EntryLotteryError = null;
        try { await ChangeLotteryAsync(change); }
        catch (Exception error) { EntryLotteryError = error.Message; }
    }
}
