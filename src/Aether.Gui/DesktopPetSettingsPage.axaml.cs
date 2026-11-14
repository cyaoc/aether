using System.ComponentModel;
using System.Globalization;
using Aether.Core;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aether.Gui;

/// <summary>The 桌宠 page (#343, canvas DP1): her switch, the 桌宠页's connection, a 试演, and which 演出 play, her 心情 and
/// 日常小动作 and her lines, all saved to desktop_pet. Groups, switches, kinds of lines and 试演 come from the 演出表, so a new
/// 演出 needs no change here.</summary>
public partial class DesktopPetSettingsPage : SettingsPage
{
    private readonly ShowTable shows;
    private readonly DesktopPetShowRow[] showRows;
    private readonly OrderedInputs thresholds, dailySeconds;
    private string? blocked;
    // What the next 报名抽奖 starts with, as Core last said: the 帮它的关键字 must not be it. Until Core says, it matches nothing typed.
    private string entryKeyword = "";
    private bool testing;
    private string playing = "";

    public DesktopPetSettingsPage() : this(DataDirectory.Locate()) { }

    public DesktopPetSettingsPage(string dataDirectory) : this(dataDirectory, ShowTable.Current) { }

    /// <param name="shows">The 演出表 the page is built from.</param>
    internal DesktopPetSettingsPage(string dataDirectory, ShowTable shows) : base(dataDirectory)
    {
        this.shows = shows;
        InitializeComponent();
        lineTarget = new(LineRows, RefreshInsertTarget);
        Title = "桌宠";
        TimingHint = "保存后立即生效；什么都不改，就是设计好的样子";
        showRows = [.. shows.Shows.Where(show => show.CanTurnOff).Select(show => new DesktopPetShowRow(show))];
        thresholds = new([MidGiftInput, BigGiftInput], ThresholdError, Settings.NumberOrder.Rising,
            new("门槛要填正整数", "中礼物门槛要小于大礼物门槛", "中礼物门槛要小于大礼物门槛，改好后才能保存"));
        dailySeconds = new([DailyMinInput, DailyMaxInput], DailyError, Settings.NumberOrder.NotFalling,
            new("间隔要填正整数", "最短间隔不能比最长间隔长", "最短间隔不能比最长间隔长，改好后才能保存"));
        BindInputs();
        BuildLists();
        WatchNotes();
        Tabs.SelectionChanged += (_, _) => ShowTab();
        TestNicknameInput.TextChanged += (_, _) => RefreshTest();
        LoadSettings();
    }

    protected override bool AppliesToWebService => true;
    protected override string DirtyDescription => blocked ?? base.DirtyDescription;
    internal override IEnumerable<string> FieldNames => base.FieldNames.Append(Settings.DesktopPetLinesName);

    private void BindInputs()
    {
        Bind(EnabledInput, EnabledReadOnly, null, s => s.DesktopPetEnabled);
        Bind(MidGiftInput, MidGiftReadOnly, null, s => s.DesktopPetMidGift);
        Bind(BigGiftInput, BigGiftReadOnly, null, s => s.DesktopPetBigGift);
        Bind(BoredInput, BoredReadOnly, BoredError, s => s.DesktopPetBoredMinutes);
        Bind(GloomyInput, GloomyReadOnly, GloomyError, s => s.DesktopPetGloomyMinutes);
        Bind(SleepyInput, SleepyReadOnly, SleepyError, s => s.DesktopPetSleepyMinutes);
        Bind(DailyMinInput, DailyMinReadOnly, null, s => s.DesktopPetDailyMinSeconds);
        Bind(DailyMaxInput, DailyMaxReadOnly, null, s => s.DesktopPetDailyMaxSeconds);
        Bind(MishapInput, MishapReadOnly, MishapError, s => s.DesktopPetMishapPercent);
        Bind(PranksterMinutesInput, PranksterMinutesReadOnly, PranksterMinutesError, s => s.DesktopPetPranksterMinutes);
        Bind(PranksterKeywordInput, PranksterKeywordReadOnly, null, s => s.DesktopPetPranksterKeyword);
    }

