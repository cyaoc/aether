using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class RoomInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Selecting_from_the_open_list_never_connects(bool keyboard) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var settings = harness.LoadSettings();
        settings.RecentRooms = ["6", "42"];
        settings.Save();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            var input = window.FindControl<ComboBox>("RoomInput")!;
            GuiHarness.Flush();
            var arrow = input.TranslatePoint(new Point(input.Bounds.Width - 16, input.Bounds.Height / 2), window)!.Value;
            window.MouseDown(arrow, MouseButton.Left);
            window.MouseUp(arrow, MouseButton.Left);
            GuiHarness.Flush();
            Assert.True(input.IsDropDownOpen);
            if (keyboard)
            {
                var popup = TopLevel.GetTopLevel(input.ContainerFromIndex(0)!)!;
                popup.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                popup.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                popup.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                Assert.False(viewModel.HasRoomConnection);
                Assert.Equal("42", viewModel.RoomNumber);
                popup.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            }
            else
            {
                var item = input.ContainerFromIndex(1)!;
                var popup = TopLevel.GetTopLevel(item)!;
                var point = item.TranslatePoint(new Point(10, 10), popup)!.Value;
                popup.MouseDown(point, MouseButton.Left);
                popup.MouseUp(point, MouseButton.Left);
            }
            GuiHarness.Flush();
            Assert.False(input.IsDropDownOpen);
            Assert.Equal("42", viewModel.RoomNumber);
            Assert.Equal("42", input.Text);
            Assert.False(viewModel.HasRoomConnection);
            Assert.Null(viewModel.ConnectCommand.ExecutionTask);
            Assert.Equal(["6", "42"], harness.LoadSettings().RecentRooms);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Typing_a_new_room_connects_with_enter() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var requested = new TaskCompletionSource<Uri>();
        harness.RoomResponse = async (uri, token) =>
        {
            requested.TrySetResult(uri);
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            var input = window.FindControl<ComboBox>("RoomInput")!;
            GuiHarness.Flush();
            var text = input.GetVisualDescendants().OfType<TextBox>().Single();
            text.Focus();
            window.KeyTextInput("12345");
            Assert.Equal("12345", viewModel.RoomNumber);
            Assert.False(viewModel.HasRoomConnection);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Assert.Equal("?id=12345", (await requested.Task.WaitAsync(TimeSpan.FromSeconds(5))).Query);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Enter_with_no_selected_recent_room_only_closes_the_open_list(bool hasHistory) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var settings = harness.LoadSettings();
        settings.RecentRooms = hasHistory ? ["6"] : [];
        settings.Save();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            var input = window.FindControl<ComboBox>("RoomInput")!;
            GuiHarness.Flush();
            var text = input.GetVisualDescendants().OfType<TextBox>().Single();
            text.Focus();
            text.SelectAll();
            window.KeyTextInput("12345");
            input.IsDropDownOpen = true;
            GuiHarness.Flush();
            Assert.Equal(-1, input.SelectedIndex);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Assert.False(input.IsDropDownOpen);
            Assert.Equal("12345", viewModel.RoomNumber);
            Assert.Null(viewModel.ConnectCommand.ExecutionTask);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Editable_room_input_keeps_the_full_focus_border_surface_and_numeric_font(bool dark) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory)
        {
            DataContext = viewModel, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            var input = window.FindControl<ComboBox>("RoomInput")!;
            GuiHarness.Flush();
            Assert.Equal("房间号", AutomationProperties.GetName(input));
            Assert.Equal("房间号", input.PlaceholderText);
            var text = input.GetVisualDescendants().OfType<TextBox>().Single();
            var outline = input.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "Background");
            text.Focus();
            GuiHarness.Flush();
            Assert.Equal(input.Bounds.Width, outline.Bounds.Width);
            Assert.Equal(input.Bounds.Height, outline.Bounds.Height);
            Assert.Equal(Color.Parse(dark ? "#8DA2FB" : "#3358D4"), ColorOf(outline.BorderBrush));
            Assert.Equal(Color.Parse(dark ? "#1B1C1F" : "#FFFFFF"), ColorOf(outline.Background));
            Assert.Equal(Colors.Transparent, ColorOf(text.GetVisualDescendants().OfType<Border>()
                .Single(x => x.Name == "PART_BorderElement").Background));
            Assert.Equal(FontFeature.Parse("tnum"), Assert.Single(text.FontFeatures!));
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
