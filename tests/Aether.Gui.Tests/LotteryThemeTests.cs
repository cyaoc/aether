using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.Time.Testing;
using System.Net;
using System.Net.Sockets;

namespace Aether.Gui.Tests;

public sealed class LotteryThemeTests
{
    [Fact]
    public Task Both_modes_share_the_selected_theme_and_example_status_without_a_room_connection() => HeadlessApp.Session.Dispatch(async () =>
    {
        var time = new FakeTimeProvider();
        using var h = new GuiHarness(time);
        var vm = h.CreateViewModel();
        var page = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720 };
        window.Show();
        try
        {
            LotteryThemePicker[] pickers = [page.FindControl<LotteryThemePicker>("PoolTheme")!,
                page.FindControl<EntryLotteryPanel>("EntryContent")!.FindControl<LotteryThemePicker>("EntryTheme")!];
            foreach (var picker in pickers)
            {
                Assert.True(picker.FindControl<RadioButton>("StarSummonCard")!.IsChecked);
                var play = picker.FindControl<Button>("ExampleButton")!;
                Assert.False(play.IsEnabled);
                Assert.Contains("网页服务", picker.FindControl<TextBlock>("ExampleHint")!.Text);
            }
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["web_service.port"] = port.ToString() });
            await vm.StartWebServiceAsync(h.DataDirectory);
            GuiHarness.Flush();
            Assert.All(pickers, picker => Assert.True(picker.FindControl<Button>("ExampleButton")!.IsEnabled));
            pickers[0].FindControl<Button>("ExampleButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            Assert.True(vm.LotteryState.IsExamplePlaying);
            foreach (var picker in pickers)
            {
                Assert.False(picker.FindControl<Button>("ExampleButton")!.IsEnabled);
                Assert.Equal("示例播放中…", picker.FindControl<Button>("ExampleButton")!.Content);
                Assert.Contains("直播中观众也会看到", picker.FindControl<TextBlock>("ExampleHint")!.Text);
            }
            time.Advance(TimeSpan.FromSeconds(21));
            await GuiHarness.WaitForAsync(() => !vm.LotteryState.IsExamplePlaying);
            Assert.All(pickers, picker => Assert.True(picker.FindControl<Button>("ExampleButton")!.IsEnabled));
            pickers[1].FindControl<Button>("ExampleButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(vm.LotteryState.IsExamplePlaying);
            time.Advance(TimeSpan.FromSeconds(21));
            await GuiHarness.WaitForAsync(() => !vm.LotteryState.IsExamplePlaying);
            Assert.False(File.Exists(Path.Combine(h.DataDirectory, "aether.db")));
            vm.Apply(new WebServiceStatus(false, 18080, 0, 0, "端口被占用"));
            Assert.All(pickers, picker => Assert.Contains("端口被占用", picker.FindControl<TextBlock>("ExampleHint")!.Text));
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