    /// <summary>The 演出 groups with their switches, the 日常小动作' switches, the kinds of lines and the 试演, from the 演出表.</summary>
    private void BuildLists()
    {
        ShowGroupList.ItemsSource = shows.Groups.Where(group => group.Note is not null).Select(group =>
            new DesktopPetShowGroup(group, [.. showRows.Where(row => row.Show.Group == group.Id)], group.Id == ShowGroup.Gift)).ToArray();
        ShowGroupList.SelectedIndex = 0;
        DailyRows.ItemsSource = showRows.Where(row => row.Show.Group == ShowGroup.Daily).ToArray();
        foreach (var row in showRows)
            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DesktopPetShowRow.On)) Validate(); };
        TestShowInput.ItemsSource = shows.Shows.SelectMany(show => show.Tests.Select(variant => new DesktopPetTest(show.Id, variant,
            variant is null ? show.Label : $"{show.Label}：{shows.Variants[variant]}"))).ToArray();
        TestShowInput.SelectedIndex = 0;
        BuildLineKinds();
    }

    /// <summary>When a 演出 plays follows the thresholds as they are typed; a 心情 or mishap chance of 0 says it never comes.</summary>
    private void WatchNotes()
    {
        OnText(MidGiftInput, RefreshShowNotes);
        OnText(BigGiftInput, RefreshShowNotes);
        foreach (var (input, note, usual, never) in new (NumericUpDown, TextBlock, string, string)[]
        {
            (BoredInput, BoredNote, "托腮发呆，隔一阵叹口气", "填了 0：不会无聊"),
            (GloomyInput, GloomyNote, "蹲着画圈圈，头上一片阴影", "填了 0：不会沮丧"),
            (SleepyInput, SleepyNote, "抱着膝盖打瞌睡，有人来就惊醒", "填了 0：不会犯困"),
            (MishapInput, MishapNote, "砸到脚、破音、摔一跤；填 0 就不出事", "填了 0：不会出事"),
            (PranksterMinutesInput, PranksterMinutesNote, "有人在看、她闲着时它自己来；填 0 就只在你叫它时来", "填了 0：它只在你叫它时来")
        })
            OnText(input, () =>
            {
                var zero = Number(input) == 0;
                note.Text = zero ? never : usual;
                note.Classes.Set("accent", zero);
            });
    }

    private static void OnText(NumericUpDown input, Action changed) =>
        input.PropertyChanged += (_, e) => { if (e.Property == NumericUpDown.TextProperty) changed(); };

    private static int? Number(NumericUpDown input) => int.TryParse(input.Text, out var value) ? value : null;

    private void RefreshShowNotes()
    {
        string Battery(int? value) => (value ?? 0).ToString(CultureInfo.InvariantCulture);
        var (mid, big) = (Number(MidGiftInput), Number(BigGiftInput));
        foreach (var row in showRows)
            row.Note = row.Show.Note?.Replace("{mid}", Battery(mid)).Replace("{big-1}", Battery(big - 1)).Replace("{big}", Battery(big)) ?? "";
    }

    private void ShowTab()
    {
        ShowsTab.IsVisible = Tabs.SelectedIndex == 0;
        MoodsTab.IsVisible = Tabs.SelectedIndex == 1;
        GameTab.IsVisible = Tabs.SelectedIndex == 2;
        LinesTab.IsVisible = Tabs.SelectedIndex == 3;
    }

    protected override void LoadRows()
    {
        var off = settings.DesktopPetShowsOff;
        foreach (var row in showRows)
        {
            row.On = !off.Value.Contains(row.Show.Id);
            row.CanEdit = off.CanEdit;
        }
        ShowsReadOnly.IsVisible = DailyReadOnly.IsVisible = !off.CanEdit;
        LoadLines();
        RefreshTest();
    }

    protected override IEnumerable<Field> RowFields() =>
    [
        new(settings.DesktopPetShowsOff.Name, settings.DesktopPetShowsOff.CanEdit,
            Settings.TextListText(showRows.Where(row => !row.On).Select(row => row.Show.Id)))
    ];

    protected override bool ShowRowErrors()
    {
        var thresholdError = thresholds.Check();
        var dailyError = dailySeconds.Check();
        var keywordError = CheckPranksterKeyword();
        var linesError = CheckLines();
        blocked = thresholdError ?? dailyError ?? keywordError ?? linesError;
        DirtyBlocked = blocked is not null;
        return blocked is not null;
    }

    /// <summary>Marks a 帮它的关键字 Core says cannot be saved, with why under it; why the page cannot be saved, or null.</summary>
    private string? CheckPranksterKeyword()
    {
        var why = settings.DesktopPetPranksterKeyword.CanEdit ? settings.ValidatePranksterKeyword(PranksterKeywordInput.Text ?? "", entryKeyword) : null;
        PranksterKeywordError.Text = why ?? "";
        PranksterKeywordError.IsVisible = why is not null;
        return why is null ? null : "小游戏里帮它的关键字要先改好，才能保存";
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) ReadEntryKeywordAsync().RaiseFailure();
    }

    /// <summary>Asks Core each time the page shows, so never under the splash, before the window reads the lottery at all; a
    /// 报名抽奖 started on its own page may have changed the keyword.</summary>
    // shortcut: a room connected while the page shows is checked against the keyword it had until it shows again; ask Core on
    // each lottery change if that is ever seen.
    private async Task ReadEntryKeywordAsync()
    {
        if (viewModel is null || !IsVisible) return;
        entryKeyword = await viewModel.Lottery.GetNextEntryKeywordAsync() ?? "";
        Validate();
    }

    protected override bool RowsChanged => EditedLines().Count > 0;

    protected override Task<string> WriteSettings(Dictionary<string, string> changes)
    {
        Settings.SaveDesktopPet(dataDirectory, changes, EditedLines(), settings);
        return Task.FromResult("已保存。");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.WebService.PropertyChanged -= OnWebServiceChanged;
            viewModel.WebService.TestShowEnded -= OnTestShowEnded;
        }
        base.OnDataContextChanged(e);
        if (viewModel is not null)
        {
            viewModel.WebService.PropertyChanged += OnWebServiceChanged;
            viewModel.WebService.TestShowEnded += OnTestShowEnded;
        }
        RefreshTest();
        RefreshSummon();
        ReadEntryKeywordAsync().RaiseFailure();
    }

    private void OnWebServiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WebServiceViewModel.DesktopPet)) RefreshTest();
        if (e.PropertyName == nameof(WebServiceViewModel.Summon)) RefreshSummon();
    }

    private void OnTestShowEnded()
    {
        testing = false;
        RefreshTest();
    }

    /// <summary>A 试演 plays until the 桌宠页 says it is over, it is stopped, or no page could play it.</summary>
    private void RefreshTest()
    {
        var on = settings.Error is null && settings.DesktopPetEnabled.Value;
        var connected = viewModel?.WebService.DesktopPet.Connected == true;
        testing &= on && connected;
        PageStatus.IsVisible = on;
        OffNotice.IsVisible = !on;
        TestShowInput.IsEnabled = TestNicknameInput.IsEnabled = on && connected;
        TestButton.IsEnabled = on && connected && (testing || !string.IsNullOrWhiteSpace(TestNicknameInput.Text));
        PlayIcon.IsVisible = !testing;
        StopIcon.IsVisible = testing;
        TestText.Text = testing ? "停止" : "试演";
        AutomationProperties.SetName(TestButton, testing ? "停止试演" : "试演选中的这一段");
        TestNote.Text = !on ? "先打开桌宠" : !connected ? "先在直播姬里加好桌宠页" : testing ? $"正在直播画面上演：{playing}" : "";
        TestNote.Classes.Set("accent", testing);
    }

    private void TestOrStop(object? sender, RoutedEventArgs e)
    {
        if (testing) viewModel?.WebService.StopTestShow();
        else if (TestShowInput.SelectedItem is DesktopPetTest test)
        {
            viewModel?.WebService.TestShow(test.Show, test.Variant, TestNicknameInput.Text ?? "");
            playing = test.Label;
        }
        testing = !testing;
        RefreshTest();
    }

    /// <summary>叫它来 as Core says it is: why it cannot, else where the 捣蛋鬼 is.</summary>
    private void RefreshSummon()
    {
        var summon = viewModel?.WebService.Summon;
        SummonButton.IsEnabled = summon?.CanSummon == true;
        SummonText.Text = summon?.Visit switch { PranksterVisit.OnStage => "它在场上", PranksterVisit.OnTheWay => "它在路上…", _ => "叫它来" };
        SummonNote.Text = summon?.Blocked ?? summon?.Visit switch
        {
            PranksterVisit.OnStage => "它走了才能再叫",
            PranksterVisit.OnTheWay => "她正在演的这段完了它就来",
            _ => "点了它马上来，不是试演，观众送的礼物都算；不用保存"
        };
        SummonNote.Classes.Set("accent", summon is { Blocked: null, Visit: PranksterVisit.OnTheWay });
    }

    private void Summon(object? sender, RoutedEventArgs e) => viewModel?.WebService.SummonPrankster();
}
