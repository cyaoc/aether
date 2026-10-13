using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;

namespace Aether.Gui.Tests;

public sealed class VisualReviewTests
{
    // Set AETHER_SCREENSHOT_DIR to retain the six pages per size/theme for visual review.
    [Theory]
    [InlineData(960, 640, false)]
    [InlineData(960, 640, true)]
    [InlineData(760, 520, false)]
    [InlineData(760, 520, true)]
    public Task Render_all_pages_with_Skia(int width, int height, bool dark) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.NavResponse = _ => Task.FromResult(GuiHarness.AccountResponse("Aether助手"));
        var roomStarted = new TaskCompletionSource();
        harness.RoomResponse = async (_, token) =>
        {
            roomStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        Settings.Load(harness.DataDirectory);
        var path = Settings.FilePath(harness.DataDirectory);
        File.WriteAllText(path, File.ReadAllText(path).Replace("enabled: false", "enabled: true").Replace("uids: []", "uids: [12345, 67890]"));
        var clock = new FakeTimeProvider();
        var viewModel = harness.CreateViewModel(clock);
        viewModel.RoomNumber = "1768506153";
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, Width = width, Height = height, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            viewModel.ConnectCommand.Execute(null);
            await roomStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.Apply(new Connected());
            clock.Advance(TimeSpan.FromMinutes(42));
            var time = new DateTimeOffset(2026, 10, 12, 20, 41, 0, TimeSpan.FromHours(8));
            var names = new[] { "一只小鹿", "柚子不酸", "Aether助手", "晚风吹过山岗", "Aether助手", "是阿橙呀", "momo_7", "夜航船" };
            var messages = new[] { "来了来了", "今日盲盒", "@柚子不酸 投入52电池 亏12.6电池", "哈哈哈哈哈哈这个反应", "欢迎舰长晚风吹过山岗", "刚下班，今天播到几点？", "这首歌叫什么名字", "晚上好！这是一条用来检查自动换行的较长弹幕，窄窗口也应该能完整阅读。" };
            for (var i = 0; i < names.Length; i++)
                viewModel.Apply(new Danmaku(time.AddSeconds(i * 3), names[i], messages[i]) { Uid = names[i] == "Aether助手" ? 123 : 1000 + i });
            for (var i = 0; i < 6; i++)
            {
                viewModel.Apply(new SendQueueUpdate(i + 1, time.AddSeconds(-i * 12), SendQueueItemKind.Welcome,
                    i == 1 ? SendQueueItemState.Queued : i == 3 ? SendQueueItemState.Discarded : SendQueueItemState.Sent, 1000 + i, names[i])
                { Text = "欢迎" + names[i], WelcomeCondition = i % 2 == 0 ? WelcomeCondition.Guard : WelcomeCondition.FanClubLevel,
                    WelcomeValue = i % 2 == 0 ? "舰长" : "12", Reason = i == 3 ? "进场超过 30 秒" : null });
                viewModel.Apply(new SendQueueUpdate(i + 10, time.AddSeconds(-i * 12), i % 2 == 0 ? SendQueueItemKind.BlindBoxQuery : SendQueueItemKind.BlindBoxAnnouncement,
                    i == 1 ? SendQueueItemState.WaitingToRetry : SendQueueItemState.Sent, 1000 + i, names[i])
                { Text = "@" + names[i] + " 投入52电池 亏12.6电池", RetrySeconds = 1 });
            }
            var navigation = window.FindControl<ListBox>("Navigation")!;
            var pages = new[] { "danmaku", "blind-box", "welcome", "send-queue", "general" };
            for (var i = 0; i < pages.Length; i++)
            {
                navigation.SelectedIndex = i;
                Capture(pages[i]);
            }
            navigation.SelectedIndex = 2;
            GuiHarness.Flush();
            var welcome = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            welcome.FindControl<TextBox>("WelcomeTextInput")!.Text = "欢迎{昵称}回到直播间";
            GuiHarness.Flush();
            welcome.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Capture("welcome-reconnect");
            File.WriteAllText(path, "log:\n  level: @bad\n");
            navigation.SelectedIndex = 4;
            Capture("settings-error");
            viewModel.IsLoginPage = true;
            viewModel.Apply(new WatchQrCode("https://example.test/visual-review"));
            Capture("login");
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;

        void Capture(string page)
        {
            GuiHarness.Flush();
            using var bitmap = window.CaptureRenderedFrame();
            Assert.NotNull(bitmap);
            Assert.Equal(width, bitmap.PixelSize.Width);
            Assert.Equal(height, bitmap.PixelSize.Height);
            if (Environment.GetEnvironmentVariable("AETHER_SCREENSHOT_DIR") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output);
                bitmap.Save(Path.Combine(output, $"{page}-{width}x{height}-{(dark ? "dark" : "light")}.png"), PngBitmapEncoderOptions.Default);
            }
        }
    }, TestContext.Current.CancellationToken);
}
