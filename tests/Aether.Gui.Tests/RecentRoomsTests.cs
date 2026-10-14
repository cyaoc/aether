using Aether.Core;
using Aether.Gui.Tests.Support;

namespace Aether.Gui.Tests;

public sealed class RecentRoomsTests
{
    [Theory]
    [InlineData("6", new[] { "42", "123" })]
    [InlineData("42", new[] { "6", "123" })]
    public async Task Deleting_a_recent_room_saves_the_remaining_rooms_and_keeps_the_input(string room, string[] expected)
    {
        using var harness = new GuiHarness();
        harness.SaveRecentRooms("6", "42", "123");
        var viewModel = harness.CreateViewModel();
        viewModel.RemoveRecentRoomCommand.Execute(room);
        Assert.Equal("6", viewModel.RoomNumber);
        Assert.Equal(expected, viewModel.RecentRooms);
        Assert.Equal(expected, harness.LoadSettings().RecentRooms);
        await viewModel.ShutdownAsync();
    }

    [Fact]
    public async Task Deleting_reports_a_save_failure_and_keeps_the_input()
    {
        using var harness = new GuiHarness();
        harness.SaveRecentRooms("6", "42");
        var viewModel = harness.CreateViewModel();
        File.Delete(harness.SettingsFile);
        Directory.CreateDirectory(harness.SettingsFile);
        viewModel.RemoveRecentRoomCommand.Execute("6");
        Assert.Equal("6", viewModel.RoomNumber);
        Assert.Equal(["42"], viewModel.RecentRooms);
        Assert.StartsWith("保存 GUI 设置失败：", viewModel.Message);
        await viewModel.ShutdownAsync();
    }

    [Fact]
    public Task Connected_room_is_saved_as_typed_and_prefilled_after_restart() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var requested = harness.HoldRoomRequest();
        var viewModel = harness.CreateViewModel();
        try
        {
            Assert.Equal("", viewModel.RoomNumber);
            viewModel.RoomNumber = "006";
            viewModel.ConnectCommand.Execute(null);
            await requested.WaitAsync(TimeSpan.FromSeconds(5));
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
        var viewModel = harness.CreateViewModel();
        try
        {
            foreach (var room in new[] { "1", "2", "3", "4", "5", "6", "3" })
            {
                var requested = harness.HoldRoomRequest();
                viewModel.RoomNumber = room;
                viewModel.ConnectCommand.Execute(null);
                await requested.WaitAsync(TimeSpan.FromSeconds(5));
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

    [Fact]
    public Task Reconnecting_to_the_latest_room_does_not_rewrite_gui_yml() => HeadlessApp.Session.Dispatch(async () =>
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
            viewModel.Apply(new Connected());
            File.Delete(harness.SettingsFile);
            viewModel.Apply(new Reconnecting());
            viewModel.Apply(new Connected());
            Assert.False(File.Exists(harness.SettingsFile));
            Assert.Equal(["6"], viewModel.RecentRooms);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Saving_history_keeps_a_setting_next_to_a_broken_recent_rooms_value() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        File.WriteAllText(harness.SettingsFile, "KeepRecentDanmaku: false\nRecentRooms: {a: 1}");
        var requested = harness.HoldRoomRequest();
        var viewModel = harness.CreateViewModel();
        try
        {
            viewModel.RoomNumber = "6";
            viewModel.ConnectCommand.Execute(null);
            await requested.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.Apply(new Connected());
            var reloaded = harness.LoadSettings();
            Assert.False(reloaded.KeepRecentDanmaku);
            Assert.Equal(["6"], reloaded.RecentRooms);
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
        harness.SaveRecentRooms("6");
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
        var requested = harness.HoldRoomRequest();
        var viewModel = harness.CreateViewModel();
        try
        {
            viewModel.RoomNumber = "6";
            viewModel.ConnectCommand.Execute(null);
            await requested.WaitAsync(TimeSpan.FromSeconds(5));
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
        var requested = harness.HoldRoomRequest();
        var viewModel = harness.CreateViewModel();
        Directory.CreateDirectory(harness.SettingsFile);
        try
        {
            viewModel.RoomNumber = "6";
            viewModel.ConnectCommand.Execute(null);
            await requested.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.Apply(new Connected());
            Assert.True(viewModel.IsConnected);
            Assert.True(viewModel.ConnectCommand.IsRunning);
            Assert.Equal(["6"], viewModel.RecentRooms);
            Assert.StartsWith("保存 GUI 设置失败：", viewModel.Message);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
