using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Net;
using System.Net.Sockets;
using Avalonia.Input.Platform;
using System.Net.Http.Json;
using System.Text.Json;

namespace Aether.Gui.Tests;

public sealed class WelcomeAnimationSettingsTests
{
    [Fact]
    public Task History_keeps_one_hundred_finished_items_and_clears_only_for_a_new_room_connection() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var requested = harness.HoldRoomRequest();
        var viewModel = harness.CreateViewModel();
        try
        {
            viewModel.RoomNumber = "6";
            viewModel.ConnectCommand.Execute(null);
            await requested.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.Apply(new WelcomeAnimationPlayback("pending", 42, "a.mp4", false, PlaybackState.Queued));
            for (var i = 0; i < 102; i++)
                viewModel.Apply(new WelcomeAnimationPlayback(i.ToString(), 42, "a.mp4", false, PlaybackState.Completed));
            Assert.Equal(101, viewModel.RecentAnimations.Count);
            Assert.Contains(viewModel.RecentAnimations, item => item.Playback.Id == "pending");
            Assert.DoesNotContain(viewModel.RecentAnimations, item => item.Playback.Id is "0" or "1");
            viewModel.Apply(new Reconnecting());
            Assert.Equal(101, viewModel.RecentAnimations.Count);
            await viewModel.ReconnectCommand.ExecuteAsync(null);
            Assert.Equal(101, viewModel.RecentAnimations.Count);
            viewModel.ConnectCommand.Cancel();
            await viewModel.ConnectCommand.ExecutionTask!;
            Assert.Equal(101, viewModel.RecentAnimations.Count);
            viewModel.ConnectCommand.Execute(null);
            Assert.Empty(viewModel.RecentAnimations);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("没有推流软件连着", true)]
    [InlineData("排队超过 30 秒", true)]
    [InlineData(WelcomeAnimation.RecentlyPlayed, false)]
    public Task Skipped_playback_keeps_its_specific_reason(string reason, bool unplayed) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        viewModel.Apply(new WelcomeAnimationPlayback("skip", 42, "a.mp4", false, PlaybackState.Skipped, reason, "小鹿"));
        var item = viewModel.RecentAnimations.Single();
        Assert.Equal("没播：" + reason, item.Status);
        Assert.Equal(("小鹿", "UID 42"), (item.Name, item.Detail));
        Assert.Equal(unplayed, item.Unplayed);
        Assert.Equal(!unplayed, item.RecentlyPlayed);
        await viewModel.ShutdownAsync();
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Cancelling_or_selecting_an_unsupported_file_does_not_add_a_viewer() => HeadlessApp.Session.Dispatch(() =>
    {
        using var harness = new GuiHarness();
        var choices = new Queue<string?>([null, Video(harness, "source", "not-video.txt")]);
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation, () => Task.FromResult(choices.Dequeue()));
        Click(page, "AddAnimationButton");
        Assert.Empty(Rows(page));
        Click(page, "AddAnimationButton");
        Assert.Empty(Rows(page));
        Assert.Contains(".mp4", page.FindControl<TextBlock>("Message")!.Text);
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Replacing_own_filename_keeps_the_name_and_rolls_back_on_later_copy_failure(bool fail) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var owned = Video(harness, "videos", "video.mp4", "old video");
        var source = Video(harness, "source", "video.mp4", "new video");
        var missing = Video(harness, "source", "missing.webm");
        Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 42, "video.mp4");
        var selected = new Queue<string>([source, missing]);
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation, () => Task.FromResult<string?>(selected.Dequeue()));
        var window = new Window { Content = page };
        window.Show();
        try
        {
            Click(Rows(page)[0], "FileButton");
            if (fail) { Add(page, "43"); File.Delete(missing); }
            Click(page, "SaveButton");
            await GuiHarness.WaitForAsync(() => page.IsEnabled);
            Assert.Equal("video.mp4", Settings.Load(harness.DataDirectory).WelcomeAnimationVideos[42].Value);
            Assert.Equal(fail ? "old video" : "new video", File.ReadAllText(owned));
            Assert.Equal("new video", File.ReadAllText(source));
            Assert.Single(Directory.GetFiles(Path.Combine(harness.DataDirectory, "videos")));
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("", "填一个 UID")]
    [InlineData("0", "UID 必须是正整数")]
    [InlineData("-1", "UID 必须是正整数")]
    [InlineData("abc", "UID 必须是正整数")]
    [InlineData("9223372036854775808", "UID 必须是正整数")]
    [InlineData("00042", "和上面那条重复了")]
    public Task Invalid_uids_show_inline_hints_and_cannot_be_saved(string uid, string hint) => HeadlessApp.Session.Dispatch(() =>
    {
        using var harness = new GuiHarness();
        var source = Video(harness, "source", "video.mp4");
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation, () => Task.FromResult<string?>(source));
        var window = new Window { Content = page };
        window.Show();
        try
        {
            Add(page, "42");
            Click(page, "AddAnimationButton");
            var row = Rows(page)[1];
            var input = row.FindControl<TextBox>("UidInput")!;
            Assert.True(input.IsFocused);
            Assert.Equal("保存后才能试播", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.DoesNotContain("error", row.FindControl<Grid>("RowNote")!.Classes);
            Assert.DoesNotContain("invalid", input.Classes);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            input.Text = uid;
            GuiHarness.Flush();
            Assert.Equal("保存后才能试播", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            Assert.False(row.CanTest);
            Rows(page)[0].FocusInput();
            GuiHarness.Flush();
            Assert.Contains(hint, row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.Contains("error", row.FindControl<Grid>("RowNote")!.Classes);
            Assert.Contains("invalid", input.Classes);
            input.Focus();
            input.Text = "43";
            GuiHarness.Flush();
            Assert.Equal("保存后才能试播", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.DoesNotContain("error", row.FindControl<Grid>("RowNote")!.Classes);
            Assert.DoesNotContain("invalid", input.Classes);
            Assert.True(page.FindControl<Button>("SaveButton")!.IsEnabled);
            input.Text = uid;
            GuiHarness.Flush();
            Assert.Contains(hint, row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.Contains("invalid", input.Classes);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            Assert.False(row.CanTest);
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Existing_animation_uid_validates_without_waiting_for_blur() => HeadlessApp.Session.Dispatch(() =>
    {
        using var harness = new GuiHarness();
        Video(harness, "videos", "video.mp4");
        Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 42, "video.mp4");
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation);
        var window = new Window { Content = page };
        window.Show();
        try
        {
            var row = Assert.Single(Rows(page));
            var input = row.FindControl<TextBox>("UidInput")!;
            input.Focus();
            input.Text = "";
            GuiHarness.Flush();
            Assert.Equal("填一个 UID，或删掉这条", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.Contains("error", row.FindControl<Grid>("RowNote")!.Classes);
            Assert.Contains("invalid", input.Classes);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            Assert.False(row.CanTest);
            input.Text = "43";
            GuiHarness.Flush();
            Assert.Equal("保存后才能试播", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.DoesNotContain("error", row.FindControl<Grid>("RowNote")!.Classes);
            Assert.DoesNotContain("invalid", input.Classes);
            Assert.True(page.FindControl<Button>("SaveButton")!.IsEnabled);
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Test_play_is_gated_by_saved_video_connection_file_and_other_playback() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var path = Video(harness, "videos", "video.mp4");
        Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 42, "video.mp4");
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["welcome_animation.enabled"] = "true" });
        var viewModel = harness.CreateViewModel();
        await viewModel.ApplyWebSettingsAsync(Settings.Load(harness.DataDirectory));
        var replacement = Video(harness, "source", "video.mp4");
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation, () => Task.FromResult<string?>(replacement)) { DataContext = viewModel };
        var window = new Window { Content = page };
        window.Show();
        try
        {
            var row = Rows(page).Single();
            var button = row.FindControl<Button>("TestButton")!;
            Assert.False(button.IsEnabled);
            viewModel.Apply(new WebServiceStatus(true, 17890, 1, 0));
            Assert.True(button.IsEnabled);
            viewModel.Apply(new WelcomeAnimationPlayback("other", 43, "other.mp4", false, PlaybackState.Queued));
            Assert.False(button.IsEnabled);
            viewModel.Apply(new WelcomeAnimationPlayback("other", 43, "other.mp4", false, PlaybackState.Completed));
            Assert.True(button.IsEnabled);
            viewModel.Apply(new WelcomeAnimationPlayback("one", 42, "video.mp4", true, PlaybackState.Playing));
            Assert.Equal("播放中", row.FindControl<TextBlock>("TestText")!.Text);
            Assert.Contains("正在直播姬里播放", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.False(button.IsEnabled);
            viewModel.Apply(new WelcomeAnimationPlayback("one", 42, "video.mp4", true, PlaybackState.NoReport));
            Assert.DoesNotContain("上次播放失败", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.True(button.IsEnabled);
            viewModel.Apply(new WelcomeAnimationPlayback("failure", 42, "video.mp4", true, PlaybackState.Failed));
            Assert.Contains("上次播放失败", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.Contains("H.264", row.FindControl<TextBlock>("RowMessage")!.Text);
            viewModel.Apply(new WelcomeAnimationPlayback("success", 42, "video.mp4", true, PlaybackState.Playing));
            Assert.Equal("播放中", row.FindControl<TextBlock>("TestText")!.Text);
            Assert.Contains("上次播放失败", row.FindControl<TextBlock>("RowMessage")!.Text);
            viewModel.Apply(new WelcomeAnimationPlayback("success", 42, "video.mp4", true, PlaybackState.Completed));
            Assert.Contains("上次播放失败", row.FindControl<TextBlock>("RowMessage")!.Text);
            File.Delete(path);
            viewModel.Apply(new WebServiceStatus(true, 17890, 2, 0));
            Assert.False(button.IsEnabled);
            Assert.Contains("找不到视频文件", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.NotNull(row.FindControl<TextBlock>("FileNameText")!.TextDecorations);
            Click(row, "FileButton");
            Assert.False(button.IsEnabled);
            Assert.Contains("保存后才能试播", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.DoesNotContain("上次播放失败", row.FindControl<TextBlock>("RowMessage")!.Text);
            await Save(page);
            Assert.DoesNotContain("上次播放失败", Rows(page).Single().FindControl<TextBlock>("RowMessage")!.Text);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("welcome_animation:\n  enabled: |-\n    true\nweb_service:\n  port: |-\n    17891\nwelcome_animation_videos: {42: video.mp4}\n")]
    [InlineData("welcome_animation: {enabled: true}\nweb_service: {port: 17891}\nwelcome_animation_videos:\n  &viewer 42: video.mp4\nother: *viewer\n")]
    public Task Noneditable_settings_keep_controls_read_only_and_explain_where_to_edit(string yaml) => HeadlessApp.Session.Dispatch(() =>
    {
        using var harness = new GuiHarness();
        Directory.CreateDirectory(harness.DataDirectory);
        File.WriteAllText(Settings.FilePath(harness.DataDirectory), yaml);
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation);
        Assert.False(page.FindControl<ToggleSwitch>("AnimationInput")!.IsEnabled);
        Assert.True(page.FindControl<NumericUpDown>("WebPortInput")!.IsReadOnly);
        Assert.False(page.FindControl<NumericUpDown>("WebPortInput")!.AllowSpin);
        Assert.True(Rows(page).Single().FindControl<TextBox>("UidInput")!.IsReadOnly);
        Assert.False(Rows(page).Single().FindControl<Button>("FileButton")!.IsEnabled);
        Assert.Contains("设置文件", Rows(page).Single().FindControl<TextBlock>("RowMessage")!.Text);
        Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task A_failed_copy_keeps_the_mapping_and_rolls_back_new_copies() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var first = Video(harness, "source", "first.mp4");
        var second = Video(harness, "source", "second.webm");
        var selected = new Queue<string>([first, second]);
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation, () => Task.FromResult<string?>(selected.Dequeue()));
        var window = new Window { Content = page };
        window.Show();
        try
        {
            Add(page, "42");
            Add(page, "43");
            var before = File.ReadAllText(Settings.FilePath(harness.DataDirectory));
            File.Delete(second);
            Click(page, "SaveButton");
            await GuiHarness.WaitForAsync(() => page.IsEnabled);
            Assert.Contains("second.webm", page.FindControl<TextBlock>("Message")!.Text);
            Assert.Equal(before, File.ReadAllText(Settings.FilePath(harness.DataDirectory)));
            Assert.Empty(Directory.GetFiles(Path.Combine(harness.DataDirectory, "videos")));
            Assert.Equal("video", File.ReadAllText(first));
            Assert.Equal(2, Rows(page).Length);
            Assert.True(page.FindControl<TextBlock>("DirtyMessage")!.IsVisible);
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Imports_suffix_collisions_and_remove_only_unreferenced_copies() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var first = Video(harness, "first", "欢迎.mp4", "first video");
        var second = Video(harness, "second", "欢迎.mp4", "second video");
        var replacement = Video(harness, "third", "欢迎.mp4", "replacement video");
        var selected = new Queue<string>([first, second, replacement]);
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation, () => Task.FromResult<string?>(selected.Dequeue()));
        var window = new Window { Content = page };
        window.Show();
        try
        {
            Add(page, "42");
            Add(page, "43");
            await Save(page);
            var saved = Settings.Load(harness.DataDirectory);
            Assert.Equal("欢迎.mp4", saved.WelcomeAnimationVideos[42].Value);
            Assert.Equal("欢迎 (2).mp4", saved.WelcomeAnimationVideos[43].Value);
            Click(Rows(page)[0], "FileButton");
            await Save(page);
            saved = Settings.Load(harness.DataDirectory);
            Assert.Equal("欢迎.mp4", saved.WelcomeAnimationVideos[42].Value);
            Assert.Equal("replacement video", File.ReadAllText(Copy(harness, "欢迎.mp4")));
            Assert.Equal("second video", File.ReadAllText(Copy(harness, "欢迎 (2).mp4")));
            // A mapping added in the settings file after this draft was opened must keep its shared video.
            Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 44, "欢迎.mp4");
            Click(Rows(page)[0], "RemoveButton");
            await Save(page);
            Assert.Equal("replacement video", File.ReadAllText(Copy(harness, "欢迎.mp4")));
            Assert.Equal("欢迎.mp4", Settings.Load(harness.DataDirectory).WelcomeAnimationVideos[44].Value);
            Click(Rows(page).Single(row => row.Uid == 43), "RemoveButton");
            await Save(page);
            Assert.False(File.Exists(Copy(harness, "欢迎 (2).mp4")));
            Click(Rows(page).Single(), "RemoveButton");
            await Save(page);
            Assert.False(File.Exists(Copy(harness, "欢迎.mp4")));
            Assert.Empty(Settings.Load(harness.DataDirectory).WelcomeAnimationVideos);
            Assert.Equal("first video", File.ReadAllText(first));
            Assert.Equal("second video", File.ReadAllText(second));
            Assert.Equal("replacement video", File.ReadAllText(replacement));
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    private static string Video(GuiHarness harness, string folder, string name, string content = "video")
    {
        var path = Path.Combine(harness.DataDirectory, folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Copy(GuiHarness harness, string name) => Path.Combine(harness.DataDirectory, "videos", name);
    private static WelcomeAnimationRow[] Rows(SettingsPage page) => page.FindControl<StackPanel>("AnimationRows")!.Children.Cast<WelcomeAnimationRow>().ToArray();
    private static void Click(Control control, string name) { control.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); GuiHarness.Flush(); }
    private static void Add(SettingsPage page, string uid)
    {
        Click(page, "AddAnimationButton");
        Rows(page)[^1].FindControl<TextBox>("UidInput")!.Text = uid;
        GuiHarness.Flush();
    }
    private static async Task Save(SettingsPage page)
    {
        GuiHarness.Flush();
        Assert.True(page.FindControl<Button>("SaveButton")!.IsEnabled);
        Click(page, "SaveButton");
        await GuiHarness.WaitForAsync(() => page.IsEnabled);
        Assert.Equal("已保存。", page.FindControl<TextBlock>("Message")!.Text);
    }

    [Theory]
    [InlineData("Video", "/video")]
    [InlineData("Lottery", "/lottery")]
    public Task Copy_address_reports_success_for_two_seconds(string pageName, string path) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.General) { DataContext = viewModel };
        var window = new Window { Content = page };
        window.Show();
        try
        {
            viewModel.Apply(new WebServiceStatus(true, 17891, 0, 0));
            GuiHarness.Flush();
            var button = page.FindControl<Button>("Copy" + pageName + "AddressButton")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var label = ((StackPanel)button.Content!).Children.OfType<TextBlock>().Single();
            await GuiHarness.WaitForAsync(() => label.Text == "已复制");
            Assert.Equal("http://localhost:17891" + path, await window.Clipboard!.TryGetTextAsync());
            await GuiHarness.WaitForAsync(() => label.Text == "复制");
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(PlaybackState.Queued, null, "排队中 · 第 1 个", true)]
    [InlineData(PlaybackState.Playing, null, "播放中", true)]
    [InlineData(PlaybackState.Completed, null, "已播放", false)]
    [InlineData(PlaybackState.Failed, "视频无法播放", "播放失败：视频无法播放", false)]
    [InlineData(PlaybackState.NoReport, "推流软件已全部断开", "没收到播放回报：推流软件已全部断开", false)]
    [InlineData(PlaybackState.Skipped, "10 分钟内播过", "没播：10 分钟内播过", false)]
    public Task Recent_playback_shows_progress_reason_and_test_marker(PlaybackState state, string? reason, string expected, bool busy) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation) { DataContext = viewModel };
        var window = new Window { Content = page };
        window.Show();
        try
        {
            viewModel.Apply(new WelcomeAnimationPlayback("one", 42, "欢迎.mp4", true, PlaybackState.Queued));
            viewModel.Apply(new WelcomeAnimationPlayback("one", 42, "欢迎.mp4", true, state, reason));
            GuiHarness.Flush();
            var list = page.FindControl<ListBox>("RecentAnimationsList")!;
            Assert.Equal(1, list.ItemCount);
            Assert.Contains(expected, GuiHarness.VisibleText(window));
            Assert.Contains("试播", GuiHarness.VisibleText(window));
            Assert.Equal(busy, viewModel.AnimationBusy);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Recent_playback_header_reads_nickname_and_uid_as_one_line_on_the_time_baseline() => HeadlessApp.Session.Dispatch(async () =>
    {
        const string nickname = "小鹿";
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation) { DataContext = viewModel };
        var window = new Window { Content = page };
        window.Show();
        try
        {
            viewModel.Apply(new WelcomeAnimationPlayback("one", 42, "欢迎.mp4", false, PlaybackState.Completed, Nickname: nickname));
            GuiHarness.Flush();
            var list = page.FindControl<ListBox>("RecentAnimationsList")!;
            var text = list.ContainerFromIndex(0)!.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible).ToArray();
            var header = Assert.Single(text, x => new TextBlockAutomationPeer(x).GetName() == $"{nickname} UID 42");
            var time = text.Single(x => x.Text == viewModel.RecentAnimations.Single().OccurredAt.ToString("HH:mm:ss"));
            Assert.Single(header.TextLayout.TextLines);
            GuiHarness.AssertSameBaseline(header, time, window);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Queued_playbacks_are_numbered_in_arrival_order_and_move_up() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        try
        {
            foreach (var id in new[] { "a", "b", "c" })
                viewModel.Apply(new WelcomeAnimationPlayback(id, 42, "欢迎.mp4", false, PlaybackState.Queued, Nickname: id));
            string Status(string id) => viewModel.RecentAnimations.Single(item => item.Playback.Id == id).Status;
            Assert.Equal(["排队中 · 第 1 个", "排队中 · 第 2 个", "排队中 · 第 3 个"], new[] { Status("a"), Status("b"), Status("c") });
            viewModel.Apply(new WelcomeAnimationPlayback("a", 42, "欢迎.mp4", false, PlaybackState.Playing, Nickname: "a"));
            Assert.Equal(["播放中", "排队中 · 第 1 个", "排队中 · 第 2 个"], new[] { Status("a"), Status("b"), Status("c") });
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Web_service_runs_without_login_or_enabled_features_and_restarts_only_for_a_saved_port() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        harness.LoginResponse = (_, token) => new TaskCompletionSource<HttpResponseMessage>().Task.WaitAsync(token);
        var port = FreePort();
        Video(harness, "videos", "trial.mp4", "trial video");
        Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 42, "trial.mp4");
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["web_service.port"] = port.ToString() });
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        window.Show();
        try
        {
            await GuiHarness.WaitForAsync(() => viewModel.IsLoginPage && viewModel.WebServiceStatus.Running);
            Assert.Contains("<video", await http.GetStringAsync(viewModel.VideoAddressText));
            Assert.Contains("/lottery/events", await http.GetStringAsync(viewModel.LotteryAddressText));
            window.FindControl<Button>("OpenSettingsWithoutLoginButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await GuiHarness.WaitForAsync(() => !viewModel.IsLoginPage);
            Assert.Equal(5, window.Navigation.SelectedIndex);
            var general = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            Assert.Equal("端口保存后立即生效；日志设置重启后生效", general.FindControl<TextBlock>("TimingHint")!.Text);
            using var response = await http.GetAsync(viewModel.VideoAddressText + "/events", HttpCompletionOption.ResponseHeadersRead);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            await GuiHarness.WaitForAsync(() => viewModel.HasVideoConnections);
            window.Navigation.SelectedIndex = 3;
            var animation = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            Assert.False(animation.FindControl<NumericUpDown>("WebPortInput")!.IsEffectivelyVisible);
            foreach (var enabled in new[] { true, false, true })
            {
                animation.FindControl<ToggleSwitch>("AnimationInput")!.IsChecked = enabled;
                await Save(animation);
                Assert.True(viewModel.WebServiceStatus.Running);
                Assert.Equal(1, viewModel.WebServiceStatus.VideoConnections);
                Assert.Equal(enabled, Rows(animation).Single().CanTest);
            }
            Click(Rows(animation).Single(), "TestButton");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string? line;
            do { line = await reader.ReadLineAsync(deadline.Token); } while (line is not null && !line.StartsWith("data: "));
            Assert.NotNull(line);
            using var payload = JsonDocument.Parse(line[6..]);
            Assert.False(payload.RootElement.TryGetProperty("uid", out _));
            var id = payload.RootElement.GetProperty("id").GetString();
            Assert.Equal("trial video", await http.GetStringAsync(viewModel.VideoAddressText + "/files/" + id));
            using var report = await http.PostAsJsonAsync(viewModel.VideoAddressText + "/report", new { id, success = true });
            await GuiHarness.WaitForAsync(() => viewModel.RecentAnimations.Single().Playback.State == PlaybackState.Completed);
            Click(animation, "OpenGeneralSettingsButton");
            Assert.Equal(5, window.Navigation.SelectedIndex);
            var nextPort = FreePort();
            general.FindControl<NumericUpDown>("WebPortInput")!.Text = nextPort.ToString();
            GuiHarness.Flush();
            Assert.Equal(port, viewModel.WebServiceStatus.Port);
            Assert.Equal("保存后网页服务换到新端口，直播姬里的地址要跟着改", general.FindControl<TextBlock>("DirtyMessage")!.Text);
            Click(general, "SaveButton");
            await GuiHarness.WaitForAsync(() => general.IsEnabled);
            Assert.Equal("已保存，网页服务已换到新端口", general.FindControl<TextBlock>("Message")!.Text);
            Assert.Equal(nextPort, viewModel.WebServiceStatus.Port);
            Assert.Equal(0, viewModel.WebServiceStatus.VideoConnections);
            Assert.Contains("<video", await http.GetStringAsync(viewModel.VideoAddressText));
            await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync($"http://localhost:{port}/video", TestContext.Current.CancellationToken));
            var externalPort = FreePort();
            Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["web_service.port"] = externalPort.ToString() });
            window.Navigation.SelectedIndex = 0;
            await GuiHarness.WaitForAsync(() => viewModel.WebServiceStatus.Port == externalPort && viewModel.WebServiceStatus.Running);
            Assert.Contains("<video", await http.GetStringAsync(viewModel.VideoAddressText));
            Assert.False(viewModel.HasRoomConnection);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Fact]
    public Task Invalid_external_settings_preserve_the_running_service_and_playback_status() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        Video(harness, "videos", "video.mp4");
        Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 42, "video.mp4");
        Settings.Save(harness.DataDirectory, new Dictionary<string, string>
        {
            ["welcome_animation.enabled"] = "true", ["web_service.port"] = FreePort().ToString()
        });
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        window.Show();
        try
        {
            await GuiHarness.WaitForAsync(() => viewModel.WebServiceStatus.Running);
            using var response = await http.GetAsync(viewModel.VideoAddressText + "/events", HttpCompletionOption.ResponseHeadersRead);
            await GuiHarness.WaitForAsync(() => viewModel.HasVideoConnections);
            viewModel.TestPlay(42, "video.mp4");
            await GuiHarness.WaitForAsync(() => viewModel.RecentAnimations.Any(item => item.Playing));
            var before = viewModel.WebServiceStatus;
            File.WriteAllText(Settings.FilePath(harness.DataDirectory), "web_service:\n  port: invalid\n");
            window.Navigation.SelectedIndex = 3;
            GuiHarness.Flush();
            Assert.Equal(before, viewModel.WebServiceStatus);
            Assert.True(viewModel.AnimationBusy);
            Assert.True(viewModel.AnimationPlaying(42, "video.mp4"));
            Assert.Contains("<video", await http.GetStringAsync(viewModel.VideoAddressText));
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            Assert.Contains("web_service.port", page.FindControl<TextBlock>("FileMessage")!.Text);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Navigation_applies_external_animation_mappings_before_test_play_without_restarting_unchanged_settings(bool draft) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        Video(harness, "videos", "old.mp4", "old video");
        Video(harness, "videos", "new.mp4", "new video");
        Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 42, "old.mp4");
        Settings.Save(harness.DataDirectory, new Dictionary<string, string>
        {
            ["welcome_animation.enabled"] = "true", ["web_service.port"] = FreePort().ToString()
        }, Settings.Load(harness.DataDirectory));
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        window.Show();
        try
        {
            await GuiHarness.WaitForAsync(() => viewModel.WebServiceStatus.Running);
            using var original = await http.GetAsync(viewModel.VideoAddressText + "/events", HttpCompletionOption.ResponseHeadersRead);
            await GuiHarness.WaitForAsync(() => viewModel.HasVideoConnections);
            window.Navigation.SelectedIndex = 3;
            GuiHarness.Flush();
            Assert.True(viewModel.HasVideoConnections);
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            if (draft) Rows(page).Single().FindControl<TextBox>("UidInput")!.Text = "44";
            GuiHarness.Flush();
            Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 42, "new.mp4");
            Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 43, "new.mp4");
            window.Navigation.SelectedIndex = 0;
            window.Navigation.SelectedIndex = 3;
            GuiHarness.Flush();
            Assert.Equal(1, viewModel.WebServiceStatus.VideoConnections);
            using var reader = new StreamReader(await original.Content.ReadAsStreamAsync());
            if (draft)
            {
                var row = Assert.Single(Rows(page));
                Assert.Equal("old.mp4", row.FileName);
                Assert.False(row.FindControl<Button>("TestButton")!.IsEnabled);
                Assert.Contains("保存后才能试播", row.FindControl<TextBlock>("RowMessage")!.Text);
                Assert.False(viewModel.AnimationBusy);
                return true;
            }
            Assert.All(Rows(page), row => Assert.True(row.FindControl<Button>("TestButton")!.IsEnabled));
            Click(Rows(page).Single(row => row.Uid == 43), "TestButton");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string? line;
            do { line = await reader.ReadLineAsync(deadline.Token); } while (line is not null && !line.StartsWith("data: "));
            Assert.NotNull(line);
            using var payload = JsonDocument.Parse(line[6..]);
            Assert.False(payload.RootElement.TryGetProperty("uid", out _));
            Assert.Equal("new video", await http.GetStringAsync(viewModel.VideoAddressText + "/files/" + payload.RootElement.GetProperty("id").GetString()));
            using var report = await http.PostAsJsonAsync(viewModel.VideoAddressText + "/report", new { id = payload.RootElement.GetProperty("id").GetString(), success = true });
            await GuiHarness.WaitForAsync(() => !viewModel.AnimationBusy);
            Assert.Equal(PlaybackState.Completed, viewModel.RecentAnimations.Single().Playback.State);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Adding_a_video_waits_for_uid_and_save_before_copying() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        Directory.CreateDirectory(harness.DataDirectory);
        var source = Path.Combine(harness.DataDirectory, "欢迎.mp4");
        File.WriteAllText(source, "original video");
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation, () => Task.FromResult<string?>(source));
        var window = new Window { Content = page };
        window.Show();
        try
        {
            page.FindControl<Button>("AddAnimationButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            var row = Assert.Single(page.FindControl<StackPanel>("AnimationRows")!.Children);
            Assert.Equal("", row.FindControl<TextBox>("UidInput")!.Text);
            Assert.Equal("保存后才能试播", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            Assert.False(Directory.Exists(Path.Combine(harness.DataDirectory, "videos")));
            row.FindControl<TextBox>("UidInput")!.Text = "42";
            GuiHarness.Flush();
            Assert.Contains("保存后才能试播", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.True(page.FindControl<Button>("SaveButton")!.IsEnabled);
            page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await GuiHarness.WaitForAsync(() => page.IsEnabled);
            Assert.Equal("已保存。", page.FindControl<TextBlock>("Message")!.Text);
            Assert.Equal("欢迎.mp4", Settings.Load(harness.DataDirectory).WelcomeAnimationVideos[42].Value);
            Assert.Equal("original video", File.ReadAllText(Path.Combine(harness.DataDirectory, "videos", "欢迎.mp4")));
            Assert.Equal("original video", File.ReadAllText(source));
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(true, 2, 1, null, "运行中", "视频页：已有 2 个推流软件连着")]
    [InlineData(true, 2, 0, null, "运行中", "视频页：已有 2 个推流软件连着")]
    [InlineData(true, 0, 1, null, "运行中", "视频页还没有推流软件连着，先在直播姬里加好再试播")]
    [InlineData(true, 0, 0, null, "运行中", "视频页还没有推流软件连着，先在直播姬里加好再试播")]
    [InlineData(false, 0, 0, "端口 17891 被别的程序占用", "没打开：端口 17891 被别的程序占用", "网页服务没打开：端口 17891 被别的程序占用，试播和进场都播不了")]
    [InlineData(false, 0, 0, null, "未运行", "网页服务没打开，试播和进场都播不了")]
    public Task Web_service_card_shows_each_page_count_setup_and_errors(bool running, int videos, int lotteries, string? error, string expected, string videoRow) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.General) { DataContext = viewModel };
        var window = new Window { Content = page };
        window.Show();
        try
        {
            viewModel.Apply(new WebServiceStatus(running, 17891, videos, lotteries, error));
            GuiHarness.Flush();
            Assert.Equal(expected, page.FindControl<TextBlock>("WebServiceStatusText")!.Text);
            Assert.Equal("http://localhost:17891/video", page.FindControl<TextBox>("VideoAddress")!.Text);
            Assert.Equal("http://localhost:17891/lottery", page.FindControl<TextBox>("LotteryAddress")!.Text);
            string Count(int count) => !running ? "服务没打开" : count > 0 ? $"{count} 个推流软件连着" : "还没有连着";
            Assert.Equal(Count(videos), page.FindControl<TextBlock>("VideoConnectionsText")!.Text);
            Assert.Equal(Count(lotteries), page.FindControl<TextBlock>("LotteryConnectionsText")!.Text);
            Assert.Equal(running && videos == 0, page.FindControl<Control>("VideoSetupHelp")!.IsVisible);
            Assert.Equal(running && lotteries == 0, page.FindControl<Control>("LotterySetupHelp")!.IsVisible);
            var help = page.FindControl<TextBlock>("WebServiceHelp")!;
            Assert.Equal(running || error is not null, help.IsVisible);
            if (error is not null) Assert.StartsWith("换一个端口并保存", help.Text);
            Assert.Equal(videoRow, viewModel.VideoPageText);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Welcome_animation_settings_follow_welcome_and_start_with_an_empty_viewer_list() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            Assert.Equal(6, window.Navigation.ItemCount);
            window.Navigation.SelectedIndex = 3;
            GuiHarness.Flush();
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            Assert.Equal("欢迎动画", page.FindControl<TextBlock>("PageTitle")!.Text);
            Assert.True(page.FindControl<Control>("EmptyAnimations")!.IsEffectivelyVisible);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
