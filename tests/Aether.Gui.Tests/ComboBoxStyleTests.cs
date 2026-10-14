using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class ComboBoxStyleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task All_three_dropdowns_share_the_card_items_and_open_state(bool dark) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.SaveRecentRooms("12345", "42");
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory)
        {
            DataContext = viewModel, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            foreach (var (pageIndex, name) in new[] { (0, "RoomInput"), (4, "LogLevelInput"), (2, "GuardInput") })
            {
                window.Navigation.SelectedIndex = pageIndex;
                GuiHarness.Flush();
                var input = window.GetVisualDescendants().OfType<ComboBox>().Single(x => x.Name == name && x.IsEffectivelyVisible);
                input.BringIntoView();
                input.SelectedIndex = 0;
                input.IsDropDownOpen = true;
                GuiHarness.Flush();
                var card = input.ContainerFromIndex(0)!.GetVisualAncestors().OfType<Border>().Single(x => x.Name == "PopupBorder");
                Assert.Equal(Color.Parse(dark ? "#222428" : "#FFFFFF"), ColorOf(card.Background));
                Assert.Equal(Color.Parse(dark ? "#2E3136" : "#E3E6EB"), ColorOf(card.BorderBrush));
                Assert.Equal(new Thickness(1), card.BorderThickness);
                Assert.Equal(new CornerRadius(8), card.CornerRadius);
                Assert.Equal(new Thickness(4), card.Padding);
                Assert.Equal(input.Bounds.Width, card.Bounds.Width);
                Assert.Equal(input.PointToScreen(default).X, card.PointToScreen(default).X);
                Assert.Equal(input.PointToScreen(new Point(0, input.Bounds.Height)).Y + 4, card.PointToScreen(default).Y);
                Assert.NotEqual(default, card.BoxShadow);
                var outline = input.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "Background");
                Assert.Equal(Color.Parse(dark ? "#8DA2FB" : "#3358D4"), ColorOf(outline.BorderBrush));
                Assert.Equal(new Thickness(2), outline.BorderThickness);
                var glyph = input.GetVisualDescendants().OfType<PathIcon>().Single(x => x.Name == "DropDownGlyph");
                Assert.Equal(180, Assert.IsType<RotateTransform>(glyph.RenderTransform).Angle);
                var selected = Assert.IsType<ComboBoxItem>(input.ContainerFromIndex(0));
                var other = Assert.IsType<ComboBoxItem>(input.ContainerFromIndex(1));
                Assert.Equal(32, selected.Bounds.Height);
                Assert.Equal(name == "RoomInput" ? new Thickness(10, 0, 4, 0) : new Thickness(10, 0), selected.Padding);
                Assert.Equal(2, other.Bounds.Y - selected.Bounds.Bottom);
                var presenter = selected.GetVisualDescendants().OfType<ContentPresenter>().First();
                Assert.Equal(new CornerRadius(6), presenter.CornerRadius);
                Assert.Equal(Color.Parse(dark ? "#262F52" : "#E6EBFA"), ColorOf(presenter.Background));
                Assert.Equal(Color.Parse(dark ? "#B4C3FF" : "#2A47B0"), ColorOf(presenter.Foreground));
                Assert.Equal(FontWeight.SemiBold, presenter.GetValue(TextElement.FontWeightProperty));
                var root = TopLevel.GetTopLevel(other)!;
                root.MouseMove(other.TranslatePoint(new Point(5, 16), root)!.Value);
                GuiHarness.Flush();
                Assert.Equal(Color.Parse(dark ? "#26282C" : "#EEF0F3"),
                    ColorOf(other.GetVisualDescendants().OfType<ContentPresenter>().First().Background));
                root.MouseMove(selected.TranslatePoint(new Point(5, 16), root)!.Value);
                GuiHarness.Flush();
                Assert.Equal(Color.Parse(dark ? "#262F52" : "#E6EBFA"), ColorOf(presenter.Background));
                if (name == "RoomInput")
                {
                    var remove = other.GetVisualDescendants().OfType<Button>().Single();
                    Assert.Equal(new Size(24, 24), remove.Bounds.Size);
                    Assert.Equal(Color.Parse(dark ? "#9097A3" : "#5B6270"), ColorOf(remove.Foreground));
                    root.MouseMove(remove.TranslatePoint(new Point(12, 12), root)!.Value);
                    GuiHarness.Flush();
                    var button = remove.GetVisualDescendants().OfType<ContentPresenter>().First();
                    Assert.Equal(Color.Parse(dark ? "#F87171" : "#B91C1C"), ColorOf(button.Foreground));
                    Assert.Equal(Color.Parse(dark ? "#321E22" : "#FEF2F2"), ColorOf(button.Background));
                }
                input.IsDropDownOpen = false;
            }
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            var add = page.FindControl<Button>("AddConditionButton")!;
            add.BringIntoView();
            GuiHarness.Flush();
            var menu = ConditionList.Menu(page);
            menu.ShowAt(add);
            GuiHarness.Flush();
            var item = menu.Items.Cast<MenuItem>().First(x => x.IsEnabled);
            var outer = item.GetVisualAncestors().OfType<MenuFlyoutPresenter>().Single();
            Assert.Equal(new Thickness(24, 16, 24, 32), outer.Margin);
            Assert.False(outer.ClipToBounds);
            var border = outer.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "LayoutRoot");
            Assert.Equal(Color.Parse(dark ? "#222428" : "#FFFFFF"), ColorOf(border.Background));
            Assert.Equal(Color.Parse(dark ? "#2E3136" : "#E3E6EB"), ColorOf(border.BorderBrush));
            Assert.Equal(new Thickness(1), border.BorderThickness);
            Assert.Equal(new CornerRadius(8), border.CornerRadius);
            Assert.Equal(new Thickness(4), border.Padding);
            Assert.NotEqual(default, border.BoxShadow);
            var menuRoot = TopLevel.GetTopLevel(item)!;
            menuRoot.MouseMove(item.TranslatePoint(new Point(10, 10), menuRoot)!.Value);
            GuiHarness.Flush();
            var itemBorder = item.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "PART_LayoutRoot");
            Assert.Equal(new CornerRadius(6), itemBorder.CornerRadius);
            Assert.Equal(Color.Parse(dark ? "#26282C" : "#EEF0F3"), ColorOf(itemBorder.Background));
            menu.Hide();
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
