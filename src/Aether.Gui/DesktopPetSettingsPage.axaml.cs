using System.ComponentModel;
using System.Globalization;
using Aether.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aether.Gui;

/// <summary>The 桌宠 page (#343, canvas DP1): her switch, the 桌宠页's connection, a 试演, and which 演出 play, her 心情 and
/// 日常小动作 and her lines, all saved to desktop_pet. Groups, switches, kinds of lines and 试演 come from the 演出表, so a new
/// 演出 needs no change here.</summary>
public partial class DesktopPetSettingsPage : SettingsPage
{
    // The group whose 演出 the 中礼物 and 大礼物 thresholds divide, and the 日常小动作', switched on the 心情和日常 tab.
    private const string GiftGroup = "gift", DailyGroup = "daily";
    private readonly ShowTable shows;
    private readonly DesktopPetShowRow[] showRows;
    private readonly ThresholdInputs thresholds;
    private string? blocked;
    private bool testing;
    private string playing = "";
    public event Action? GeneralSettingsRequested;

    public DesktopPetSettingsPage() : this(DataDirectory.Locate()) { }

    public DesktopPetSettingsPage(string dataDirectory) : this(dataDirectory, ShowTable.Current) { }

    /// <param name="shows">The 演出表 the page is built from.</param>
    internal DesktopPetSettingsPage(string dataDirectory, ShowTable shows) : base(dataDirectory)
    {
        this.shows = shows;
        InitializeComponent();
        Title = "桌宠";
        TimingHint = "保存后立即生效；什么都不改，就是设计好的样子";
        showRows = [.. shows.Shows.Where(show => show.CanTurnOff).Select(show => new DesktopPetShowRow(show))];
        thresholds = new([MidGiftInput, BigGiftInput], ThresholdError, "中礼物门槛要小于大礼物门槛", "中礼物门槛要小于大礼物门槛，改好后才能保存");
        BindInputs();
        BuildLists();
        WatchNotes();
        Tabs.SelectionChanged += (_, _) => ShowTab();
        TestNicknameInput.TextChanged += (_, _) => RefreshTest();
        PageStatus.GeneralSettingsRequested += () => GeneralSettingsRequested?.Invoke();
        LoadSettings();
    }

    protected override bool AppliesToWebService => true;
    protected override string DirtyDescription => blocked ?? base.DirtyDescription;
    internal override IEnumerable<string> FieldNames => base.FieldNames.Append("desktop_pet.lines");

    private void BindInputs()
    {
        Bind(EnabledInput, EnabledReadOnly, null, s => s.DesktopPetEnabled);
        Bind(MidGiftInput, MidGiftReadOnly, null, s => s.DesktopPetGiftThresholds[0]);
        Bind(BigGiftInput, BigGiftReadOnly, null, s => s.DesktopPetGiftThresholds[1]);
        Bind(BoredInput, BoredReadOnly, BoredError, s => s.DesktopPetMoodMinutes[0]);
        Bind(GloomyInput, GloomyReadOnly, GloomyError, s => s.DesktopPetMoodMinutes[1]);
        Bind(SleepyInput, SleepyReadOnly, SleepyError, s => s.DesktopPetMoodMinutes[2]);
        Bind(DailyMinInput, DailyMinReadOnly, DailyMinError, s => s.DesktopPetDailySeconds[0]);
        Bind(DailyMaxInput, DailyMaxReadOnly, DailyMaxError, s => s.DesktopPetDailySeconds[1]);
        Bind(MishapInput, MishapReadOnly, MishapError, s => s.DesktopPetMishapPercent);
    }

    /// <summary>The 演出 groups with their switches, the 日常小动作' switches, the kinds of lines and the 试演, from the 演出表.</summary>
    private void BuildLists()
    {
        ShowGroupList.ItemsSource = shows.Groups.Where(group => group.Note is not null).Select(group =>
            new DesktopPetShowGroup(group, [.. showRows.Where(row => row.Show.Group == group.Id)], group.Id == GiftGroup)).ToArray();
        ShowGroupList.SelectedIndex = 0;
        DailyRows.ItemsSource = showRows.Where(row => row.Show.Group == DailyGroup).ToArray();
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
            (MishapInput, MishapNote, "砸到脚、破音、摔一跤；填 0 就不出事", "填了 0：不会出事")
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
        LinesTab.IsVisible = Tabs.SelectedIndex == 2;
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
        var linesError = CheckLines();
        blocked = thresholdError ?? linesError;
        DirtyBlocked = blocked is not null;
        return blocked is not null;
    }

    protected override bool RowsChanged => EditedLines().Count > 0;

    protected override Task<string> WriteSettings(Dictionary<string, string> changes)
    {
        Settings.SaveDesktopPet(dataDirectory, changes, EditedLines(), settings);
        return Task.FromResult("已保存。");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (viewModel is not null) viewModel.WebService.PropertyChanged -= OnWebServiceChanged;
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.WebService.PropertyChanged += OnWebServiceChanged;
        RefreshTest();
    }

    private void OnWebServiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WebServiceViewModel.DesktopPet)) RefreshTest();
    }

    /// <summary>The 桌宠页 never says when a 试演 ends, so one plays until stopped, or until no page could play it.</summary>
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
}
