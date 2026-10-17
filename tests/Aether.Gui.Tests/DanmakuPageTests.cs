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

public sealed class DanmakuPageTests
{
    [Theory]
    [InlineData(1200)]
    [InlineData(1040)]
    public Task Rows_have_separate_highlights_and_align_with_the_page_title(int width) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, Width = width };
        window.Show();
        try
        {
            for (var i = 0; i < 3; i++)
                viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "观众", "晚上好") { GuardTier = i < 2 ? 1 : 0 });
            GuiHarness.Flush();
            var page = window.FindControl<Grid>("DanmakuPage")!;
            var title = page.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "弹幕");
            double previousBottom = 0;
            for (var i = 0; i < 3; i++)
            {
                var item = window.DanmakuList.ContainerFromIndex(i)!;
                var row = item.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "DanmakuRow");
                var origin = row.TranslatePoint(default, window.DanmakuList)!.Value;
                Assert.Equal(8, origin.X);
                Assert.Equal(window.DanmakuList.Bounds.Width - 16, row.Bounds.Width);
                Assert.True(origin.Y >= (i == 0 ? 8 : previousBottom + 2));
                var avatar = item.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "Avatar");
                Assert.Equal(title.TranslatePoint(default, window)!.Value.X, avatar.TranslatePoint(default, window)!.Value.X);
                previousBottom = origin.Y + row.Bounds.Height;
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(1, "舰长", false, "#E0FDFC", "#B9EFEE", "#006E70")]
    [InlineData(2, "提督", false, "#FFEFFF", "#F8D7F6", "#7E3F7C")]
    [InlineData(3, "总督", false, "#FFF4E0", "#F7E0BA", "#7D5100")]
    [InlineData(1, "舰长", true, "#0B2828", "#004848", "#8BE3E2")]
    [InlineData(2, "提督", true, "#2C1D2B", "#512F50", "#F0BEEE")]
    [InlineData(3, "总督", true, "#2B210F", "#503807", "#EFCB91")]
    public Task Guard_fan_club_and_wealth_badges_reply_prefix_and_row_colors_follow_the_danmaku(int tier, string guard, bool dark,
        string rowColor, string guardBackground, string guardForeground) => HeadlessApp.Session.Dispatch(async () =>
    {
        const string accountNickname = "昵称很长也不应该把徽章挤出弹幕列表";
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, Width = 1040, Height = 600, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "观众", "今日盲盒")
                { Uid = 999, GuardTier = tier, FanClubLevel = 47, WealthLevel = 45, ReplyName = "深蓝色的机器人" });
            viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, accountNickname, "回复正文")
                { Uid = 123, GuardTier = tier, FanClubLevel = 47, WealthLevel = 45, ReplyName = "观众" });
            viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "路人", "晚上好") { Uid = 998 });
            viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "只戴牌子", "晚上好") { Uid = 997, FanClubLevel = 12 });
            viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "只有荣耀", "晚上好") { Uid = 996, WealthLevel = 8 });
            GuiHarness.Flush();

            AssertRow(0, ["观众", guard, "粉丝团 47", "荣耀 45", "00:00:00"], rowColor, "@深蓝色的机器人 今日盲盒");
            AssertRow(1, [accountNickname, "账号", guard, "粉丝团 47", "荣耀 45", "00:00:00"], dark ? "#232B46" : "#E3ECFF", "@观众 回复正文");
            AssertRow(2, ["路人", "00:00:00"], null, "晚上好");
            AssertRow(3, ["只戴牌子", "粉丝团 12", "00:00:00"], null, "晚上好");
            AssertRow(4, ["只有荣耀", "荣耀 8", "00:00:00"], null, "晚上好");
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;

        void AssertRow(int index, string[] header, string? background, string content)
        {
            var row = Assert.IsType<ListBoxItem>(window.DanmakuList.ContainerFromIndex(index));
            var borders = row.GetVisualDescendants().OfType<Border>().ToArray();
            var rowBorder = Assert.Single(borders, x => x.Name == "DanmakuRow");
            if (background is null) Assert.True(rowBorder.Background is null || ColorOf(rowBorder.Background) == Colors.Transparent);
            else Assert.Equal(Color.Parse(background), ColorOf(rowBorder.Background));
            var heading = row.GetVisualDescendants().OfType<Panel>().Single(x => x.Name == "DanmakuHeader");
            Assert.Equal(header, heading.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible).Select(x => x.Text));
            Assert.All(heading.Children.Where(x => x.IsVisible), child => Assert.True(child.Bounds.Right <= heading.Bounds.Width));
            var body = row.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "DanmakuContent");
            var runs = body.Inlines!.OfType<Run>().ToArray();
            Assert.Equal(content, string.Concat(runs.Select(x => x.Text)));
            Assert.Equal(content, new TextBlockAutomationPeer(body).GetName());
            Assert.Equal(FontWeight.SemiBold, runs[0].FontWeight);
            Assert.Equal(dark ? Color.Parse("#B4C3FF") : Color.Parse("#2A47B0"), ColorOf(runs[0].Foreground));
            Assert.Equal(FontWeight.Normal, runs[1].FontWeight);
            Assert.Equal(dark ? Color.Parse("#E8EAED") : Color.Parse("#16181D"), ColorOf(runs[1].Foreground));
            foreach (var badge in borders.Where(x => x.Classes.Contains("badge") && x.IsEffectivelyVisible))
            {
                var text = Assert.IsType<TextBlock>(badge.Child);
                var (bg, fg) = text.Text == "账号" ? (dark ? "#8DA2FB" : "#3358D4", dark ? "#141518" : "#FFFFFF")
                    : text.Text == guard ? (guardBackground, guardForeground)
                    : (dark ? "#2E3136" : "#EEF0F3", dark ? "#C2C6CE" : "#3D4350");
                Assert.Equal(Color.Parse(bg), ColorOf(badge.Background));
                Assert.Equal(Color.Parse(fg), ColorOf(text.Foreground));
                Assert.Equal(text.Text is "账号" || text.Text == guard ? FontWeight.SemiBold : FontWeight.Normal, text.FontWeight);
                Assert.Equal(new CornerRadius(4), badge.CornerRadius);
                Assert.Equal(new Thickness(5, 1), badge.Padding);
                Assert.Equal(11, text.FontSize);
            }
        }
    }, TestContext.Current.CancellationToken);

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [Theory]
    [InlineData("known", false, 0)]
    [InlineData("known", true, 0)]
    [InlineData("loading", false, 0)]
    [InlineData("failed", false, 0)]
    [InlineData("loading", false, 1)]
    [InlineData("loading", true, 2)]
    [InlineData("loading", false, 3)]
    [InlineData("failed", true, 1)]
    public Task Only_known_account_danmaku_are_marked_and_late_account_reads_update_existing_rows(string accountState, bool dark, int guardTier) =>
        HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.NavResponse = token => response.Task.WaitAsync(token);
            var viewModel = harness.CreateViewModel();
            var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            window.Show();
            try
            {
                if (accountState == "failed") response.SetException(new HttpRequestException("offline"));
                if (accountState == "known") response.SetResult(GuiHarness.AccountResponse());
                if (accountState != "loading") await viewModel.ReadAccountCommand.ExecutionTask!;
                viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "同名观众", "账号发的") { Uid = 123, GuardTier = guardTier });
                viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "同名观众", "别人发的") { Uid = 999, GuardTier = guardTier });
                GuiHarness.Flush();
                AssertRow(0, accountState == "known");
                AssertRow(1, false);
                if (accountState == "loading")
                {
                    response.SetResult(GuiHarness.AccountResponse());
                    await viewModel.ReadAccountCommand.ExecutionTask!;
                    GuiHarness.Flush();
                    AssertRow(0, true);
                    AssertRow(1, false);
                }
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
            return true;

            void AssertRow(int index, bool marked)
            {
                var row = Assert.IsType<ListBoxItem>(window.DanmakuList.ContainerFromIndex(index));
                var badge = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "账号");
                Assert.Equal(marked, badge.IsEffectivelyVisible);
                var background = row.GetVisualDescendants().OfType<Border>().FirstOrDefault(x => x.Classes.Contains("account"));
                if (marked)
                {
                    Assert.NotNull(background);
                    Assert.Equal(Color.Parse(dark ? "#232B46" : "#E3ECFF"), ColorOf(background.Background));
                }
                else Assert.Null(background);
                if (!marked && guardTier > 0)
                {
                    var guardRow = row.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "DanmakuRow");
                    var resource = $"Guard.{new[] { "", "Captain", "Admiral", "Governor" }[guardTier]}.Row";
                    Assert.True(window.TryFindResource(resource, window.ActualThemeVariant, out var expected));
                    Assert.Equal(ColorOf(expected as IBrush), ColorOf(guardRow.Background));
                }
                Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "00:00:00");
            }
        }, TestContext.Current.CancellationToken);
}
