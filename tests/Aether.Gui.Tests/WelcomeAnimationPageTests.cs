using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Net;
using System.Net.Sockets;
using Avalonia.Input.Platform;
using System.Net.Http.Json;
using System.Text.Json;

namespace Aether.Gui.Tests;

public sealed class WelcomeAnimationPageTests
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
            viewModel.Apply(new WelcomeAnimationPlayback("pending", 42, "a.mp4", false, WelcomeAnimationState.Queued));
            for (var i = 0; i < 102; i++)
                viewModel.Apply(new WelcomeAnimationPlayback(i.ToString(), 42, "a.mp4", false, WelcomeAnimationState.Completed));
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
    [InlineData("没有推流软件连着")]
    [InlineData("排队超过 30 秒")]
    public Task Skipped_playback_keeps_its_specific_reason(string reason) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        viewModel.Apply(new WelcomeAnimationPlayback("skip", 42, "a.mp4", false, WelcomeAnimationState.Skipped, reason));
        Assert.Equal("没播 · " + reason, viewModel.RecentAnimations.Single().Status);
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
        var owned = Video(harness, "welcome-animations", "video.mp4", "old video");
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
            Assert.Single(Directory.GetFiles(Path.Combine(harness.DataDirectory, "welcome-animations")));
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("", "UID 没填")]
    [InlineData("0", "UID 不合法")]
    [InlineData("-1", "UID 不合法")]
    [InlineData("abc", "UID 不合法")]
    [InlineData("9223372036854775808", "UID 不合法")]
    [InlineData("00042", "UID 重复")]
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
            Add(page, uid);
            Assert.Contains(hint, Rows(page)[1].FindControl<TextBlock>("RowMessage")!.Text);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            Assert.False(Rows(page)[1].FindControl<Button>("TestButton")!.IsEnabled);
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Trial_is_gated_by_saved_video_connection_file_and_other_playback() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var path = Video(harness, "welcome-animations", "video.mp4");
        Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 42, "video.mp4");
        var viewModel = harness.CreateViewModel();
        await viewModel.ReloadAnimationPageAsync(Settings.Load(harness.DataDirectory));
        var replacement = Video(harness, "source", "video.mp4");
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation, () => Task.FromResult<string?>(replacement)) { DataContext = viewModel };
        var window = new Window { Content = page };
        window.Show();
        try
        {
            var row = Rows(page).Single();
            var button = row.FindControl<Button>("TestButton")!;
            Assert.False(button.IsEnabled);
            viewModel.Apply(new AnimationPageStatus(true, 17890, 1));
            Assert.True(button.IsEnabled);
            viewModel.Apply(new WelcomeAnimationPlayback("other", 43, "other.mp4", false, WelcomeAnimationState.Queued));
            Assert.False(button.IsEnabled);
            viewModel.Apply(new WelcomeAnimationPlayback("other", 43, "other.mp4", false, WelcomeAnimationState.Completed));
            Assert.True(button.IsEnabled);
            viewModel.Apply(new WelcomeAnimationPlayback("one", 42, "video.mp4", true, WelcomeAnimationState.Playing));
            Assert.Equal("播放中", button.Content);
            Assert.Contains("正在播放", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.False(button.IsEnabled);
            viewModel.Apply(new WelcomeAnimationPlayback("one", 42, "video.mp4", true, WelcomeAnimationState.NoReport));
            Assert.DoesNotContain("上次播放失败", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.True(button.IsEnabled);
            viewModel.Apply(new WelcomeAnimationPlayback("failure", 42, "video.mp4", true, WelcomeAnimationState.Failed));
            Assert.Contains("上次播放失败", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.Contains("H.264", row.FindControl<TextBlock>("RowMessage")!.Text);
            viewModel.Apply(new WelcomeAnimationPlayback("success", 42, "video.mp4", true, WelcomeAnimationState.Completed));
            Assert.Contains("上次播放失败", row.FindControl<TextBlock>("RowMessage")!.Text);
            File.Delete(path);
            viewModel.Apply(new AnimationPageStatus(true, 17890, 2));
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
    [InlineData("welcome_animation:\n  enabled: |-\n    true\n  port: |-\n    17891\nwelcome_animation_videos: {42: video.mp4}\n")]
    [InlineData("welcome_animation: {enabled: true, port: 17891}\nwelcome_animation_videos:\n  &viewer 42: video.mp4\nother: *viewer\n")]
    public Task Noneditable_settings_keep_controls_read_only_and_explain_where_to_edit(string yaml) => HeadlessApp.Session.Dispatch(() =>
    {
        using var harness = new GuiHarness();
        Directory.CreateDirectory(harness.DataDirectory);
        File.WriteAllText(Settings.FilePath(harness.DataDirectory), yaml);
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation);
        Assert.False(page.FindControl<ToggleSwitch>("AnimationInput")!.IsEnabled);
        Assert.True(page.FindControl<NumericUpDown>("AnimationPortInput")!.IsReadOnly);
        Assert.False(page.FindControl<NumericUpDown>("AnimationPortInput")!.AllowSpin);
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
            Assert.Empty(Directory.GetFiles(Path.Combine(harness.DataDirectory, "welcome-animations")));
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

    private static string Copy(GuiHarness harness, string name) => Path.Combine(harness.DataDirectory, "welcome-animations", name);
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

    [Fact]
    public Task Copy_address_reports_success_for_two_seconds() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation) { DataContext = viewModel };
        var window = new Window { Content = page };
        window.Show();
        try
        {
            viewModel.Apply(new AnimationPageStatus(true, 17891, 0));
            GuiHarness.Flush();
            var button = page.FindControl<Button>("CopyAnimationAddressButton")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await GuiHarness.WaitForAsync(() => Equals(button.Content, "已复制"));
            Assert.Equal("http://localhost:17891/", await window.Clipboard!.TryGetTextAsync());
            await GuiHarness.WaitForAsync(() => Equals(button.Content, "复制"));
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(WelcomeAnimationState.Queued, "排队中", true)]
    [InlineData(WelcomeAnimationState.Playing, "播放中", true)]
    [InlineData(WelcomeAnimationState.Completed, "已播放", false)]
    [InlineData(WelcomeAnimationState.Failed, "播放失败", false)]
    [InlineData(WelcomeAnimationState.NoReport, "没收到播放回报", false)]
    [InlineData(WelcomeAnimationState.Skipped, "没播 · 10 分钟内播过", false)]
    public Task Recent_playback_shows_progress_reason_and_test_marker(WelcomeAnimationState state, string expected, bool busy) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation) { DataContext = viewModel };
        var window = new Window { Content = page };
        window.Show();
        try
        {
            viewModel.Apply(new WelcomeAnimationPlayback("one", 42, "欢迎.mp4", true, WelcomeAnimationState.Queued));
            viewModel.Apply(new WelcomeAnimationPlayback("one", 42, "欢迎.mp4", true, state, state == WelcomeAnimationState.Skipped ? "10 分钟内播过" : null));
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
    public Task Animation_page_runs_and_is_reachable_without_login_and_restarts_only_after_save() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        harness.LoginResponse = (_, token) => new TaskCompletionSource<HttpResponseMessage>().Task.WaitAsync(token);
        var port = FreePort();
        Video(harness, "welcome-animations", "trial.mp4", "trial video");
        Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 42, "trial.mp4");
        var settings = Settings.Load(harness.DataDirectory);
        Settings.Save(harness.DataDirectory, new Dictionary<string, string>
        {
            ["welcome_animation.enabled"] = "true", ["welcome_animation.port"] = port.ToString()
        }, settings);
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        window.Show();
        try
        {
            await GuiHarness.WaitForAsync(() => viewModel.IsLoginPage && viewModel.AnimationPageStatus.Running);
            Assert.Contains("<video", await http.GetStringAsync(viewModel.AnimationAddressText));
            window.FindControl<Button>("OpenAnimationsWithoutLoginButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await GuiHarness.WaitForAsync(() => !viewModel.IsLoginPage);
            Assert.Equal(3, window.Navigation.SelectedIndex);
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            using (var response = await http.GetAsync(viewModel.AnimationAddressText + "events", HttpCompletionOption.ResponseHeadersRead))
            using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync()))
            {
                await GuiHarness.WaitForAsync(() => viewModel.HasAnimationPages);
                Assert.True(Rows(page).Single().FindControl<Button>("TestButton")!.IsEnabled);
                Click(Rows(page).Single(), "TestButton");
                Assert.False(Rows(page).Single().FindControl<Button>("TestButton")!.IsEnabled);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                string? line;
                do { line = await reader.ReadLineAsync(deadline.Token); } while (line is not null && !line.StartsWith("data: "));
                Assert.NotNull(line);
                using var payload = JsonDocument.Parse(line[6..]);
                Assert.Equal("42", payload.RootElement.GetProperty("uid").GetString());
                Assert.Equal("trial video", await http.GetStringAsync(viewModel.AnimationAddressText + "videos/42"));
                using var report = await http.PostAsJsonAsync(viewModel.AnimationAddressText + "report", new { id = payload.RootElement.GetProperty("id").GetString(), success = true });
                Assert.Equal(HttpStatusCode.NoContent, report.StatusCode);
                await GuiHarness.WaitForAsync(() => viewModel.RecentAnimations.Single().Playback.State == WelcomeAnimationState.Completed);
                Assert.True(viewModel.RecentAnimations.Single().Playback.IsTest);
            }
            var nextPort = FreePort();
            page.FindControl<NumericUpDown>("AnimationPortInput")!.Text = nextPort.ToString();
            GuiHarness.Flush();
            Assert.Equal(port, viewModel.AnimationPageStatus.Port);
            page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await GuiHarness.WaitForAsync(() => viewModel.AnimationPageStatus.Port == nextPort && viewModel.AnimationPageStatus.Running && page.IsEnabled);
            Assert.Contains("<video", await http.GetStringAsync(viewModel.AnimationAddressText));
            page.FindControl<ToggleSwitch>("AnimationInput")!.IsChecked = false;
            GuiHarness.Flush();
            page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await GuiHarness.WaitForAsync(() => !viewModel.AnimationPageStatus.Running && page.IsEnabled);
            Assert.False(viewModel.HasRoomConnection);
            Assert.Empty(harness.RejectedRequests);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Navigation_applies_external_animation_mappings_before_trial_without_restarting_unchanged_settings(bool draft) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        Video(harness, "welcome-animations", "old.mp4", "old video");
        Video(harness, "welcome-animations", "new.mp4", "new video");
        Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 42, "old.mp4");
        Settings.Save(harness.DataDirectory, new Dictionary<string, string>
        {
            ["welcome_animation.enabled"] = "true", ["welcome_animation.port"] = FreePort().ToString()
        }, Settings.Load(harness.DataDirectory));
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        window.Show();
        try
        {
            await GuiHarness.WaitForAsync(() => viewModel.AnimationPageStatus.Running);
            using var original = await http.GetAsync(viewModel.AnimationAddressText + "events", HttpCompletionOption.ResponseHeadersRead);
            await GuiHarness.WaitForAsync(() => viewModel.HasAnimationPages);
            window.Navigation.SelectedIndex = 3;
            GuiHarness.Flush();
            Assert.True(viewModel.HasAnimationPages);
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            if (draft) page.FindControl<NumericUpDown>("AnimationPortInput")!.Text = FreePort().ToString();
            GuiHarness.Flush();
            Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 42, "new.mp4");
            Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, 43, "new.mp4");
            window.Navigation.SelectedIndex = 0;
            window.Navigation.SelectedIndex = 3;
            GuiHarness.Flush();
            Assert.All(Rows(page), row => Assert.False(row.FindControl<Button>("TestButton")!.IsEnabled));
            await GuiHarness.WaitForAsync(() => viewModel.AnimationPageStatus.Running && !viewModel.HasAnimationPages);
            using var response = await http.GetAsync(viewModel.AnimationAddressText + "events", HttpCompletionOption.ResponseHeadersRead);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            await GuiHarness.WaitForAsync(() => viewModel.HasAnimationPages);
            Assert.Equal("new video", await http.GetStringAsync(viewModel.AnimationAddressText + "videos/42"));
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
            Assert.Equal("43", payload.RootElement.GetProperty("uid").GetString());
            using var report = await http.PostAsJsonAsync(viewModel.AnimationAddressText + "report", new { id = payload.RootElement.GetProperty("id").GetString(), success = true });
            await GuiHarness.WaitForAsync(() => !viewModel.AnimationBusy);
            Assert.Equal(WelcomeAnimationState.Completed, viewModel.RecentAnimations.Single().Playback.State);
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
            Assert.Contains("UID", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            Assert.False(Directory.Exists(Path.Combine(harness.DataDirectory, "welcome-animations")));
            row.FindControl<TextBox>("UidInput")!.Text = "42";
            GuiHarness.Flush();
            Assert.Contains("保存后才能试播", row.FindControl<TextBlock>("RowMessage")!.Text);
            Assert.True(page.FindControl<Button>("SaveButton")!.IsEnabled);
            page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await GuiHarness.WaitForAsync(() => page.IsEnabled);
            Assert.Equal("已保存。", page.FindControl<TextBlock>("Message")!.Text);
            Assert.Equal("欢迎.mp4", Settings.Load(harness.DataDirectory).WelcomeAnimationVideos[42].Value);
            Assert.Equal("original video", File.ReadAllText(Path.Combine(harness.DataDirectory, "welcome-animations", "欢迎.mp4")));
            Assert.Equal("original video", File.ReadAllText(source));
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(true, 2, null, "✓ 已有 2 个推流软件连着动画页")]
    [InlineData(true, 0, null, "⚠ 还没有推流软件连着动画页")]
    [InlineData(false, 0, "端口 17891 被占用", "✕ 端口 17891 被占用")]
    public Task Animation_card_shows_connection_count_or_error(bool running, int pages, string? error, string expected) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        var page = new SettingsPage(harness.DataDirectory, SettingsSection.WelcomeAnimation) { DataContext = viewModel };
        var window = new Window { Content = page };
        window.Show();
        try
        {
            viewModel.Apply(new AnimationPageStatus(running, 17891, pages, error));
            GuiHarness.Flush();
            Assert.Equal(expected, page.FindControl<TextBlock>("AnimationPageStatusText")!.Text);
            Assert.Equal("http://localhost:17891/", page.FindControl<TextBox>("AnimationAddress")!.Text);
            Assert.Equal(pages == 0 && error is null, page.FindControl<TextBlock>("AnimationSetupHint")!.IsVisible);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Animation_page_follows_welcome_and_starts_with_an_empty_viewer_list() => HeadlessApp.Session.Dispatch(async () =>
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
