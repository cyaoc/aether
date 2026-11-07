using Aether.Core;
using Aether.Gui.Tests.Support;

namespace Aether.Gui.Tests;

public sealed class RecentRoomsTests
{
    [Theory]
    [InlineData("6", new[] { "42", "123" })]
    [InlineData("42", new[] { "6", "123" })]
    public async Task Deleting_a_recent_room_saves_the_remaining_rooms(string room, string[] expected)
    {
        using var harness = new GuiHarness();
        harness.SaveRecentRooms("6", "42", "123");
        var viewModel = harness.CreateViewModel();
        viewModel.RemoveRecentRoomCommand.Execute(room);
        Assert.Equal(expected, viewModel.RecentRooms);
        Assert.Equal(expected, harness.LoadGuiState().RecentRooms);
        await viewModel.ShutdownAsync();
    }

    [Fact]
    public async Task Deleting_reports_a_save_failure()
    {
        using var harness = new GuiHarness();
        harness.SaveRecentRooms("6", "42");
        var viewModel = harness.CreateViewModel();
        harness.BreakGuiState();
        viewModel.RemoveRecentRoomCommand.Execute("6");
        Assert.Equal(["42"], viewModel.RecentRooms);
        Assert.StartsWith("保存最近的房间号和“只保留最近 1000 条”失败：", viewModel.Message);
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
            Assert.Empty(harness.LoadGuiState().RecentRooms);
            viewModel.RoomNumber = "999";
            viewModel.Apply(new Connected(7734200, 24680));
            Assert.Equal(["006"], viewModel.RecentRooms);
            Assert.Equal(["006"], harness.LoadGuiState().RecentRooms);
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
                viewModel.Apply(new Connected(7734200, 24680));
                viewModel.Apply(new Reconnecting());
                viewModel.Apply(new Connected(7734200, 24680));
                viewModel.ConnectCommand.Cancel();
                await viewModel.ConnectCommand.ExecutionTask!;
                Assert.Equal(room, viewModel.RoomNumber);
            }
            Assert.Equal(["3", "6", "5", "4", "2"], viewModel.RecentRooms);
            Assert.Equal(viewModel.RecentRooms, harness.LoadGuiState().RecentRooms);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Reconnecting_to_the_latest_room_does_not_rewrite_the_recent_rooms() => HeadlessApp.Session.Dispatch(async () =>
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
            viewModel.Apply(new Connected(7734200, 24680));
            // A write would now fail and say so.
            harness.BreakGuiState();
            viewModel.Apply(new Reconnecting());
            viewModel.Apply(new Connected(7734200, 24680));
            Assert.Equal("", viewModel.Message);
            Assert.Equal(["6"], viewModel.RecentRooms);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Saving_history_keeps_the_danmaku_switch() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.Client.GuiState.Save(new(false, []));
        var requested = harness.HoldRoomRequest();
        var viewModel = harness.CreateViewModel();
        try
        {
            viewModel.RoomNumber = "6";
            viewModel.ConnectCommand.Execute(null);
            await requested.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.Apply(new Connected(7734200, 24680));
            var reloaded = harness.LoadGuiState();
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
            Assert.Equal(["6"], harness.LoadGuiState().RecentRooms);
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
            viewModel.Apply(new Connected(7734200, 24680));
            await viewModel.ConnectCommand.ExecutionTask!;
            Assert.Empty(viewModel.RecentRooms);
            Assert.Empty(harness.LoadGuiState().RecentRooms);
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
        harness.BreakGuiState();
        try
        {
            viewModel.RoomNumber = "6";
            viewModel.ConnectCommand.Execute(null);
            await requested.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.Apply(new Connected(7734200, 24680));
            Assert.True(viewModel.IsConnected);
            Assert.True(viewModel.ConnectCommand.IsRunning);
            Assert.Equal(["6"], viewModel.RecentRooms);
            Assert.StartsWith("保存最近的房间号和“只保留最近 1000 条”失败：", viewModel.Message);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
