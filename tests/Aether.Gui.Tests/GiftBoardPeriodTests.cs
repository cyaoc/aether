using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using static Aether.Gui.Tests.Support.GiftBoardScene;

namespace Aether.Gui.Tests;

/// <summary>The 礼物榜 runs in 期 like a lottery (ADR 0014, canvases GS1, GS3 and GS11–GS13): the header says which state the
/// room's latest 期 is in and offers only what that state allows.</summary>
public sealed class GiftBoardPeriodTests
{
    [Fact]
    public Task The_header_shows_the_state_and_only_the_buttons_it_allows() => Run(async scene =>
    {
        // Not in a room: no state, and a new 期 waits for one.
        Assert.Equal(new Header(null, false, false, false, true), Read(scene));

        scene.Connect();
        await GuiHarness.WaitForAsync(() => Read(scene).Status == "还没开始");
        Assert.Equal(new Header("还没开始", false, false, true, false), Read(scene));

        scene.Start();
        await GuiHarness.WaitForAsync(() => Read(scene).Status == "统计中");
        Assert.Equal(new Header("统计中", true, false, false, false), Read(scene));

        scene.Board.EndPeriod();
        await GuiHarness.WaitForAsync(() => Read(scene).Status == "已结束");
        Assert.Equal(new Header("已结束", false, true, true, false), Read(scene));

        scene.Disconnect();
        await GuiHarness.WaitForAsync(() => Read(scene).Status is null);
        Assert.Equal(new Header(null, false, false, false, true), Read(scene));
    });

    [Fact]
    public Task The_first_period_starts_without_asking() => Run(async scene =>
    {
        scene.GoToToday();
        scene.Connect();
        await GuiHarness.WaitForAsync(() => Read(scene).NewPeriod);

        GuiHarness.Click(scene.Page, "NewPeriodButton");

        Assert.Null(AskedText(scene));
        await GuiHarness.WaitForAsync(() => Footer(scene) == "已为直播间 1768506153 开始新的一期");
        Assert.Equal("统计中", Read(scene).Status);
        Assert.Equal("本期从 11月4日 20:03 开始", Text(scene, "BoardPeriod"));
    });

