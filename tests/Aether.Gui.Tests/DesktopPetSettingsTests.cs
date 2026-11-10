using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using static Aether.Gui.Tests.Support.DesktopPetForm;

namespace Aether.Gui.Tests;

public sealed class DesktopPetSettingsTests
{
    [Theory]
    [InlineData("desktop_pet.enabled", "true")]
    [InlineData("desktop_pet.shows_off", "[entry]")]
    [InlineData("desktop_pet.big_gift_spend", "20000")]
    [InlineData("desktop_pet.sleepy_minutes", "0")]
    [InlineData("desktop_pet.daily_max_seconds", "60")]
    [InlineData("desktop_pet.mishap_percent", "0")]
    [InlineData("desktop_pet.lines.1.bigGift", """["谢谢{昵称}"]""")]
    public Task A_desktop_pet_setting_changed_in_the_file_reaches_the_web_service(string name, string value) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        harness.SaveSettings();
        var viewModel = harness.CreateViewModel();
        try
        {
            await viewModel.WebService.RefreshWebServiceAsync(Settings.Load(harness.DataDirectory));
            Settings.Save(harness.DataDirectory, new Dictionary<string, string> { [name] = value });
            var edited = Settings.Load(harness.DataDirectory);
            await viewModel.WebService.RefreshWebServiceAsync(edited);
            Assert.Same(edited, harness.Client.WebService.Settings);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("switch", "有未保存的修改")]
    [InlineData("show", "有未保存的修改")]
    [InlineData("threshold", "有未保存的修改")]
    [InlineData("mood", "有未保存的修改")]
    [InlineData("daily", "有未保存的修改")]
    [InlineData("mishap", "有未保存的修改")]
    [InlineData("line", "有未保存的修改")]
    [InlineData("addLine", "进场的台词有错，改好后才能保存")]
    public Task Any_edit_shows_unsaved_changes(string edit, string dirty) => WithPage(page =>
    {
        Assert.False(page.IsDirty);
        switch (edit)
        {
            case "switch": page.FindControl<ToggleSwitch>("EnabledInput")!.IsChecked = true; break;
            case "show": Switch(page, "进场").IsChecked = false; break;
            case "threshold": Pick(page, "ShowGroupList", "礼物"); Number(page, "大礼物门槛（电池）").Text = "20000"; break;
            case "mood": Tab(page, 1); Number(page, "犯困：几分钟后进入").Text = "0"; break;
            case "daily": Tab(page, 1); Switch(page, "滑雪").IsChecked = false; break;
            case "mishap": Tab(page, 1); Number(page, "出事的几率（%）").Text = "50"; break;
            case "line": Tab(page, 2); Line(page, 1).Text = "欢迎{昵称}"; break;
            case "addLine": Tab(page, 2); GuiHarness.Click(page, "AddLineButton"); break;
        }
        GuiHarness.Flush();
        Assert.True(GuiHarness.FindVisual<TextBlock>(page, "DirtyMessage").IsEffectivelyVisible);
        Assert.Equal(dirty, page.DirtyText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Saving_writes_desktop_pet_keeps_only_the_changed_kinds_of_lines_and_tells_the_page() => WithPage(async (page, harness) =>
    {
        Switch(page, "进场").IsChecked = false;
        Pick(page, "ShowGroupList", "礼物");
        Number(page, "中礼物门槛（电池）").Text = "2000";
        Number(page, "大礼物门槛（电池）").Text = "1500";
        GuiHarness.Flush();
        Assert.False(page.CanSave);
        Assert.Equal("中礼物门槛要小于大礼物门槛，改好后才能保存", page.DirtyText);
        Number(page, "大礼物门槛（电池）").Text = "10000";
        GuiHarness.Flush();
        Assert.Contains("这一轮 2000 – 9999 电池", Texts(page));
        Tab(page, 1);
        Switch(page, "哼歌").IsChecked = false;
        Number(page, "无聊：几分钟后进入").Text = "0";
        GuiHarness.Flush();
        Assert.Contains("填了 0：不会无聊", Texts(page));
        Tab(page, 2);
        Pick(page, "LineKindList", "大礼物");
        Line(page, 1).Text = "{昵称}，谢谢你～";
        Pick(page, "LineKindList", "普通礼物");
        // What leaving the page and coming back does: the draft stays.
        page.RefreshFileStatus(Settings.Load(harness.DataDirectory));
        Pick(page, "LineKindList", "大礼物");
        Assert.Equal("{昵称}，谢谢你～", Line(page, 1).Text);

        await GuiHarness.SaveAsync(page);
        var saved = Settings.Load(harness.DataDirectory);
        Assert.Equal(["entry", "hum"], saved.DesktopPetShowsOff.Value);
        Assert.Equal((2000, 0), (saved.DesktopPetGiftThresholds[0].Value, saved.DesktopPetMoodMinutes[0].Value));
        Assert.Equal(["bigGift"], saved.DesktopPetChangedLines.Keys);
        Assert.Equal(["{昵称}，谢谢你～", .. Character.Current.Lines["bigGift"].Skip(1)], saved.DesktopPetChangedLines["bigGift"].Value);
        Assert.Equal(["entry", "hum"], harness.Client.WebService.Settings!.DesktopPetShowsOff.Value);
        Assert.Contains("已改", Texts(page));

        GuiHarness.Click(page, "RestoreButton");
        Assert.True(page.IsDirty);
        await GuiHarness.SaveAsync(page);
        Assert.Empty(Settings.Load(harness.DataDirectory).DesktopPetChangedLines);
        Assert.DoesNotContain("lines.1.", File.ReadAllText(Settings.FilePath(harness.DataDirectory)));
        Assert.DoesNotContain("已改", Texts(page));
    });

    [Fact]
    public Task The_daily_actions_least_seconds_may_equal_the_most_but_not_exceed_them() => WithPage(page =>
    {
        Tab(page, 1);
        Number(page, "日常最短间隔（秒）").Text = "41";
        GuiHarness.Flush();
        Assert.False(page.CanSave);
        Assert.Equal("最短间隔不能比最长间隔长，改好后才能保存", page.DirtyText);
        Assert.Contains("最短间隔不能比最长间隔长", Texts(page));
        Assert.Contains("invalid", Number(page, "日常最长间隔（秒）").Classes);

        Number(page, "日常最短间隔（秒）").Text = "40";
        GuiHarness.Flush();
        Assert.True(page.CanSave);
        Assert.DoesNotContain("invalid", Number(page, "日常最长间隔（秒）").Classes);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Insert_offers_only_the_kinds_placeholders_and_puts_one_in_the_line_being_edited() => WithPage(page =>
    {
        Tab(page, 2);
        Pick(page, "LineKindList", "冷场后来了人");
        Assert.Contains("这一类没有可插入的内容，台词照写就行", Texts(page));
        Pick(page, "LineKindList", "大礼物");
        var chips = page.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible && button.Classes.Contains("chip")).ToArray();
        Assert.Equal(["昵称", "礼物", "个数"], chips.Select(chip => chip.Content));
        Line(page, 2).Focus();
        Line(page, 2).CaretIndex = 0;
        GuiHarness.Flush();
        Assert.Contains("到第 2 句", Texts(page));
        chips[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        GuiHarness.Flush();
        Assert.Equal("{礼物}" + Character.Current.Lines["bigGift"][1], Line(page, 2).Text);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_test_show_plays_on_the_desktop_pet_page_until_stopped_or_over_and_says_why_it_cannot_play() => WithPage(async (page, harness) =>
    {
        await ((MainWindowViewModel)page.DataContext!).WebService.ApplyWebSettingsAsync(Settings.Load(harness.DataDirectory));
        var button = page.FindControl<Button>("TestButton")!;
        var note = page.FindControl<TextBlock>("TestNote")!;
        GuiHarness.Flush();
        Assert.Equal((false, "先打开桌宠"), (button.IsEnabled, note.Text));
        Assert.True(page.FindControl<Border>("OffNotice")!.IsVisible);
        page.FindControl<ToggleSwitch>("EnabledInput")!.IsChecked = true;
        await GuiHarness.SaveAsync(page);
        Assert.Equal((false, "先在直播姬里加好桌宠页"), (button.IsEnabled, note.Text));
        Assert.Contains("桌宠页：还没有推流软件连着", Texts(page));

        using var pet = await DesktopPetPageConnection.OpenAsync(harness);
        await GuiHarness.WaitForAsync(() => button.IsEnabled);
        PickTest(page, "盲盒·大：出大奖");
        GuiHarness.Click(page, "TestButton");
        var played = await pet.NextAsync(DesktopPet.Test);
        Assert.Equal(("blindBoxLarge", "Jackpot", "柚子不酸"),
            (played.GetProperty("show").GetString(), played.GetProperty("variant").GetString(), played.GetProperty("nickname").GetString()));
        Assert.Equal(("停止", "正在直播画面上演：盲盒·大：出大奖"), (page.FindControl<TextBlock>("TestText")!.Text, note.Text));
        GuiHarness.Click(page, "TestButton");
        await pet.NextAsync(DesktopPet.StopTest);
        Assert.Equal(("试演", ""), (page.FindControl<TextBlock>("TestText")!.Text, note.Text));

        GuiHarness.Click(page, "TestButton");
        await pet.NextAsync(DesktopPet.Test);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(harness.Client.WebService.Status.Origin) };
        using var over = await http.PostAsync("desktop-pet/report", null);
        await GuiHarness.WaitForAsync(() => page.FindControl<TextBlock>("TestText")!.Text == "试演");
        Assert.Equal("", note.Text);
    });

    [Fact]
    public Task A_new_show_in_the_show_table_brings_its_switch_kind_of_lines_and_test() => WithPage(page =>
    {
        Pick(page, "ShowGroupList", "PK");
        Assert.True(Switch(page, "PK 赢了").IsEnabled);
        Assert.Contains("PK 结束时赢了", Texts(page));
        Assert.Contains("PK 赢了", page.FindControl<ComboBox>("TestShowInput")!.Items.Cast<DesktopPetTest>().Select(test => test.Label));
        Tab(page, 2);
        var kinds = page.FindControl<ListBox>("LineKindList")!;
        kinds.ScrollIntoView(kinds.Items.Count - 1);
        GuiHarness.Flush();
        Assert.Subset(Texts(page).ToHashSet(), new HashSet<string?> { "PK", "PK 赢了" });
        return Task.CompletedTask;
    }, ShowTable.Current with
    {
        Groups = [.. ShowTable.Current.Groups, new("pk", "PK", "和别的直播间 PK 时")],
        Shows = [.. ShowTable.Current.Shows, new("pkWin", "pk", "PK 赢了", "B", ["pkWin"], [null], "PK 结束时赢了")],
        Lines = [.. ShowTable.Current.Lines, new("pkWin", "pk", "PK 赢了", "PK 结束时赢了", ["昵称"])]
    });

    private static Task WithPage(Func<DesktopPetSettingsPage, Task> test, ShowTable? shows = null) =>
        WithPage((page, _) => test(page), shows);

    /// <summary>The page at 1200 × 720, built from <paramref name="shows"/>, or the program's 演出表.</summary>
    private static Task WithPage(Func<DesktopPetSettingsPage, GuiHarness, Task> test, ShowTable? shows = null) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        harness.SaveSettings();
        var viewModel = harness.CreateViewModel();
        var page = new DesktopPetSettingsPage(harness.DataDirectory, shows ?? ShowTable.Current) { DataContext = viewModel };
        var window = new Window { Content = page, Width = 1200, Height = 720 };
        window.Show();
        try
        {
            GuiHarness.Flush();
            await test(page, harness);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
