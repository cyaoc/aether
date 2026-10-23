using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class RoomInputTests
{
    [Fact]
    public Task Signed_room_number_is_rejected_before_connecting() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        await h.SaveCredentialAsync();
        var vm = h.CreateViewModel();
        var window = new MainWindow(h.DataDirectory) { DataContext = vm };
        window.Show();
        try
        {
            await vm.ReadAccountCommand.ExecutionTask!;
            EditorOf(window.FindControl<ComboBox>("RoomInput")!).Text = "+123";
            await vm.ConnectCommand.ExecuteAsync(null);
            GuiHarness.Flush();
            Assert.Equal("请输入有效的房间号（正整数）。", vm.Message);
            Assert.Contains(vm.Message, GuiHarness.VisibleText(window));
            Assert.False(vm.HasRoomConnection);
            Assert.Empty(h.RejectedRequests);
        }
        finally { await vm.ShutdownAsync(); window.Close(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public Task Delete_button_keeps_input_and_popup_without_selecting_its_row(int index) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.SaveRecentRooms("6", "42");
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            var input = window.FindControl<ComboBox>("RoomInput")!;
            GuiHarness.Flush();
            EditorOf(input).Focus();
            input.IsDropDownOpen = true;
            GuiHarness.Flush();
            var remove = input.ContainerFromIndex(index)!.GetVisualDescendants().OfType<Button>().Single();
            Assert.Equal($"从最近列表中删除 {(index == 0 ? "6" : "42")}", AutomationProperties.GetName(remove));
            var popup = TopLevel.GetTopLevel(remove)!;
            var point = remove.TranslatePoint(new Point(12, 12), popup)!.Value;
            popup.MouseDown(point, MouseButton.Left);
            popup.MouseUp(point, MouseButton.Left);
            GuiHarness.Flush();
            Assert.Equal("6", viewModel.RoomNumber);
            Assert.Equal("6", input.Text);
            Assert.True(input.IsDropDownOpen);
            Assert.Equal("", EditorOf(input).SelectedText);
            Assert.True(EditorOf(input).IsFocused);
            Assert.Equal(index == 0 ? new[] { "42" } : ["6"], viewModel.RecentRooms);
            Assert.Equal(viewModel.RecentRooms, harness.LoadSettings().RecentRooms);
            Assert.Null(viewModel.ConnectCommand.ExecutionTask);
            // Typing goes on at the end of the box, even after the room it shows was deleted.
            window.KeyTextInput("7");
            Assert.Equal("67", viewModel.RoomNumber);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Selecting_from_the_open_list_never_connects(bool keyboard) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.SaveRecentRooms("6", "42");
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
    public Task Choosing_a_recent_room_only_fills_the_input_and_closed_enter_connects() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.SaveRecentRooms("6", "42");
        var requested = harness.HoldRoomRequest();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            var input = window.FindControl<ComboBox>("RoomInput")!;
            GuiHarness.Flush();
            Assert.True(input.IsEditable);
            Assert.Equal("6", input.Text);
            input.SelectedIndex = 1;
            GuiHarness.Flush();
            Assert.Equal("42", viewModel.RoomNumber);
            Assert.False(viewModel.HasRoomConnection);
            Assert.False(requested.IsCompleted);
            EditorOf(input).Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            var uri = await requested.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("?id=42", uri.Query);
            viewModel.Apply(new Connected());
            viewModel.ConnectCommand.Cancel();
            await viewModel.ConnectCommand.ExecutionTask!;
            GuiHarness.Flush();
            Assert.Equal("42", input.Text);
            Assert.Equal(["42", "6"], input.Items.Cast<string>());
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Typing_a_new_room_connects_with_enter() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var requested = harness.HoldRoomRequest();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            var input = window.FindControl<ComboBox>("RoomInput")!;
            GuiHarness.Flush();
            var text = EditorOf(input);
            text.Focus();
            window.KeyTextInput("12345");
            Assert.Equal("12345", viewModel.RoomNumber);
            Assert.False(viewModel.HasRoomConnection);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Assert.Equal("?id=12345", (await requested.WaitAsync(TimeSpan.FromSeconds(5))).Query);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Enter_with_no_selected_recent_room_only_closes_the_open_list() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.SaveRecentRooms("6");
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            var input = window.FindControl<ComboBox>("RoomInput")!;
            GuiHarness.Flush();
            var text = EditorOf(input);
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

    [Fact]
    public Task Shift_delete_only_removes_history_while_open_and_plain_delete_edits_text() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.SaveRecentRooms("6", "42");
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            var input = window.FindControl<ComboBox>("RoomInput")!;
            GuiHarness.Flush();
            input.IsDropDownOpen = true;
            GuiHarness.Flush();
            window.KeyPress(Key.Delete, RawInputModifiers.Shift, PhysicalKey.Delete, null);
            GuiHarness.Flush();
            Assert.Equal(["42"], viewModel.RecentRooms);
            Assert.Equal("6", viewModel.RoomNumber);
            Assert.True(input.IsDropDownOpen);
            var text = EditorOf(input);
            text.SelectionStart = text.SelectionEnd = text.CaretIndex = 0;
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            Assert.Equal("", viewModel.RoomNumber);
            Assert.Equal(["42"], viewModel.RecentRooms);
            input.IsDropDownOpen = false;
            input.SelectedIndex = 0;
            window.KeyPress(Key.Delete, RawInputModifiers.Shift, PhysicalKey.Delete, null);
            Assert.Equal(["42"], viewModel.RecentRooms);
            Assert.Equal(["42"], harness.LoadSettings().RecentRooms);
            Assert.Null(viewModel.ConnectCommand.ExecutionTask);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    public Task Empty_history_hides_the_arrow_and_cannot_open_even_after_deleting_the_last_room(int count) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.SaveRecentRooms([.. new[] { "6", "42", "123", "456", "789" }.Take(count)]);
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            var input = window.FindControl<ComboBox>("RoomInput")!;
            GuiHarness.Flush();
            if (count > 0)
            {
                input.IsDropDownOpen = true;
                GuiHarness.Flush();
                if (count == 1)
                    window.KeyPress(Key.Delete, RawInputModifiers.Shift, PhysicalKey.Delete, null);
                else
                    foreach (var room in viewModel.RecentRooms.ToArray()) viewModel.RemoveRecentRoomCommand.Execute(room);
                GuiHarness.Flush();
                Assert.Equal("6", input.Text);
                Assert.Equal("", EditorOf(input).SelectedText);
                var presenter = EditorOf(input).GetVisualDescendants().OfType<TextPresenter>().Single();
                Assert.Equal(presenter.SelectionStart, presenter.SelectionEnd);
                Assert.Empty(harness.LoadSettings().RecentRooms);
            }
            Assert.Empty(viewModel.RecentRooms);
            Assert.False(input.IsDropDownOpen);
            Assert.False(input.GetVisualDescendants().OfType<PathIcon>().Single(x => x.Name == "DropDownGlyph").IsVisible);
            var point = input.TranslatePoint(new Point(input.Bounds.Width - 16, input.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            GuiHarness.Flush();
            Assert.False(input.IsDropDownOpen);
            EditorOf(input).Focus();
            window.KeyPress(Key.F4, RawInputModifiers.None, PhysicalKey.F4, null);
            GuiHarness.Flush();
            Assert.False(input.IsDropDownOpen);
            // Windows closes the window on Alt+F4 only while the key event stays unhandled.
            var altF4Unhandled = false;
            window.AddHandler(InputElement.KeyDownEvent, (_, e) => altF4Unhandled |= e.Key == Key.F4, RoutingStrategies.Bubble);
            window.KeyPress(Key.F4, RawInputModifiers.Alt, PhysicalKey.F4, null);
            Assert.True(altF4Unhandled);
            Assert.Null(viewModel.ConnectCommand.ExecutionTask);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Opening_and_closing_clears_text_selection_but_tab_focus_keeps_select_all() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.SaveRecentRooms("12345", "42");
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            var input = window.FindControl<ComboBox>("RoomInput")!;
            GuiHarness.Flush();
            var text = EditorOf(input);
            text.Focus(NavigationMethod.Tab);
            Assert.Equal("12345", text.SelectedText);
            text.Focus();
            // Clicking ▾ and then a row both run ComboBox's SelectAll on focus.
            var arrow = input.TranslatePoint(new Point(input.Bounds.Width - 16, input.Bounds.Height / 2), window)!.Value;
            window.MouseDown(arrow, MouseButton.Left);
            window.MouseUp(arrow, MouseButton.Left);
            GuiHarness.Flush();
            Assert.True(input.IsDropDownOpen);
            Assert.Equal("", text.SelectedText);
            Assert.Equal(5, text.CaretIndex);
            var row = input.ContainerFromIndex(1)!;
            var popup = TopLevel.GetTopLevel(row)!;
            var point = row.TranslatePoint(new Point(10, 16), popup)!.Value;
            popup.MouseDown(point, MouseButton.Left);
            popup.MouseUp(point, MouseButton.Left);
            GuiHarness.Flush();
            Assert.False(input.IsDropDownOpen);
            Assert.Equal("42", text.Text);
            Assert.Equal("", text.SelectedText);
            Assert.Equal(2, text.CaretIndex);
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
            var text = EditorOf(input);
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

    private static TextBox EditorOf(ComboBox input) => input.GetVisualDescendants().OfType<TextBox>().Single();

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