    [Fact]
    public Task Ending_asks_first_and_the_ended_period_stays_in_the_list() => Run(async scene =>
    {
        scene.GoToToday();
        scene.Connect();
        scene.Start();
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450));
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 1);

        GuiHarness.Click(scene.Page, "EndButton");
        Assert.Equal("结束后不再记录，画面上的榜也会收起，确定吗？", AskedText(scene));
        Assert.True(IsShown(scene, "ConfirmEndButton"));
        Assert.Contains("dangerSolid", scene.Page.FindControl<Button>("ConfirmEndButton")!.Classes);
        Assert.False(IsShown(scene, "ConfirmNewPeriodButton"));

        GuiHarness.Click(scene.Page, "CancelConfirmButton");
        Assert.Null(AskedText(scene));
        Assert.Equal("统计中", Read(scene).Status);

        GuiHarness.Click(scene.Page, "EndButton");
        scene.Clock.Advance(TimeSpan.FromMinutes(158));
        GuiHarness.Click(scene.Page, "ConfirmEndButton");
        await GuiHarness.WaitForAsync(() => Footer(scene) == "已结束这一期，直播画面上的榜已收起");
        Assert.Equal("已结束", Read(scene).Status);
        Assert.Equal(["1 月下Lily 次元之城 ×1 12450 电池"], scene.Listed());
        Assert.Equal("本期 11月4日 20:03 – 22:41，已结束", Text(scene, "BoardPeriod"));
    });

    [Fact]
    public Task A_new_period_after_an_ended_one_asks_first_and_starts_from_zero() => Run(async scene =>
    {
        scene.Connect();
        scene.Start();
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450));
        scene.Board.EndPeriod();
        await GuiHarness.WaitForAsync(() => Read(scene).Status == "已结束");

        GuiHarness.Click(scene.Page, "NewPeriodButton");
        Assert.Equal("开始新一期后，画面上的榜从零算起，确定吗？", AskedText(scene));
        Assert.True(IsShown(scene, "ConfirmNewPeriodButton"));
        Assert.False(IsShown(scene, "ConfirmEndButton"));
        GuiHarness.Click(scene.Page, "ConfirmNewPeriodButton");

        await GuiHarness.WaitForAsync(() => Footer(scene) == "已为直播间 1768506153 开始新的一期");
        Assert.Equal("统计中", Read(scene).Status);
        Assert.Empty(scene.Listed());
        Assert.Equal("还没有人上榜。达到门槛的礼物会出现在这里和直播画面上。", Text(scene, "BoardEmpty"));
    });

    [Fact]
    public Task Continuing_an_ended_period_needs_no_question() => Run(async scene =>
    {
        scene.Connect();
        scene.Start();
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450));
        scene.Board.EndPeriod();
        await GuiHarness.WaitForAsync(() => Read(scene).Continue);

        GuiHarness.Click(scene.Page, "ContinueButton");

        Assert.Null(AskedText(scene));
        await GuiHarness.WaitForAsync(() => Footer(scene) == "已继续这一期，画面上重新显示榜");
        Assert.Equal("统计中", Read(scene).Status);
        Assert.Single(scene.Listed());
    });

    [Fact]
    public Task A_question_closes_when_what_it_asks_can_no_longer_be_done() => Run(async scene =>
    {
        scene.Connect();
        scene.Start();
        await GuiHarness.WaitForAsync(() => Read(scene).End);
        GuiHarness.Click(scene.Page, "EndButton");
        Assert.NotNull(AskedText(scene));

        scene.Disconnect();
        await GuiHarness.WaitForAsync(() => AskedText(scene) is null);
        Assert.Equal("选项跟着直播间的期，连上直播间后才能改", Footer(scene));
    });

    [Fact]
    public Task An_action_that_fails_says_so_in_the_footer_and_the_window_carries_on() => Run(async scene =>
    {
        scene.Connect();
        await GuiHarness.WaitForAsync(() => Read(scene).NewPeriod);
        using var database = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(scene.DataDirectory, "aether.db")};Pooling=False");
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = "ALTER TABLE gift_board_period RENAME TO unavailable_period";
        command.ExecuteNonQuery();

        GuiHarness.Click(scene.Page, "NewPeriodButton");
        await GuiHarness.WaitForAsync(() => Text(scene, "ErrorMessage").StartsWith("开始新一期失败："));

        command.CommandText = "ALTER TABLE unavailable_period RENAME TO gift_board_period";
        command.ExecuteNonQuery();
        GuiHarness.Click(scene.Page, "NewPeriodButton");
        await GuiHarness.WaitForAsync(() => Read(scene).Status == "统计中");
        Assert.False(IsShown(scene, "ErrorMessage"));
    });

    /// <summary>The state in the header, null when it shows none, and which of its buttons are there to click.</summary>
    private sealed record Header(string? Status, bool End, bool Continue, bool NewPeriod, bool NeedsRoom);

    private static Header Read(GiftBoardScene scene)
    {
        GuiHarness.Flush();
        var newPeriod = scene.Page.FindControl<Button>("NewPeriodButton")!;
        return new(IsShown(scene, "StatusChip") ? Text(scene, "StatusText") : null, IsShown(scene, "EndButton"), IsShown(scene, "ContinueButton"),
            newPeriod.IsEffectivelyVisible && newPeriod.IsEffectivelyEnabled, IsShown(scene, "OfflineHint"));
    }

    private static string Footer(GiftBoardScene scene) => IsShown(scene, "FooterText") ? Text(scene, "FooterText") : "";

    /// <summary>What the footer asks, or null when it is not asking.</summary>
    private static string? AskedText(GiftBoardScene scene)
    {
        GuiHarness.Flush();
        return IsShown(scene, "ConfirmEndText") ? Text(scene, "ConfirmEndText")
            : IsShown(scene, "ConfirmNewPeriodText") ? Text(scene, "ConfirmNewPeriodText") : null;
    }

    private static string Text(GiftBoardScene scene, string name) => scene.Page.FindControl<TextBlock>(name)!.Text ?? "";

    private static bool IsShown(GiftBoardScene scene, string name) => scene.Page.FindControl<Control>(name)!.IsEffectivelyVisible;
}
