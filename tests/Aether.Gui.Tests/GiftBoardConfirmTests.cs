using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using static Aether.Gui.Tests.Support.GiftBoardScene;

namespace Aether.Gui.Tests;

public sealed class GiftBoardConfirmTests
{
    [Fact]
    public Task Clear_and_restore_are_off_until_there_is_something_to_clear_or_restore() => Run(async scene =>
    {
        // Not in a room: both are off, and the header says why.
        Assert.Equal((false, false, true), Offered(scene));

        // In a room with nobody on the board: still off, and nothing left to explain.
        scene.Connect();
        GuiHarness.Flush();
        Assert.Equal((false, false, false), Offered(scene));

        // Somebody on the board, no earlier 期: only clearing.
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450));
        await GuiHarness.WaitForAsync(() => Offered(scene).Clear);
        Assert.Equal((true, false, false), Offered(scene));

        // An earlier 期: both.
        scene.Board.Clear();
        scene.Give(PaidGift(2, "龍騰雲海", "告白气球", 2000, 5));
        await GuiHarness.WaitForAsync(() => Offered(scene).Restore);
        Assert.Equal((true, true, false), Offered(scene));

        // Leaving the room takes both away again.
        scene.Disconnect();
        await GuiHarness.WaitForAsync(() => !Offered(scene).Clear);
        Assert.Equal((false, false, true), Offered(scene));
    }, On);

    [Fact]
    public Task Clearing_asks_first_and_only_a_yes_opens_a_new_period() => Run(async scene =>
    {
        scene.GoToToday();
        scene.Connect();
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450), PaidGift(2, "龍騰雲海", "告白气球", 2000, 5));
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 2);

        GuiHarness.Click(scene.Page, "AskClearButton");
        Assert.Equal("清空后直播画面上的礼物榜也会变空，确定吗？", AskedText(scene));
        Assert.True(IsShown(scene, "ConfirmClearButton"));
        Assert.False(IsShown(scene, "ConfirmRestoreButton"));
        Assert.False(IsShown(scene, "PART_SaveButton"));

        GuiHarness.Click(scene.Page, "CancelConfirmButton");
        Assert.Null(AskedText(scene));
        Assert.True(IsShown(scene, "PART_SaveButton"));
        Assert.Equal(2, scene.Listed().Length);

        GuiHarness.Click(scene.Page, "AskClearButton");
        scene.Clock.Advance(TimeSpan.FromMinutes(12));
        GuiHarness.Click(scene.Page, "ConfirmClearButton");
        Assert.Null(AskedText(scene));
        await GuiHarness.WaitForAsync(() => Footer(scene) == "已清空直播间 1768506153 的礼物榜");
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 0);
        Assert.Equal("本期从 11月4日 20:15 开始", scene.Page.FindControl<TextBlock>("BoardPeriod")!.Text);
    }, On);

    [Fact]
    public Task Restoring_asks_first_and_merges_the_previous_period_back() => Run(async scene =>
    {
        scene.Connect();
        scene.Give(PaidGift(2, "龍騰雲海", "告白气球", 2000, 3), PaidGift(7, "夜雨听风", "星河入梦", 199, 2),
            PaidGift(10, "Aki秋", "为你加冕", 4500), PaidGift(11, "南風知我意", "提督", 19980));
        scene.GoToToday();
        scene.Board.Clear();
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450), PaidGift(2, "龍騰雲海", "告白气球", 2000, 5));
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 2 && Offered(scene).Restore);

        GuiHarness.Click(scene.Page, "AskRestoreButton");
        Assert.Equal("上一期（11月3日，4 位）会合并回来，确定吗？", AskedText(scene));
        Assert.True(IsShown(scene, "ConfirmRestoreButton"));
        Assert.False(IsShown(scene, "ConfirmClearButton"));

        GuiHarness.Click(scene.Page, "CancelConfirmButton");
        Assert.Equal(2, scene.Listed().Length);

        GuiHarness.Click(scene.Page, "AskRestoreButton");
        GuiHarness.Click(scene.Page, "ConfirmRestoreButton");
        await GuiHarness.WaitForAsync(() => Footer(scene) == "已恢复上一期");
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 5);
        Assert.Equal(["1 南風知我意 提督 ×1 19980 电池", "2 龍騰雲海 告白气球 ×8 16000 电池", "3 月下Lily 次元之城 ×1 12450 电池",
            "4 Aki秋 为你加冕 ×1 4500 电池", "5 夜雨听风 星河入梦 ×2 398 电池"], scene.Listed());
        Assert.Equal("本期从 11月3日 20:01 开始", scene.Page.FindControl<TextBlock>("BoardPeriod")!.Text);
        Assert.False(Offered(scene).Restore);
    }, On);

    [Fact]
    public Task Restoring_a_period_that_was_just_cleared_says_it_comes_back_whole() => Run(async scene =>
    {
        scene.Connect();
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450), PaidGift(2, "龍騰雲海", "告白气球", 2000, 5));
        scene.Board.Clear();
        await GuiHarness.WaitForAsync(() => Offered(scene).Restore && scene.Listed().Length == 0);

        GuiHarness.Click(scene.Page, "AskRestoreButton");
        Assert.Equal("上一期（11月3日，2 位）会恢复回来，确定吗？", AskedText(scene));
    }, On);

    [Fact]
    public Task A_question_closes_when_what_it_asks_can_no_longer_be_done() => Run(async scene =>
    {
        scene.Connect();
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450));
        await GuiHarness.WaitForAsync(() => Offered(scene).Clear);
        GuiHarness.Click(scene.Page, "AskClearButton");
        Assert.NotNull(AskedText(scene));

        scene.Disconnect();
        await GuiHarness.WaitForAsync(() => AskedText(scene) is null);
        Assert.True(IsShown(scene, "PART_SaveButton"));
    }, On);

    [Fact]
    public Task A_clear_or_restore_that_fails_says_so_in_the_footer_and_the_window_carries_on() => Run(async scene =>
    {
        scene.Connect();
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450));
        scene.Board.Clear();
        scene.Give(PaidGift(2, "龍騰雲海", "告白气球", 2000, 5));
        await GuiHarness.WaitForAsync(() => Offered(scene) is (true, true, false));
        using var database = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(scene.DataDirectory, "aether.db")};Pooling=False");
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = "ALTER TABLE gift_board_total RENAME TO unavailable_total; ALTER TABLE gift_board_period RENAME TO unavailable_period";
        command.ExecuteNonQuery();

        GuiHarness.Click(scene.Page, "AskClearButton");
        GuiHarness.Click(scene.Page, "ConfirmClearButton");
        await GuiHarness.WaitForAsync(() => Footer(scene).StartsWith("清空失败："));
        GuiHarness.Click(scene.Page, "AskRestoreButton");
        GuiHarness.Click(scene.Page, "ConfirmRestoreButton");
        await GuiHarness.WaitForAsync(() => Footer(scene).StartsWith("恢复失败："));

        command.CommandText = "ALTER TABLE unavailable_total RENAME TO gift_board_total; ALTER TABLE unavailable_period RENAME TO gift_board_period";
        command.ExecuteNonQuery();
        Assert.Equal(1, scene.Listed().Length);
        Assert.True(Offered(scene).Restore);
    }, On);

    private static string Footer(GiftBoardScene scene) => GuiHarness.FindVisual<TextBlock>(scene.Page, "Message").Text ?? "";

    /// <summary>Whether clearing and restoring are on offer, and whether the header says they need a room.</summary>
    private static (bool Clear, bool Restore, bool NeedsRoom) Offered(GiftBoardScene scene) =>
        (scene.Page.FindControl<Button>("AskClearButton")!.IsEnabled, scene.Page.FindControl<Button>("AskRestoreButton")!.IsEnabled,
            scene.Page.FindControl<TextBlock>("OfflineHint")!.IsEffectivelyVisible);

    /// <summary>What the footer asks, or null when it is not asking.</summary>
    private static string? AskedText(GiftBoardScene scene) =>
        IsShown(scene, "ConfirmText") ? scene.Page.FindControl<TextBlock>("ConfirmText")!.Text : null;

    private static bool IsShown(GiftBoardScene scene, string name) => GuiHarness.FindVisual<Control>(scene.Page, name).IsEffectivelyVisible;
}
