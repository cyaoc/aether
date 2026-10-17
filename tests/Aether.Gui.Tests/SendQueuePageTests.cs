using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class SendQueuePageTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 12, 14, 30, 0, TimeSpan.FromHours(8));

    [Theory]
    [InlineData(1200, "momo_7")]
    [InlineData(1040, "momo_7")]
    [InlineData(1200, "观众")]
    [InlineData(1040, "观众")]
    [InlineData(1200, "观众bili")]
    [InlineData(1040, "观众bili")]
    public Task Activity_rows_align_with_their_headings_and_leave_space_at_both_ends(int width, string nickname) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, Width = width };
        window.Show();
        try
        {
            viewModel.Apply(Update(1, SendQueueItemKind.Welcome) with { Nickname = nickname, Text = "欢迎回来" });
            viewModel.Apply(Update(2, SendQueueItemKind.Welcome, SendQueueItemState.Sent) with
            {
                Nickname = nickname, Text = "欢迎回来", WelcomeCondition = WelcomeCondition.Guard, WelcomeValue = "舰长"
            });
            viewModel.Apply(Update(3, SendQueueItemKind.BlindBoxQuery, SendQueueItemState.Sent) with { Nickname = nickname });
            viewModel.Apply(Update(4, SendQueueItemKind.BlindBoxAnnouncement, SendQueueItemState.Sent) with { Nickname = nickname });
            foreach (var (index, listName, heading, detail) in new[]
            {
                (5, "PendingSendsList", "待发", "进场欢迎"), (5, "RecentSendsList", "最近发送", "盲盒播报"),
                (2, "RecentWelcomesList", "最近欢迎", "舰长"), (1, "RecentQueriesAndAnnouncementsList", "最近回复和播报", "播报")
            })
            {
                var (page, list, header, time) = FirstRecordHeader(window, index, listName, $"{nickname} {detail}");
                var title = page.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == heading);
                var count = Assert.IsType<Grid>(title.Parent).GetVisualDescendants().OfType<TextBlock>().Last(x => x.IsEffectivelyVisible);
                var first = list.ContainerFromIndex(0)!;
                Assert.Single(header.TextLayout.TextLines);
                Assert.Equal(title.TranslatePoint(default, window)!.Value.X, header.TranslatePoint(default, window)!.Value.X);
                Assert.Equal(count.TranslatePoint(new Point(count.Bounds.Width, 0), window)!.Value.X,
                    time.TranslatePoint(new Point(time.Bounds.Width, 0), window)!.Value.X);
                Assert.True(first.TranslatePoint(default, list)!.Value.Y >= 8);
                // The separator shares the text edges, and only non-final rows have a bottom stroke.
                for (var i = 0; i < list.ItemCount; i++)
                {
                    var item = list.ContainerFromIndex(i)!;
                    var border = item.GetVisualDescendants().OfType<Border>().Single(x => x.Child is StackPanel);
                    Assert.Equal(header.TranslatePoint(default, window)!.Value.X, border.TranslatePoint(default, window)!.Value.X);
                    Assert.Equal(time.TranslatePoint(new Point(time.Bounds.Width, 0), window)!.Value.X,
                        border.TranslatePoint(new Point(border.Bounds.Width, 0), window)!.Value.X);
                    var content = Assert.IsType<StackPanel>(border.Child);
                    var contentBottom = content.TranslatePoint(new Point(0, content.Bounds.Height), border)!.Value.Y;
                    Assert.Equal(i == list.ItemCount - 1 ? 10 : 11, border.Bounds.Height - contentBottom);
                }
                var viewer = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                viewer.ScrollToEnd();
                GuiHarness.Flush();
                var last = list.ContainerFromIndex(list.ItemCount - 1)!;
                Assert.True(list.Bounds.Height - last.TranslatePoint(new Point(0, last.Bounds.Height), list)!.Value.Y >= 12);
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(1040, false)]
    [InlineData(1040, true)]
    [InlineData(1200, false)]
    [InlineData(1200, true)]
    public Task Long_viewer_nicknames_wrap_in_full_beside_the_time(int width, bool dark) => HeadlessApp.Session.Dispatch(async () =>
    {
        // Doubled so it still wraps in the wide pending column at 1200 with Skia fonts.
        const string nickname = GuiHarness.LongEnglishNickname + GuiHarness.LongEnglishNickname;
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory)
        {
            DataContext = viewModel, Width = width, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            viewModel.Apply(Update(1, SendQueueItemKind.Welcome) with { Nickname = nickname });
            viewModel.Apply(Update(2, SendQueueItemKind.Welcome, SendQueueItemState.Sent) with
            {
                Nickname = nickname, WelcomeCondition = WelcomeCondition.FanClubLevel, WelcomeValue = "12"
            });
            viewModel.Apply(Update(3, SendQueueItemKind.BlindBoxAnnouncement, SendQueueItemState.Sent) with { Nickname = nickname });
            foreach (var (index, listName, detail) in new[]
            {
                (5, "PendingSendsList", "进场欢迎"), (5, "RecentSendsList", "盲盒播报"),
                (2, "RecentWelcomesList", "粉丝团 12"), (1, "RecentQueriesAndAnnouncementsList", "播报")
            })
            {
                var (_, _, header, time) = FirstRecordHeader(window, index, listName, $"{nickname} {detail}");
                var lines = header.TextLayout.TextLines;
                Assert.True(lines.Count > 1);
                Assert.True(header.Bounds.Height > lines[0].Height);
                Assert.All(lines, line =>
                {
                    Assert.False(line.HasCollapsed);
                    Assert.True(line.Width <= header.Bounds.Width + 0.5);
                });
                Assert.True(header.TranslatePoint(new Point(header.Bounds.Width, 0), window)!.Value.X <= time.TranslatePoint(default, window)!.Value.X - 8);
                Assert.Null(ToolTip.GetTip(header));
                var runs = header.Inlines!.OfType<Run>().ToArray();
                Assert.Equal(FontWeight.SemiBold, runs.First().FontWeight);
                Assert.Equal(FontWeight.Normal, runs.Last().FontWeight);
                Assert.All(runs, run => Assert.Equal(13, run.FontSize));
                Assert.Equal(Color.Parse(dark ? "#E8EAED" : "#16181D"), ColorOf(runs.First().Foreground));
                Assert.Equal(Color.Parse(dark ? "#9097A3" : "#5B6270"), ColorOf(runs.Last().Foreground));
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Only_the_actual_last_record_loses_its_separator_after_scrolling_and_completion() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, Width = 1040, Height = 600 };
        window.Show();
        try
        {
            for (var id = 1; id <= 100; id++) viewModel.Apply(Update(id, SendQueueItemKind.BlindBoxQuery));
            window.Navigation.SelectedIndex = 5;
            GuiHarness.Flush();
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            var list = page.FindControl<ListBox>("PendingSendsList")!;
            var viewer = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            for (var remaining = 100; remaining >= 99; remaining--)
            {
                viewer.ScrollToEnd();
                GuiHarness.Flush();
                AssertSeparator(Assert.IsType<ListBoxItem>(list.ContainerFromIndex(remaining - 1)), last: true);
                AssertSeparator(Assert.IsType<ListBoxItem>(list.ContainerFromIndex(remaining - 2)), last: false);
                viewer.ScrollToHome();
                GuiHarness.Flush();
                var realized = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
                Assert.InRange(realized.Length, 1, remaining - 1);
                Assert.All(realized, item => AssertSeparator(item, last: false));
                viewModel.Apply(Update(remaining, SendQueueItemKind.BlindBoxQuery, SendQueueItemState.Sent));
                GuiHarness.Flush();
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;

        static void AssertSeparator(ListBoxItem item, bool last)
        {
            var border = item.GetVisualDescendants().OfType<Border>().Single(x => x.Child is StackPanel);
            var content = Assert.IsType<StackPanel>(border.Child);
            var bottom = content.TranslatePoint(new Point(0, content.Bounds.Height), border)!.Value.Y;
            Assert.Equal(last ? 10 : 11, border.Bounds.Height - bottom);
        }
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(1040, 600, 0)]
    [InlineData(1040, 600, 2)]
    [InlineData(1040, 600, 30)]
    [InlineData(1200, 720, 30)]
    public Task The_first_two_pending_records_stay_whole_and_longer_queues_take_at_most_half_when_it_fits(int width, int height, int count) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, Width = width, Height = height };
        window.Show();
        try
        {
            for (var id = 1; id <= count; id++)
                viewModel.Apply(Update(id, SendQueueItemKind.BlindBoxQuery) with { Text = "投入52电池 亏12.6电池" });
            window.Navigation.SelectedIndex = 5;
            GuiHarness.Flush();
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            var activity = page.FindControl<Grid>("SendActivity")!;
            var list = page.FindControl<ListBox>("PendingSendsList")!;
            var recent = activity.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "最近发送");
            if (count == 0)
            {
                var empty = activity.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "暂无待发弹幕");
                var recentTop = recent.TranslatePoint(default, activity)!.Value.Y;
                Assert.InRange(recentTop - empty.TranslatePoint(new Point(0, empty.Bounds.Height), activity)!.Value.Y, 12, 26);
            }
            else if (count == 2) AssertFirstTwoWhole(trailingSpace: 12);
            else
            {
                AssertFirstTwoWhole(trailingSpace: 0);
                var viewer = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                Assert.True(viewer.Extent.Height > viewer.Viewport.Height);
                // At the default size half the area already holds two records, so the queue stops there.
                if (height == 720) AssertAtMostHalf();
                for (var id = 1; id <= count - 2; id++)
                    viewModel.Apply(Update(id, SendQueueItemKind.BlindBoxQuery, SendQueueItemState.Sent));
                GuiHarness.Flush();
                AssertFirstTwoWhole(trailingSpace: 12);
                // Going from two to three records keeps both visible instead of shrinking the area.
                viewModel.Apply(Update(count + 1, SendQueueItemKind.Welcome));
                GuiHarness.Flush();
                AssertFirstTwoWhole(trailingSpace: 0);
                if (height == 720) AssertAtMostHalf();
            }

            void AssertAtMostHalf() =>
                Assert.True(list.TranslatePoint(new Point(0, list.Bounds.Height), activity)!.Value.Y <= activity.Bounds.Height / 2 + 1);

            void AssertFirstTwoWhole(double trailingSpace)
            {
                var first = list.ContainerFromIndex(0)!;
                var second = list.ContainerFromIndex(1)!;
                Assert.True(first.TranslatePoint(default, list)!.Value.Y >= 8);
                var bottom = second.TranslatePoint(new Point(0, second.Bounds.Height), list)!.Value.Y;
                Assert.True(bottom <= list.Bounds.Height - trailingSpace + 0.5, $"Second row bottom {bottom}, list height {list.Bounds.Height}");
                Assert.True(recent.TranslatePoint(default, activity)!.Value.Y > second.TranslatePoint(new Point(0, second.Bounds.Height), activity)!.Value.Y);
                Assert.True(recent.TranslatePoint(new Point(0, recent.Bounds.Height), activity)!.Value.Y < activity.Bounds.Height);
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Updates_keep_the_same_item_and_advance_waiting_positions() => HeadlessApp.Session.Dispatch(() =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        var first = Update(1, SendQueueItemKind.Welcome) with { Text = "欢迎回来", WelcomeCondition = WelcomeCondition.Uid };
        var second = Update(2, SendQueueItemKind.BlindBoxQuery);
        viewModel.Apply(first);
        viewModel.Apply(second);
        var item = viewModel.PendingSends[0];
        Assert.Equal("排队中 · 第 1 条", item.Status);
        Assert.Equal("排队中 · 第 2 条", viewModel.PendingSends[1].Status);
        Assert.Equal("欢迎回来", item.Text);

        viewModel.Apply(first with { State = SendQueueItemState.Sending });
        Assert.Same(item, viewModel.PendingSends[0]);
        Assert.Equal("发送中", item.Status);
        Assert.Equal("排队中 · 第 2 条", viewModel.PendingSends[1].Status);
        viewModel.Apply(first with { State = SendQueueItemState.WaitingToRetry, Text = null, RetrySeconds = 5 });
        Assert.Equal("被限流，5 秒后重试", item.Status);
        Assert.Equal("欢迎回来", item.Text);
        viewModel.Apply(first with { State = SendQueueItemState.Sending });
        viewModel.Apply(first with { State = SendQueueItemState.Sent, OccurredAt = OccurredAt.AddSeconds(8) });
        Assert.Same(item, Assert.Single(viewModel.RecentSends));
        Assert.Equal("已发送 14:30:08", item.Status);
        Assert.True(item.IsSent);
        Assert.False(item.IsUnsent);
        Assert.Equal("排队中 · 第 1 条", Assert.Single(viewModel.PendingSends).Status);
        Assert.Same(item, Assert.Single(viewModel.RecentWelcomes));
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Each_page_keeps_its_latest_hundred_finished_items_and_every_unfinished_item() => HeadlessApp.Session.Dispatch(() =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        for (var id = 1; id <= 110; id++) viewModel.Apply(Update(id, SendQueueItemKind.Welcome));
        for (var id = 111; id <= 220; id++) viewModel.Apply(Update(id, SendQueueItemKind.Welcome, SendQueueItemState.NotQueued));
        for (var id = 221; id <= 330; id++) viewModel.Apply(Update(id, SendQueueItemKind.BlindBoxQuery, SendQueueItemState.Sent));
        for (var id = 331; id <= 440; id++) viewModel.Apply(Update(id, SendQueueItemKind.BlindBoxAnnouncement));

        Assert.Equal(220, viewModel.PendingSends.Count);
        Assert.Equal(100, viewModel.RecentSends.Count);
        Assert.Equal(330, viewModel.RecentSends[0].Id);
        Assert.Equal(231, viewModel.RecentSends[^1].Id);
        Assert.Equal(210, viewModel.RecentWelcomes.Count);
        Assert.Equal(220, viewModel.RecentWelcomes[0].Id);
        Assert.DoesNotContain(viewModel.RecentWelcomes, item => item.Id is >= 111 and <= 120);
        Assert.Equal(210, viewModel.RecentQueriesAndAnnouncements.Count);
        Assert.Equal(440, viewModel.RecentQueriesAndAnnouncements[0].Id);
        Assert.Equal(231, viewModel.RecentQueriesAndAnnouncements[^1].Id);
        Assert.Equal(220, viewModel.WelcomeCount);
        viewModel.ConnectionState = RoomConnectionState.Connected;
        viewModel.ConnectionState = RoomConnectionState.Disconnected;
        Assert.Empty(viewModel.PendingSends);
        Assert.Equal(100, viewModel.RecentSends.Count);
        Assert.Equal(100, viewModel.RecentWelcomes.Count);
        Assert.Equal(110, viewModel.RecentWelcomes[0].Id); // The items that just ended outlast those that finished earlier.
        Assert.Equal(100, viewModel.RecentQueriesAndAnnouncements.Count);
        Assert.Equal(220, viewModel.WelcomeCount);
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Connection_end_finishes_pending_items_and_a_new_room_connection_clears_history(bool fails) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var started = new TaskCompletionSource();
        var end = new TaskCompletionSource();
        harness.RoomResponse = async (_, token) =>
        {
            started.TrySetResult();
            await end.Task.WaitAsync(token);
            throw new IOException("测试连接失败");
        };
        var viewModel = harness.CreateViewModel();
        viewModel.RoomNumber = "6";
        try
        {
            viewModel.ConnectCommand.Execute(null);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.Apply(Update(1, SendQueueItemKind.Welcome));
            viewModel.Apply(Update(2, SendQueueItemKind.BlindBoxQuery, SendQueueItemState.Sending));
            viewModel.Apply(Update(3, SendQueueItemKind.BlindBoxAnnouncement, SendQueueItemState.WaitingToRetry));
            viewModel.Apply(Update(4, SendQueueItemKind.Welcome, SendQueueItemState.Sent));
            viewModel.Apply(new Reconnecting());
            Assert.Equal(3, viewModel.PendingSends.Count); // Automatic reconnect keeps the same room connection.
            if (fails) end.SetResult();
            else viewModel.ConnectCommand.Cancel();
            await viewModel.ConnectCommand.ExecutionTask!;

            Assert.Empty(viewModel.PendingSends);
            Assert.Equal(4, viewModel.RecentSends.Count);
            Assert.All(viewModel.RecentSends.Where(item => item.Id != 4), item =>
            {
                Assert.Equal("连接结束，没发出", item.Status);
                Assert.True(item.IsUnsent);
            });
            Assert.Equal("已发送 14:30:00", viewModel.RecentSends.Single(item => item.Id == 4).Status);

            viewModel.ConnectCommand.Execute(null);
            Assert.Empty(viewModel.PendingSends);
            Assert.Empty(viewModel.RecentSends);
            Assert.Empty(viewModel.RecentWelcomes);
            Assert.Empty(viewModel.RecentQueriesAndAnnouncements);
            Assert.Equal(0, viewModel.WelcomeCount);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(WelcomeCondition.Uid, null, "指定 UID")]
    [InlineData(WelcomeCondition.FanClubLevel, "12", "粉丝团 12")]
    [InlineData(WelcomeCondition.Guard, "舰长", "舰长")]
    [InlineData(WelcomeCondition.Guard, "提督", "提督")]
    [InlineData(WelcomeCondition.Guard, "总督", "总督")]
    [InlineData(WelcomeCondition.WealthLevel, "34", "荣耀 34")]
    public Task Rejected_welcomes_show_the_matched_condition_and_reason(WelcomeCondition condition, string? value, string expected) =>
        HeadlessApp.Session.Dispatch(() =>
        {
            using var harness = new GuiHarness();
            var viewModel = harness.CreateViewModel();
            viewModel.Apply(Update(1, SendQueueItemKind.Welcome, SendQueueItemState.NotQueued) with
            {
                WelcomeCondition = condition, WelcomeValue = value, Text = "很长的欢迎语", Reason = "欢迎语超过弹幕字数上限 20"
            });
            var item = Assert.Single(viewModel.RecentWelcomes);
            Assert.Equal(expected, item.Detail);
            Assert.Equal("很长的欢迎语", item.Text);
            Assert.Equal("没发出：欢迎语超过弹幕字数上限 20", item.Status);
            Assert.True(item.IsUnsent);
            Assert.Empty(viewModel.PendingSends);
            Assert.Empty(viewModel.RecentQueriesAndAnnouncements);
            Assert.Same(item, Assert.Single(viewModel.RecentSends));
            Assert.Equal(1, viewModel.WelcomeCount);
            return true;
        }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false, 1200)]
    [InlineData(true, 1200)]
    [InlineData(false, 1040)]
    [InlineData(true, 1040)]
    public Task Pages_show_their_own_items_and_the_sidebar_shows_the_pending_count(bool dark, int width) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory)
        {
            DataContext = viewModel, Width = width, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            var badge = window.FindControl<TextBlock>("PendingSendCount");
            Assert.NotNull(badge);
            Assert.False(badge.IsVisible);
            var welcome = Update(1, SendQueueItemKind.Welcome) with { Text = "欢迎回来", WelcomeCondition = WelcomeCondition.Uid };
            var query = Update(2, SendQueueItemKind.BlindBoxQuery);
            var announcement = Update(3, SendQueueItemKind.BlindBoxAnnouncement);
            viewModel.Apply(welcome);
            viewModel.Apply(query);
            viewModel.Apply(announcement);
            Assert.Equal("3 条待发", badge.Text);
            Assert.True(badge.IsVisible);
            var navigation = window.FindControl<ListBox>("Navigation")!;
            navigation.SelectedIndex = 2;
            GuiHarness.Flush();
            Assert.Contains("最近欢迎", GuiHarness.VisibleText(window));
            Assert.Contains("共 1 次", GuiHarness.VisibleText(window));
            Assert.Contains("观众1 指定 UID", GuiHarness.VisibleText(window));
            Assert.Contains("欢迎回来", GuiHarness.VisibleText(window));
            Assert.DoesNotContain(GuiHarness.VisibleText(window), text => text?.StartsWith("观众2") == true);
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(control => control.IsEffectivelyVisible);
            var activity = page.FindControl<Border>("RecentActivity")!;
            var activityOrigin = activity.TranslatePoint(new Point(), page)!.Value;
            var save = page.FindControl<Button>("SaveButton")!;
            var saveOrigin = save.TranslatePoint(new Point(), page)!.Value;
            Assert.Equal(300, activity.Bounds.Width);
            Assert.True(activityOrigin.X >= saveOrigin.X + save.Bounds.Width);
            Assert.True(activityOrigin.X + activity.Bounds.Width <= page.Bounds.Width);

            navigation.SelectedIndex = 1;
            GuiHarness.Flush();
            Assert.Contains("最近回复和播报", GuiHarness.VisibleText(window));
            Assert.Contains("观众2 查询", GuiHarness.VisibleText(window));
            Assert.Contains("观众3 播报", GuiHarness.VisibleText(window));
            Assert.Equal(2, GuiHarness.VisibleText(window).Count(text => text == "发送时计算金额"));
            Assert.DoesNotContain(GuiHarness.VisibleText(window), text => text?.StartsWith("观众1") == true);
            Assert.Equal(new long[] { 3, 2 }, viewModel.RecentQueriesAndAnnouncements.Select(item => item.Id));

            viewModel.Apply(welcome with { State = SendQueueItemState.Sent });
            viewModel.Apply(query with { State = SendQueueItemState.Sending, Text = "今日赚 10 电池" });
            GuiHarness.Flush();
            Assert.Contains("今日赚 10 电池", GuiHarness.VisibleText(window));
            viewModel.Apply(query with { State = SendQueueItemState.WaitingToRetry, RetrySeconds = 5 });
            Assert.Equal("今日赚 10 电池", viewModel.RecentQueriesAndAnnouncements[1].Text);
            viewModel.Apply(query with { State = SendQueueItemState.Sending, Text = "今日赚 20 电池" });
            viewModel.Apply(query with { State = SendQueueItemState.Discarded, Reason = "发送被拒绝" });

            navigation.SelectedIndex = 5;
            GuiHarness.Flush();
            Assert.Contains("待发", GuiHarness.VisibleText(window));
            Assert.Contains("最近发送", GuiHarness.VisibleText(window));
            Assert.Contains("观众3 盲盒播报", GuiHarness.VisibleText(window));
            Assert.Contains("观众2 盲盒查询", GuiHarness.VisibleText(window));
            Assert.Contains("观众1 进场欢迎", GuiHarness.VisibleText(window));
            Assert.Contains("今日赚 20 电池", GuiHarness.VisibleText(window));
            Assert.Contains("没发出：发送被拒绝", GuiHarness.VisibleText(window));
            Assert.DoesNotContain("发送时计算金额", GuiHarness.VisibleText(window));
            Assert.Equal("1 条待发", badge.Text);
            var statuses = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(control => control.IsEffectivelyVisible && control.Classes.Contains("sendStatus")).ToArray();
            var pending = Assert.Single(statuses, control => control.Text == "排队中 · 第 1 条");
            var sent = Assert.Single(statuses, control => control.Text == "已发送 14:30:00");
            var unsent = Assert.Single(statuses, control => control.Text == "没发出：发送被拒绝");
            var colors = new[] { pending, sent, unsent }.Select(control => ColorOf(control.Foreground)).ToArray();
            Assert.Equal(3, colors.Distinct().Count());
            Assert.DoesNotContain(Colors.Transparent, colors);
            viewModel.Apply(announcement with { State = SendQueueItemState.Sent, Text = "本轮赚 30 电池" });
            Assert.False(badge.IsVisible);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    /// <summary>Shows a page and finds the header and time of the first record in one of its activity lists.</summary>
    private static (SettingsPage Page, ListBox List, TextBlock Header, TextBlock Time) FirstRecordHeader(
        MainWindow window, int page, string listName, string header)
    {
        window.Navigation.SelectedIndex = page;
        GuiHarness.Flush();
        var settings = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
        var list = settings.FindControl<ListBox>(listName)!;
        var text = list.ContainerFromIndex(0)!.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible).ToArray();
        var found = Assert.Single(text, x => new TextBlockAutomationPeer(x).GetName() == header);
        var time = text.Single(x => x.Text == "14:30:00");
        GuiHarness.AssertSameBaseline(found, time, window);
        return (settings, list, found, time);
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static SendQueueUpdate Update(long id, SendQueueItemKind kind,
        SendQueueItemState state = SendQueueItemState.Queued) => new(id, OccurredAt, kind, state, id, $"观众{id}");
}
