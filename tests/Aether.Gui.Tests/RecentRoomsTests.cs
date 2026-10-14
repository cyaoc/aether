using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class RecentRoomsTests
{
    [Fact]
    public Task Connected_room_is_saved_as_typed_and_prefilled_after_restart() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var started = new TaskCompletionSource();
        harness.RoomResponse = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        var viewModel = harness.CreateViewModel();
        try
        {
            Assert.Equal("", viewModel.RoomNumber);
            viewModel.RoomNumber = "006";
            viewModel.ConnectCommand.Execute(null);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(harness.LoadSettings().RecentRooms);
            viewModel.RoomNumber = "999";
            viewModel.Apply(new Connected());
            Assert.Equal(["006"], viewModel.RecentRooms);
            Assert.Equal(["006"], harness.LoadSettings().RecentRooms);
            var restarted = harness.CreateViewModel();
            Assert.Equal("006", restarted.RoomNumber);
            Assert.False(restarted.HasRoomConnection);
            Assert.False(restarted.ConnectCommand.IsRunning);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Successful_connections_move_to_front_without_duplicates_and_keep_five() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var started = new TaskCompletionSource();
        harness.RoomResponse = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        var viewModel = harness.CreateViewModel();
        try
        {
            foreach (var room in new[] { "1", "2", "3", "4", "5", "6", "3" })
            {
                started = new TaskCompletionSource();
                viewModel.RoomNumber = room;
                viewModel.ConnectCommand.Execute(null);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                viewModel.Apply(new Connected());
                viewModel.Apply(new Reconnecting());
                viewModel.Apply(new Connected());
                viewModel.ConnectCommand.Cancel();
                await viewModel.ConnectCommand.ExecutionTask!;
                Assert.Equal(room, viewModel.RoomNumber);
            }
            Assert.Equal(["3", "6", "5", "4", "2"], viewModel.RecentRooms);
            Assert.Equal(viewModel.RecentRooms, harness.LoadSettings().RecentRooms);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("999")]
    public Task Invalid_or_failed_connections_do_not_change_history(string room) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var settings = harness.LoadSettings();
        settings.RecentRooms = ["6"];
        settings.Save();
        harness.RoomResponse = (_, _) => Task.FromResult(GuiHarness.Json("""{"code":1,"message":"房间不存在"}"""));
        var viewModel = harness.CreateViewModel();
        try
        {
            viewModel.RoomNumber = room;
            await viewModel.ConnectCommand.ExecuteAsync(null);
            Assert.False(viewModel.HasRoomConnection);
            Assert.NotEmpty(viewModel.Message);
            Assert.Equal(["6"], viewModel.RecentRooms);
            Assert.Equal(["6"], harness.LoadSettings().RecentRooms);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Cancellation_before_connected_does_not_save_history() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var started = new TaskCompletionSource();
        harness.RoomResponse = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        var viewModel = harness.CreateViewModel();
        try
        {
            viewModel.RoomNumber = "6";
            viewModel.ConnectCommand.Execute(null);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.ConnectCommand.Cancel();
            viewModel.Apply(new Connected());
            await viewModel.ConnectCommand.ExecutionTask!;
            Assert.Empty(viewModel.RecentRooms);
            Assert.False(File.Exists(harness.SettingsFile));
            Assert.Equal("6", viewModel.RoomNumber);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Save_failure_keeps_the_connection_and_reports_the_error() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var started = new TaskCompletionSource();
        harness.RoomResponse = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        var viewModel = harness.CreateViewModel();
        Directory.CreateDirectory(harness.SettingsFile);
        try
        {
            viewModel.RoomNumber = "6";
            viewModel.ConnectCommand.Execute(null);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.Apply(new Connected());
            Assert.True(viewModel.IsConnected);
            Assert.True(viewModel.ConnectCommand.IsRunning);
            Assert.Equal(["6"], viewModel.RecentRooms);
            Assert.StartsWith("保存设置失败：", viewModel.Message);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Choosing_a_recent_room_only_fills_the_input_and_closed_enter_connects() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var settings = harness.LoadSettings();
        settings.RecentRooms = ["6", "42"];
        settings.Save();
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
            var input = Assert.Single(window.FindControl<StackPanel>("RoomInputPanel")!.GetVisualDescendants().OfType<ComboBox>());
            GuiHarness.Flush();
            Assert.True(input.IsEditable);
            Assert.Equal("6", input.Text);
            input.SelectedIndex = 1;
            GuiHarness.Flush();
            Assert.Equal("42", viewModel.RoomNumber);
            Assert.False(viewModel.HasRoomConnection);
            Assert.False(requested.Task.IsCompleted);
            var text = input.GetVisualDescendants().OfType<TextBox>().Single();
            text.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            var uri = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
}
