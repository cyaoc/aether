using System.Collections.ObjectModel;
using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Gui;

/// <summary>One lottery mode's 载入方案 menu and 存为方案 draft, as on canvas U1–U4.</summary>
public sealed partial class PrizeSetPickerViewModel : ObservableObject
{
    private readonly LotteryViewModel lottery;
    private readonly LotteryMode mode;
    private bool chosenSaved;
    private IReadOnlyList<LotteryPrizeSet>? entriesSource;
    private string? entriesChosen;
    private bool entriesCanEdit;
    [ObservableProperty] private string? draftName;

    internal PrizeSetPickerViewModel(LotteryViewModel lottery, LotteryMode mode)
    {
        this.lottery = lottery;
        this.mode = mode;
        RefreshEntries();
        lottery.PropertyChanged += (_, _) =>
        {
            RefreshEntries();
            OnPropertyChanged("");
        };
    }

    internal string? Chosen { get; private set; }
    internal string? Status => Chosen is null ? null : chosenSaved
        ? $"已存为「{Chosen}」，下次新开一场可以直接载入" : $"已载入「{Chosen}」，改了不会动到方案，要更新就再存一次";
    public bool CanEdit => lottery.CanEdit(mode);
    private string? Name => DraftName?.Trim();
    public bool CanSave => CanEdit && !string.IsNullOrEmpty(Name);
    public bool ReplacesSet => lottery.PrizeSets.Any(set => set.Mode == mode && set.Name == Name);
    public string SaveText => ReplacesSet ? "替换" : "保存";
    public string SaveHint => string.IsNullOrEmpty(Name) ? "先起个名字"
        : ReplacesSet ? "已有同名方案，保存会替换它"
        : mode == LotteryMode.Pool ? $"存下这 {lottery.Prizes.Count} 件奖品，名称、占比和档次"
        : $"存下这 {lottery.Awards.Count} 个奖项，名称、奖品、名额和档次";

    // A shown MenuFlyout's presenter misses ItemsSource swaps, so the entries change in place.
    public ObservableCollection<PrizeSetMenuEntry> Entries { get; } = [];

    partial void OnDraftNameChanged(string? value) => OnPropertyChanged("");

    private void RefreshEntries()
    {
        if (ReferenceEquals(entriesSource, lottery.PrizeSets) && entriesChosen == Chosen && entriesCanEdit == CanEdit) return;
        entriesSource = lottery.PrizeSets;
        entriesChosen = Chosen;
        entriesCanEdit = CanEdit;
        Entries.Clear();
        foreach (var set in lottery.PrizeSets.Where(set => set.Mode == mode))
            Entries.Add(new PrizeSetRow(set.Name,
                mode == LotteryMode.Pool ? $"{set.Prizes.Length} 件奖品" : $"{set.Awards.Length} 个奖项",
                mode == LotteryMode.Pool ? string.Join("、", set.Prizes.Select(prize => prize.Name))
                    : string.Join("、", set.Awards.Select(award => $"{award.Prize} ×{award.Capacity}")),
                new AsyncRelayCommand(() => lottery.DeletePrizeSetAsync(set)),
                CanEdit, new RelayCommand(() => lottery.LoadPrizeSet(set)), set.Name == Chosen));
        Entries.Add(new PrizeSetMenuFooter(Entries.Count == 0 ? "还没有方案，填好后点“存为方案”" : null,
            mode == LotteryMode.Pool ? "点一个方案，替换上面的奖品；换完还能接着改" : "点一个方案，替换上面的奖项；换完还能接着改"));
    }

    internal void Choose(string? name, bool saved = false) => (Chosen, chosenSaved) = (name, saved);

    internal void StartDraft() => DraftName = Chosen;

    internal Task RefreshAsync() => lottery.RefreshPrizeSetsAsync();

    internal async Task<bool> SaveAsync() => CanSave && await lottery.SavePrizeSetAsync(mode, Name!);
}
