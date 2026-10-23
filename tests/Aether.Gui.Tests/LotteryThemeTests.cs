using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using System.Net;
using System.Net.Sockets;

namespace Aether.Gui.Tests;

public sealed class LotteryThemeTests
{
    [Fact]
    public Task Both_panels_share_pinned_results_and_explain_what_will_be_shown() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var panel = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 976, Height = 720 };
        window.Show();
        try
        {
            LotteryPinnedResultsToggle[] controls = [panel.FindControl<LotteryPinnedResultsToggle>("PoolPinnedResults")!,
                panel.FindControl<EntryLotteryPanel>("EntryContent")!.FindControl<LotteryPinnedResultsToggle>("EntryPinnedResults")!];
            Assert.All(controls, control =>
            {
                Assert.False(control.FindControl<ToggleSwitch>("PinnedInput")!.IsChecked);
                Assert.Equal("默认关；打开前可以先点‘全部不挂出’", control.FindControl<TextBlock>("Hint")!.Text);
            });
            controls[0].FindControl<ToggleSwitch>("PinnedInput")!.IsChecked = true;
            GuiHarness.Flush();
            Assert.True(vm.Lottery.LotteryState.PinnedResults);
            Assert.All(controls, control =>
            {
                Assert.True(control.FindControl<ToggleSwitch>("PinnedInput")!.IsChecked);
                Assert.Equal("下面没变灰的，就是直播画面上挂着的", control.FindControl<TextBlock>("Hint")!.Text);
            });
            controls[1].FindControl<ToggleSwitch>("PinnedInput")!.IsChecked = false;
            GuiHarness.Flush();
            Assert.All(controls, control => Assert.False(control.FindControl<ToggleSwitch>("PinnedInput")!.IsChecked));
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("MechaLock")]
    [InlineData("CapsuleGacha")]
    public Task Both_modes_share_the_selected_theme_and_example_status_without_a_room_connection(string theme) => HeadlessApp.Session.Dispatch(async () =>
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
            var card = pickers[0].FindControl<RadioButton>($"{theme}Card");
            Assert.NotNull(card);
            card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            Assert.Equal(theme, vm.Lottery.LotteryState.Theme.ToString());
            Assert.All(pickers, picker =>
            {
                Assert.True(picker.FindControl<RadioButton>($"{theme}Card")!.IsChecked);
                Assert.False(picker.FindControl<RadioButton>("StarSummonCard")!.IsChecked);
            });
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["web_service.port"] = port.ToString() });
            await h.SaveCredentialAsync();
            await vm.StartAsync(h.DataDirectory);
            GuiHarness.Flush();
            foreach (var picker in pickers)
            {
                Assert.True(picker.FindControl<Button>("ExampleButton")!.IsEnabled);
                Assert.Contains("直播中观众也会看到", picker.FindControl<TextBlock>("ExampleHint")!.Text);
            }
            pickers[0].FindControl<Button>("ExampleButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            Assert.True(vm.Lottery.LotteryState.IsExamplePlaying);
            Assert.Equal(theme, vm.Lottery.LotteryState.Theme.ToString());
            foreach (var picker in pickers)
            {
                Assert.False(picker.FindControl<Button>("ExampleButton")!.IsEnabled);
                Assert.Equal("示例播放中…", picker.FindControl<TextBlock>("ExampleText")!.Text);
                var hint = picker.FindControl<TextBlock>("ExampleHint")!;
                Assert.Contains("约 20 秒", hint.Text);
                Assert.Contains("accent", hint.Classes);
            }
            time.Advance(TimeSpan.FromSeconds(21));
            await GuiHarness.WaitForAsync(() => !vm.Lottery.LotteryState.IsExamplePlaying);
            Assert.All(pickers, picker => Assert.True(picker.FindControl<Button>("ExampleButton")!.IsEnabled));
            pickers[1].FindControl<Button>("ExampleButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(vm.Lottery.LotteryState.IsExamplePlaying);
            time.Advance(TimeSpan.FromSeconds(21));
            await GuiHarness.WaitForAsync(() => !vm.Lottery.LotteryState.IsExamplePlaying);
            // The saved credential created aether.db, so check that no example reached the lottery table.
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False"))
            {
                connection.Open();
                using var count = connection.CreateCommand();
                count.CommandText = "SELECT COUNT(*) FROM lottery";
                Assert.Equal(0L, count.ExecuteScalar());
            }
            pickers[1].FindControl<RadioButton>("StarSummonCard")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            Assert.All(pickers, picker => Assert.True(picker.FindControl<RadioButton>("StarSummonCard")!.IsChecked));
            vm.WebService.Apply(new WebServiceStatus(false, 18080, 0, 0, "端口被占用"));
            Assert.All(pickers, picker => Assert.Contains("端口被占用", picker.FindControl<TextBlock>("ExampleHint")!.Text));
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
