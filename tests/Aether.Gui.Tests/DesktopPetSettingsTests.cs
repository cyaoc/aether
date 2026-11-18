using System.Text.RegularExpressions;
using Aether.Core;
using Aether.Core.Tests.Support;
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
    [InlineData("prankster", "有未保存的修改")]
    [InlineData("lucky", "有未保存的修改")]
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
            case "mood": GuiHarness.Tab(page, 1); Switch(page, "犯困").IsChecked = false; break;
            case "daily": GuiHarness.Tab(page, 1); Switch(page, "滑雪").IsChecked = false; break;
            case "mishap": GuiHarness.Tab(page, 1); Number(page, "出事的几率（%）").Text = "50"; break;
            case "prankster": GuiHarness.Tab(page, 2); Number(page, "它最多每几分钟自己来一次").Text = "10"; break;
            case "lucky": GuiHarness.Tab(page, 2); Number(page, "每个纸团是幸运纸团的几率").Text = "20"; break;
            case "line": GuiHarness.Tab(page, 3); Line(page, 1).Text = "欢迎{昵称}"; break;
            case "addLine": GuiHarness.Tab(page, 3); GuiHarness.Click(page, "AddLineButton"); break;
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
        GuiHarness.Tab(page, 1);
        Switch(page, "哼歌").IsChecked = false;
        Switch(page, "无聊").IsChecked = false;
        GuiHarness.Flush();
        Assert.Contains("不会无聊", Texts(page));
        Assert.False(Number(page, "无聊：几分钟后进入").IsEnabled);
        GuiHarness.Tab(page, 3);
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
        Assert.Equal((2000, 0), (saved.DesktopPetMidGift.Value, saved.DesktopPetBoredMinutes.Value));
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
    public Task The_chest_game_tab_saves_how_often_the_prankster_comes_on_its_own_and_a_paper_ball_is_lucky_and_has_no_helper_keyword() => WithPage(async (page, harness) =>
    {
        GuiHarness.Tab(page, 2);
        var lucky = Number(page, "每个纸团是幸运纸团的几率");
        Assert.Equal(("30", "10"), (Number(page, "它最多每几分钟自己来一次").Text, lucky.Text));
        Assert.Subset(Texts(page).ToHashSet(), new HashSet<string?> { "有人在看、她闲着时它自己来", "砸中谁，对面 10 秒内的礼物算双倍" });
        Assert.DoesNotContain("帮它的关键字", Texts(page));
        Switch(page, "捣蛋鬼自己来").IsChecked = false;
        Switch(page, "幸运纸团").IsChecked = false;
        GuiHarness.Flush();
        Assert.Subset(Texts(page).ToHashSet(), new HashSet<string?> { "只在你点「叫它来」时来", "不出幸运纸团" });
        Assert.False(lucky.IsEnabled);

        await GuiHarness.SaveAsync(page);
        var saved = Settings.Load(harness.DataDirectory);
        Assert.Equal((0, 0), (saved.DesktopPetPranksterMinutes.Value, saved.DesktopPetLuckyPercent.Value));
        Assert.Equal(0, harness.Client.WebService.Settings!.DesktopPetLuckyPercent.Value);
        Assert.DoesNotContain("prankster_keyword", File.ReadAllText(Settings.FilePath(harness.DataDirectory)));

        Switch(page, "幸运纸团").IsChecked = true;
        GuiHarness.Flush();
        Assert.Equal((true, "10"), (lucky.IsEnabled, lucky.Text));
    });

    [Fact]
    public Task Numbers_the_file_leaves_out_are_switched_on_at_their_defaults_and_opening_the_page_writes_nothing()
    {
        var before = "";
        return WithPage((page, harness) =>
        {
            Assert.DoesNotContain("bored_minutes", before);
            GuiHarness.Tab(page, 1);
            Assert.Equal([(true, "15"), (true, "40"), (true, "60"), (true, "25")],
                new[] { ("无聊", "无聊：几分钟后进入"), ("沮丧", "沮丧：几分钟后进入"), ("犯困", "犯困：几分钟后进入"), ("出事", "出事的几率（%）") }
                    .Select(row => (Switch(page, row.Item1).IsChecked == true, Number(page, row.Item2).Text)));
            GuiHarness.Tab(page, 2);
            Assert.Equal((true, "30"), (Switch(page, "捣蛋鬼自己来").IsChecked == true, Number(page, "它最多每几分钟自己来一次").Text));
            Assert.Equal((true, "10"), (Switch(page, "幸运纸团").IsChecked == true, Number(page, "每个纸团是幸运纸团的几率").Text));
            Assert.False(page.IsDirty);
            Assert.Equal(before, File.ReadAllText(Settings.FilePath(harness.DataDirectory)));
            return Task.CompletedTask;
        }, prepare: harness =>
        {
            // Every group is there, so reading the file adds nothing (ADR 0004); without these lines their numbers come from Core.
            var path = Settings.FilePath(harness.DataDirectory);
            before = Regex.Replace(File.ReadAllText(path), @"^ *(bored_minutes|gloomy_minutes|sleepy_minutes|prankster_minutes|mishap_percent|lucky_percent): \d+\r?\n",
                "", RegexOptions.Multiline);
            File.WriteAllText(path, before);
        });
    }

    [Fact]
    public Task A_number_the_file_holds_as_0_shows_its_switch_off_with_its_default_ready_to_turn_back_on() => WithPage(async (page, harness) =>
    {
        GuiHarness.Tab(page, 1);
        Assert.Equal((false, "40", false), (Switch(page, "沮丧").IsChecked, Number(page, "沮丧：几分钟后进入").Text, Number(page, "沮丧：几分钟后进入").IsEnabled));
        Assert.Equal((false, "25", false), (Switch(page, "出事").IsChecked, Number(page, "出事的几率（%）").Text, Number(page, "出事的几率（%）").IsEnabled));
        Assert.Subset(Texts(page).ToHashSet(), new HashSet<string?> { "不会沮丧", "不会出事", "托腮发呆，隔一阵叹口气", "抱着膝盖打瞌睡，有人来就惊醒" });
        Assert.Equal((true, true), (Switch(page, "无聊").IsChecked, Switch(page, "犯困").IsChecked));
        GuiHarness.Tab(page, 2);
        Assert.True(Switch(page, "捣蛋鬼自己来").IsChecked);
        Assert.False(page.IsDirty);

        GuiHarness.Tab(page, 1);
        Switch(page, "沮丧").IsChecked = true;
        GuiHarness.Flush();
        Assert.Contains("蹲着画圈圈，头上一片阴影", Texts(page));
        Assert.True(Number(page, "沮丧：几分钟后进入").IsEnabled);
        await GuiHarness.SaveAsync(page);
        var saved = Settings.Load(harness.DataDirectory);
        Assert.Equal((40, 0), (saved.DesktopPetGloomyMinutes.Value, saved.DesktopPetMishapPercent.Value));
    }, prepare: harness => harness.SaveSettings(("desktop_pet.gloomy_minutes", "0"), ("desktop_pet.mishap_percent", "0")));

    [Fact]
    public Task No_tab_says_that_0_turns_a_number_off_whether_it_is_on_or_off() => WithPage(page =>
    {
        foreach (var (tab, switches) in new[] { (1, new[] { "无聊", "沮丧", "犯困", "出事" }), (2, new[] { "捣蛋鬼自己来", "幸运纸团" }) })
        {
            GuiHarness.Tab(page, tab);
            foreach (var on in new[] { true, false })
            {
                foreach (var name in switches) Switch(page, name).IsChecked = on;
                GuiHarness.Flush();
                Assert.DoesNotContain(Texts(page), text => text?.Contains("填 0") == true || text?.Contains("填了 0") == true);
            }
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    public Task A_number_switched_on_must_be_positive_while_one_switched_off_is_not_checked(string typed) => WithPage(async (page, harness) =>
    {
        GuiHarness.Tab(page, 2);
        var minutes = Number(page, "它最多每几分钟自己来一次");
        minutes.Text = typed;
        GuiHarness.Flush();
        Assert.Equal("要填正整数", page.FindControl<TextBlock>("PranksterMinutesError")!.Text);
        Assert.True(page.FindControl<TextBlock>("PranksterMinutesError")!.IsVisible);
        Assert.Contains("invalid", minutes.Classes);
        Assert.False(page.CanSave);

        Switch(page, "捣蛋鬼自己来").IsChecked = false;
        GuiHarness.Flush();
        Assert.False(page.FindControl<TextBlock>("PranksterMinutesError")!.IsVisible);
        Assert.True(page.CanSave);
        await GuiHarness.SaveAsync(page);
        Assert.Equal(0, Settings.Load(harness.DataDirectory).DesktopPetPranksterMinutes.Value);
    });

    [Fact]
    public Task The_chest_game_lines_are_edited_like_hers_and_its_say_its_bubble_is_narrow_while_the_shows_tab_lists_none() => WithPage(async (page, harness) =>
    {
        Assert.DoesNotContain("小游戏·她", page.FindControl<ListBox>("ShowGroupList")!.Items.Cast<DesktopPetShowGroup>().Select(group => group.Group.Label));
        Assert.DoesNotContain("守住了", page.FindControl<ComboBox>("TestShowInput")!.Items.Cast<DesktopPetTest>().Select(test => test.Label));
        GuiHarness.Tab(page, 3);
        Assert.Subset(page.FindControl<ListBox>("LineKindList")!.Items.OfType<DesktopPetLineKind>().Select(kind => kind.Kind.Label).ToHashSet(),
            new HashSet<string> { "别丢啦", "绝杀守住", "点名", "回嘴", "露破绽", "趁现在", "收下小花花", "绝杀得手" });
        Pick(page, "LineKindList", "守住了");
        Assert.Contains("把它打跑、宝箱回家时；每次随机一句，不连着重复", Texts(page));
        Pick(page, "LineKindList", "招人");
        Assert.Contains("拉帮手时；它的气泡窄，一行最多 6 个字，空一格换行；每次随机一句，不连着重复", Texts(page));
        Line(page, 1).Text = "快发2 帮我！";
        GuiHarness.Flush();
        Assert.True(page.IsDirty);

        await GuiHarness.SaveAsync(page);
        Assert.Equal(["快发2 帮我！"], Settings.Load(harness.DataDirectory).DesktopPetChangedLines["pranksterRecruit"].Value);
    });

    [Fact]
    public Task Lines_in_a_group_that_cannot_be_edited_a_line_at_a_time_are_read_only() => WithPage((page, harness) =>
    {
        var port = Settings.Load(harness.DataDirectory).WebServicePort.Value;
        File.WriteAllText(Settings.FilePath(harness.DataDirectory), $"web_service:\n  port: {port}\ndesktop_pet: {{enabled: false}}\n");
        page.RefreshFileStatus(Settings.Load(harness.DataDirectory));
        GuiHarness.Tab(page, 3);
        Pick(page, "LineKindList", "大礼物");
        GuiHarness.Flush();
        Assert.True(GuiHarness.FindVisual<TextBlock>(page, "LinesReadOnly").IsVisible);
        Assert.False(GuiHarness.FindVisual<Button>(page, "AddLineButton").IsVisible);
        Assert.True(Line(page, 1).IsReadOnly);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_kind_the_file_holds_as_her_own_lines_is_not_marked_changed() => WithPage((page, harness) =>
    {
        Settings.Save(harness.DataDirectory, new Dictionary<string, string>
        {
            ["desktop_pet.lines.1.bigGift"] = System.Text.Json.JsonSerializer.Serialize(Character.Current.Lines["bigGift"])
        });
        page.RefreshFileStatus(Settings.Load(harness.DataDirectory));
        GuiHarness.Tab(page, 3);
        GuiHarness.Flush();
        Assert.DoesNotContain("已改", Texts(page));
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_daily_actions_least_seconds_may_equal_the_most_but_not_exceed_them() => WithPage(page =>
    {
        GuiHarness.Tab(page, 1);
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
        GuiHarness.Tab(page, 3);
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

        using var pet = await DesktopPetEvents.OpenAsync(harness.Client, TestContext.Current.CancellationToken);
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
        var trial = (await pet.NextAsync(DesktopPet.Test)).GetProperty("id").GetInt32();
        await pet.ReportAsync($"?trial={trial}");
        await GuiHarness.WaitForAsync(() => page.FindControl<TextBlock>("TestText")!.Text == "试演");
        Assert.Equal("", note.Text);
    });

    [Fact]
    public Task Summon_shows_what_core_says_and_calls_the_prankster_at_once_without_saving() => WithPage(async (page, harness) =>
    {
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["desktop_pet.enabled"] = "true" });
        await ((MainWindowViewModel)page.DataContext!).WebService.ApplyWebSettingsAsync(Settings.Load(harness.DataDirectory));
        GuiHarness.Tab(page, 2);
        var button = page.FindControl<Button>("SummonButton")!;
        var (text, note) = (page.FindControl<TextBlock>("SummonText")!, page.FindControl<TextBlock>("SummonNote")!);
        await GuiHarness.WaitForAsync(() => note.Text == "先在直播姬里加好桌宠页");
        Assert.Equal((false, "叫它来"), (button.IsEnabled, text.Text));
        using var pet = await DesktopPetEvents.OpenAsync(harness.Client, TestContext.Current.CancellationToken);
        await GuiHarness.WaitForAsync(() => note.Text == "连上直播间才能叫它来：没人送礼，也没人能帮它");
        Assert.False(button.IsEnabled);

        harness.Client.WebService.SetDesktopPetRoom(true);
        await GuiHarness.WaitForAsync(() => button.IsEnabled);
        Assert.Equal(("叫它来", "点了它马上来，不是试演，送的礼物都是真的；不用保存"), (text.Text, note.Text));
        GuiHarness.Click(page, "SummonButton");
        await pet.NextAsync(DesktopPet.Summon);
        await GuiHarness.WaitForAsync(() => text.Text == "它在路上…");
        Assert.Equal((false, "她正在演的这段完了它就来", true), (button.IsEnabled, note.Text, note.Classes.Contains("accent")));
        Assert.False(page.IsDirty);

        await pet.ReportAsync("?visit=start&run=0");
        await GuiHarness.WaitForAsync(() => text.Text == "它在场上");
        Assert.Equal((false, "它走了才能再叫", false), (button.IsEnabled, note.Text, note.Classes.Contains("accent")));
    });

    [Fact]
    public Task A_new_show_in_the_show_table_brings_its_switch_kind_of_lines_and_test() => WithPage(page =>
    {
        Pick(page, "ShowGroupList", "PK");
        Assert.True(Switch(page, "PK 赢了").IsEnabled);
        Assert.Contains("PK 结束时赢了", Texts(page));
        Assert.Contains("PK 赢了", page.FindControl<ComboBox>("TestShowInput")!.Items.Cast<DesktopPetTest>().Select(test => test.Label));
        GuiHarness.Tab(page, 3);
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

    /// <summary>The page at 1200 × 720, built from <paramref name="shows"/>, or the program's 演出表, over data that
    /// <paramref name="prepare"/> may change first.</summary>
    private static Task WithPage(Func<DesktopPetSettingsPage, GuiHarness, Task> test, ShowTable? shows = null,
        Action<GuiHarness>? prepare = null) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        harness.SaveSettings();
        prepare?.Invoke(harness);
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
