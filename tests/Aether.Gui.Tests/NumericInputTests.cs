using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class NumericInputTests
{
    private const string SettingsText = """
        web_service:
          port: 1
        log:
          retention_days: 1
        send:
          interval_seconds: 1
        welcome:
          uids: [1234567890123456]
          fan_club_level: 1
          wealth_level: 1
          guard: 舰长
        """;

    [Theory]
    [InlineData(1200)]
    [InlineData(1040)]
    public Task Numbers_and_units_fit_their_content_and_buttons_edit_the_setting(int width) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        File.WriteAllText(Settings.FilePath(harness.DataDirectory), SettingsText);
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, Width = width };
        window.Show();
        try
        {
            foreach (var (index, name, unit) in new[]
            {
                (5, "发送间隔秒数", "秒"), (6, "日志保留天数", "天"),
                (2, "粉丝团等级门槛", "级"), (2, "荣耀等级门槛", "级")
            })
            {
                window.Navigation.SelectedIndex = index;
                GuiHarness.Flush();
                var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
                var number = page.GetVisualDescendants().OfType<NumericUpDown>()
                    .Single(x => x.IsEffectivelyVisible && AutomationProperties.GetName(x) == name);
                number.BringIntoView();
                GuiHarness.Flush();
                Assert.Equal(120, number.Bounds.Width);
                if (index == 2)
                {
                    var reached = Assert.IsType<Grid>(number.Parent).Children.OfType<TextBlock>().Single(x => x.Text == "达到");
                    Assert.True(number.TranslatePoint(default, window)!.Value.X - reached.TranslatePoint(new Point(reached.Bounds.Width, 0), window)!.Value.X >= 12);
                }
                var label = Assert.IsAssignableFrom<Control>(number.Parent).GetVisualDescendants().OfType<TextBlock>()
                    .Single(x => x.IsEffectivelyVisible && x.Text == unit);
                Assert.Equal(8, label.TranslatePoint(default, window)!.Value.X - number.TranslatePoint(new Point(number.Bounds.Width, 0), window)!.Value.X);
                var buttons = number.GetVisualDescendants().OfType<Button>().ToArray();
                var increase = Assert.Single(buttons, x => x.Name == "PART_IncreaseButton");
                var decrease = Assert.Single(buttons, x => x.Name == "PART_DecreaseButton");
                Assert.True(increase.IsEffectivelyVisible);
                Assert.False(decrease.IsEffectivelyEnabled);
                Assert.Equal(name + "加 1", AutomationProperties.GetName(increase));
                Assert.Equal(name + "减 1", AutomationProperties.GetName(decrease));
                Assert.Equal(28, increase.Bounds.Width);
                Assert.Equal(28, decrease.Bounds.Width);
                var point = increase.TranslatePoint(new Point(increase.Bounds.Width / 2, increase.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                GuiHarness.Flush();
                Assert.Equal(2, number.Value);
                Assert.True(decrease.IsEffectivelyEnabled);
                Assert.True(GuiHarness.FindVisual<TextBlock>(page, "DirtyMessage").IsEffectivelyVisible);
                var text = number.GetVisualDescendants().OfType<TextBox>().Single();
                text.Focus();
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                GuiHarness.Flush();
                Assert.Equal(3, number.Value);
                window.MouseWheel(number.TranslatePoint(new Point(10, 10), window)!.Value, new Vector(0, -1));
                GuiHarness.Flush();
                Assert.Equal(2, number.Value);
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task Whole_number_control_shows_focus_errors_and_read_only_state(bool dark, bool readOnly) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        File.WriteAllText(Settings.FilePath(harness.DataDirectory), readOnly ? SettingsText.ReplaceLineEndings("\n").Replace(": 1\n", ": |-\n    1\n") : SettingsText);
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory)
        {
            DataContext = viewModel, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            foreach (var index in new[] { 5, 6, 2 })
            {
                window.Navigation.SelectedIndex = index;
                GuiHarness.Flush();
                var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
                foreach (var number in page.GetVisualDescendants().OfType<NumericUpDown>().Where(x => x.IsEffectivelyVisible))
                {
                    number.BringIntoView();
                    GuiHarness.Flush();
                    var text = number.GetVisualDescendants().OfType<TextBox>().Single();
                    var spinner = number.GetVisualDescendants().OfType<ButtonSpinner>().Single();
                    var outline = spinner.GetVisualDescendants().OfType<Border>().First();
                    Assert.Equal(number.Bounds.Width, outline.Bounds.Width);
                    Assert.Equal(number.Bounds.Height, outline.Bounds.Height);
                    Assert.Equal(Color.Parse(dark ? "#3A3E45" : "#D5D9E0"), ColorOf(outline.BorderBrush));
                    var buttons = number.GetVisualDescendants().OfType<Button>().ToArray();
                    var decrease = buttons.Single(x => x.Name == "PART_DecreaseButton");
                    var icon = decrease.GetVisualDescendants().OfType<PathIcon>().Single();
                    Assert.Equal(Color.Parse(dark ? "#4A4F57" : "#B4BAC4"), ColorOf(icon.Foreground));
                    if (!readOnly)
                    {
                        var increase = buttons.Single(x => x.Name == "PART_IncreaseButton");
                        window.MouseMove(increase.TranslatePoint(new Point(10, 10), window)!.Value);
                        GuiHarness.Flush();
                        Assert.Equal(Color.Parse(dark ? "#2E3136" : "#EEF0F3"),
                            ColorOf(increase.GetVisualDescendants().OfType<ContentPresenter>().First().Background));
                        Assert.Equal(Color.Parse(dark ? "#E8EAED" : "#16181D"),
                            ColorOf(increase.GetVisualDescendants().OfType<PathIcon>().Single().Foreground));
                        window.MouseMove(default);
                    }
                    text.Focus();
                    GuiHarness.Flush();
                    Assert.Equal(Color.Parse(readOnly ? dark ? "#26282C" : "#EEF0F3" : dark ? "#1B1C1F" : "#FFFFFF"), ColorOf(outline.Background));
                    Assert.All(text.GetVisualDescendants().OfType<Border>().Where(x => x.Name == "PART_BorderElement"),
                        border => Assert.Equal(Colors.Transparent, ColorOf(border.Background)));
                    if (readOnly)
                    {
                        Assert.All(buttons, button => Assert.False(button.IsEffectivelyEnabled));
                        Assert.Equal(Color.Parse(dark ? "#9097A3" : "#5B6270"), ColorOf(text.Foreground));
                    }
                    else
                    {
                        Assert.Equal(Color.Parse(dark ? "#8DA2FB" : "#3358D4"), ColorOf(outline.BorderBrush));
                        text.Text = "1.5";
                        GuiHarness.Flush();
                        Assert.Equal(Color.Parse(dark ? "#F87171" : "#B91C1C"), ColorOf(outline.BorderBrush));
                        Assert.False(GuiHarness.FindVisual<Button>(page, "SaveButton").IsEffectivelyEnabled);
                        Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), x => x.IsEffectivelyVisible &&
                            (x.Text?.Contains("正整数") == true || x.Text?.Contains("1–65535") == true || x.Text == "至少 1 级"));
                        text.Text = "12345";
                        GuiHarness.Flush();
                        Assert.Equal(Color.Parse(dark ? "#8DA2FB" : "#3358D4"), ColorOf(outline.BorderBrush));
                        GuiHarness.FindVisual<Button>(page, "SaveButton").Focus();
                        GuiHarness.Flush();
                    }
                }
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(1200)]
    [InlineData(1040)]
    public Task Short_fields_stay_compact_and_condition_delete_buttons_stay_at_the_right(int width) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        File.WriteAllText(Settings.FilePath(harness.DataDirectory), SettingsText);
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, Width = width };
        window.Show();
        try
        {
            foreach (var (index, name, expectedWidth) in new[]
            {
                (1, "DailyKeywordInput", 240), (1, "MonthlyKeywordInput", 240), (6, "LogLevelInput", 200),
                (2, "UidInput", 200), (2, "GuardInput", 132)
            })
            {
                window.Navigation.SelectedIndex = index;
                GuiHarness.Flush();
                var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
                var input = page.GetVisualDescendants().OfType<Control>().Single(x => x.Name == name && x.IsEffectivelyVisible);
                input.BringIntoView();
                GuiHarness.Flush();
                Assert.Equal(expectedWidth, input.Bounds.Width);
                if (index != 2)
                {
                    var title = GuiHarness.FindVisual<TextBlock>(page, "PageTitle");
                    Assert.Equal(title.TranslatePoint(default, page)!.Value.X, input.TranslatePoint(default, page)!.Value.X);
                }
                else
                {
                    var row = input.GetVisualAncestors().OfType<WelcomeConditionRow>().Single();
                    var remove = row.FindControl<Button>("RemoveButton")!;
                    Assert.Equal(row.Bounds.Width, remove.TranslatePoint(new Point(remove.Bounds.Width, 0), row)!.Value.X);
                    Assert.True(input.TranslatePoint(new Point(input.Bounds.Width, 0), row)!.Value.X <= remove.TranslatePoint(default, row)!.Value.X - 8);
                    if (name == "GuardInput")
                    {
                        var reached = Assert.IsType<Grid>(input.Parent).Children.OfType<TextBlock>().Single(x => x.Text == "达到");
                        Assert.True(input.TranslatePoint(default, row)!.Value.X - reached.TranslatePoint(new Point(reached.Bounds.Width, 0), row)!.Value.X >= 12);
                    }
                }
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
