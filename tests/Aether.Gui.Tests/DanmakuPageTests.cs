using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class DanmakuPageTests
{
    [Theory]
    [InlineData("known", false)]
    [InlineData("known", true)]
    [InlineData("loading", false)]
    [InlineData("failed", false)]
    [InlineData("missing", false)]
    public Task Only_known_account_danmaku_are_marked_and_late_account_reads_update_existing_rows(string accountState, bool dark) =>
        HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            if (accountState != "missing") await harness.SaveCredentialAsync();
            var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.NavResponse = token => response.Task.WaitAsync(token);
            var viewModel = harness.CreateViewModel();
            var window = new MainWindow { DataContext = viewModel, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            window.Show();
            try
            {
                if (accountState == "failed") response.SetException(new HttpRequestException("offline"));
                if (accountState == "known") response.SetResult(GuiHarness.AccountResponse());
                if (accountState != "loading") await viewModel.ReadAccountCommand.ExecutionTask!;
                viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "同名观众", "账号发的") { Uid = 123 });
                viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "同名观众", "别人发的") { Uid = 999 });
                GuiHarness.Flush();
                AssertRow(0, accountState == "known");
                AssertRow(1, false);
                if (accountState == "missing")
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "未登录" && x.IsEffectivelyVisible);
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
                    Assert.NotNull(background.Background);
                    Assert.NotEqual(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(background.Background).Color);
                }
                else Assert.Null(background);
                Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "00:00:00");
            }
        }, TestContext.Current.CancellationToken);
}
