using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;

namespace Aether.Gui.Tests;

// Canvas GW1–GW4: 网页服务 and 更新和日志 tabs, and the web pages listed on the left beside the picked one's detail.
public sealed class GeneralSettingsPageTests
{
    private static readonly WebServiceStatus Running = new(true, 17890, 0, 0, 0);

    [Fact]
    public Task Opens_on_the_web_service_tab_and_the_other_tab_holds_updates_and_logs() => GeneralSettingsForm.Open(1040, 600, Running, (page, _) =>
    {
        Assert.Equal(["网页服务", "更新和日志"], page.FindControl<ListBox>("Tabs")!.Items.Cast<ListBoxItem>().Select(item => item.Content));
        Assert.Equal("端口保存后立即生效", page.TimingHint);
        Assert.True(page.FindControl<NumericUpDown>("WebPortInput")!.IsEffectivelyVisible);
        Assert.False(page.FindControl<ToggleSwitch>("AutoUpdateInput")!.IsEffectivelyVisible);
        Assert.False(page.FindControl<NumericUpDown>("RetentionInput")!.IsEffectivelyVisible);

        GuiHarness.Tab(page, 1);
        Assert.Equal("日志设置重启后生效；自动更新下次打开时生效", page.TimingHint);
        Assert.True(page.FindControl<ToggleSwitch>("AutoUpdateInput")!.IsEffectivelyVisible);
        Assert.True(page.FindControl<NumericUpDown>("RetentionInput")!.IsEffectivelyVisible);
        Assert.True(page.FindControl<ComboBox>("LogLevelInput")!.IsEffectivelyVisible);
        Assert.False(page.FindControl<NumericUpDown>("WebPortInput")!.IsEffectivelyVisible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task One_save_bar_saves_what_was_changed_on_both_tabs() => GeneralSettingsForm.Open(1040, 600, Running, async (page, harness) =>
    {
        var port = GuiHarness.FreePort();
        page.FindControl<NumericUpDown>("WebPortInput")!.Text = port.ToString();
        GuiHarness.Tab(page, 1);
        page.FindControl<ComboBox>("LogLevelInput")!.SelectedItem = LogLevel.Warning;
        GuiHarness.Flush();
        Assert.True(GuiHarness.FindVisual<TextBlock>(page, "DirtyMessage").IsVisible);

        await GuiHarness.SaveAsync(page);
        var saved = Settings.Load(harness.DataDirectory);
        Assert.Equal(port, saved.WebServicePort.Value);
        Assert.Equal(LogLevel.Warning, saved.LogLevel.Value);
    });

    [Fact]
    public Task The_page_list_names_every_page_and_picking_one_shows_only_its_detail() => GeneralSettingsForm.Open(1040, 600, Running, (page, _) =>
    {
        Assert.Equal(["视频页", "抽奖页", "礼物榜页", "桌宠页"],
            GeneralSettingsForm.Pages.Select(name => GeneralSettingsForm.Item(page, name).GetVisualDescendants().OfType<TextBlock>().First().Text));
        Assert.Equal("Video", ShownDetail(page));
        foreach (var name in GeneralSettingsForm.Pages)
        {
            GeneralSettingsForm.Pick(page, name);
            Assert.Equal(name, ShownDetail(page));
        }

        GuiHarness.Tab(page, 1);
        Assert.False(Shown(page.FindControl<ListBox>("PageList")!));
        Assert.DoesNotContain(GeneralSettingsForm.Pages, name => Shown(GeneralSettingsForm.Detail(page, name)));
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, 0, "服务没打开")]
    [InlineData(true, 0, "还没有连着")]
    [InlineData(true, 2, "2 个推流软件连着")]
    public Task Each_page_in_the_list_says_whether_streaming_software_is_connected(bool running, int connections, string expected) =>
        GeneralSettingsForm.Open(1040, 600, new WebServiceStatus(running, 17890, connections, connections, connections) { DesktopPetConnections = connections }, (page, _) =>
        {
            foreach (var name in GeneralSettingsForm.Pages)
            {
                var status = GeneralSettingsForm.Item(page, name).GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Classes.Contains("pageStatus"));
                Assert.Equal(expected, status.Children.OfType<TextBlock>().Single().Text);
                Assert.Equal(running && connections > 0, status.Classes.Contains("connected"));
                Assert.Equal(running && connections == 0, status.Classes.Contains("waiting"));
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status.Children.OfType<TextBlock>().Single()));
            }
            // The picked page's detail says the same thing again; only the list announces a change.
            var heading = GuiHarness.FindVisual<WebPageDetail>(page, "VideoDetail").GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "ConnectionsText");
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(heading));
            return Task.CompletedTask;
        });

    [Fact]
    public Task A_status_bar_opens_the_web_service_tab_with_its_own_page_picked() => OnMainWindow(1200, 720, (window, _, general) =>
    {
        var welcome = window.GetVisualDescendants().OfType<WelcomePanel>().Single();
        foreach (var (navigation, picked) in new[] { (2, "Video"), (3, "Video"), (4, "GiftBoard"), (5, "DesktopPet") })
        {
            GeneralSettingsForm.Pick(general, "Lottery");
            GuiHarness.Tab(general, 1);
            window.Navigation.SelectedIndex = navigation;
            if (navigation == 2) welcome.Select(1);
            GuiHarness.Flush();
            var bar = window.GetVisualDescendants().OfType<PageStatusBar>().Single(bar => bar.IsEffectivelyVisible);
            GuiHarness.Click(bar, "OpenGeneralSettingsButton");
            Assert.Equal(8, window.Navigation.SelectedIndex);
            Assert.Equal(0, general.FindControl<ListBox>("Tabs")!.SelectedIndex);
            Assert.Equal(picked, ShownDetail(general));
        }
    });

    [Theory]
    [InlineData(1200, 720)]
    [InlineData(1040, 600)]
    public Task Neither_tab_scrolls_at_supported_window_sizes(int width, int height) => OnMainWindow(width, height, (window, viewModel, general) =>
    {
        window.Navigation.SelectedIndex = 8;
        GuiHarness.Flush();
        // On 网页服务 the body takes the height it needs and the list and the detail share the rest, so those two are what must fit.
        var list = general.FindControl<ListBox>("PageList")!.GetVisualDescendants().OfType<ScrollViewer>().First();
        var detail = general.FindControl<ScrollViewer>("PageDetailScroll")!;
        // Every page has its steps while nobody is connected, the tallest its detail gets.
        foreach (var status in new[] { Running, new WebServiceStatus(true, 17890, 1, 1, 1) { DesktopPetConnections = 1 } })
        {
            viewModel.WebService.Apply(status);
            foreach (var name in GeneralSettingsForm.Pages)
            {
                GeneralSettingsForm.Pick(general, name);
                AssertFits(list);
                AssertFits(detail);
            }
        }
        GuiHarness.Tab(general, 1);
        AssertFits(GuiHarness.FindVisual<Grid>(general, "PART_BodyLayout").Children.OfType<ScrollViewer>().Single());
    });

    [Fact]
    public Task The_update_and_logs_tab_scrolls_when_the_window_is_too_short_for_it() => GeneralSettingsForm.Open(1040, 300, Running, (page, _) =>
    {
        GuiHarness.Tab(page, 1);
        var layout = GuiHarness.FindVisual<Grid>(page, "PART_BodyLayout");
        var body = layout.Children.OfType<ScrollViewer>().Single();
        Assert.True(body.Viewport.Height <= layout.Bounds.Height, $"{body.Viewport.Height} tall in {layout.Bounds.Height}");
        Assert.True(body.Extent.Height > body.Viewport.Height);
        return Task.CompletedTask;
    });

    private static void AssertFits(ScrollViewer viewer) =>
        Assert.True(viewer.Extent.Height <= viewer.Viewport.Height, $"{viewer.Extent.Height} tall in {viewer.Viewport.Height}");

    private static string ShownDetail(GeneralSettingsPage page) =>
        Assert.Single(GeneralSettingsForm.Pages, name => Shown(GeneralSettingsForm.Detail(page, name)));

    // The list and the details leave the page with their tab, and a control off the page still counts itself visible.
    private static bool Shown(Control control) => TopLevel.GetTopLevel(control) is not null && control.IsEffectivelyVisible;

    /// <summary>The main window of this size, with 桌宠 on so that her page shows its status bar.</summary>
    private static Task OnMainWindow(int width, int height, Action<MainWindow, MainWindowViewModel, GeneralSettingsPage> check) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.SaveSettings(("desktop_pet.enabled", "true"));
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, Width = width, Height = height };
        window.Show();
        try
        {
            await GuiHarness.AccountReadAsync(viewModel);
            check(window, viewModel, GeneralSettingsForm.Of(window));
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
