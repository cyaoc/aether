using System.Globalization;
using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.SentDanmaku;

namespace Aether.Core.Tests;

public sealed class WelcomeConditionTests
{
    [Theory]
    [InlineData("[10001, 20002]", 10001, "UID")]
    [InlineData("[20002, 10001]", 10001, "UID")]
    [InlineData("[10001]", 10000, null)]
    [InlineData("[]", 10001, null)]
    [InlineData("[9223372036854775807]", long.MaxValue, "UID")]
    public Task Specified_uids_match_exactly_including_int64_values(string uids, long uid, string? condition) =>
        CheckAsync(new() { ["welcome.uids"] = uids }, EntryV2Fixture.Message(uid: uid, guard: 0), condition);

    [Theory]
    [InlineData(21, 21, 7734200, true, "粉丝团等级")]
    [InlineData(21, 20, 7734200, true, null)]
    [InlineData(21, 22, 7734200, true, "粉丝团等级")]
    [InlineData(0, 47, 7734200, true, null)]
    [InlineData(21, 47, 1234567, true, null)]
    [InlineData(21, 21, 7734200, false, "粉丝团等级")]
    public Task Fan_club_threshold_counts_only_this_rooms_fan_club_lit_or_not(
        int threshold, long level, long room, bool lit, string? condition) =>
        CheckAsync(new() { ["welcome.fan_club_level"] = threshold.ToString(CultureInfo.InvariantCulture) },
            EntryV2Fixture.Message(guard: 0, fanClubLevel: level, fanClubRoom: room, fanClubLit: lit), condition);

    [Theory]
    [InlineData(45, 45, "荣耀等级")]
    [InlineData(45, 44, null)]
    [InlineData(45, 46, "荣耀等级")]
    [InlineData(0, 45, null)]
    public Task Wealth_alone_can_welcome_a_viewer_without_guard_status(int threshold, long level, string? condition) =>
        CheckAsync(new() { ["welcome.wealth_level"] = threshold.ToString(CultureInfo.InvariantCulture) },
            EntryV2Fixture.Message(guard: 0, wealthLevel: level), condition);

    [Theory]
    [InlineData("[10001]", 21, "舰长", "UID")]
    [InlineData("[]", 21, "舰长", "粉丝团等级")]
    [InlineData("[]", 0, "舰长", "大航海身份")]
    [InlineData("[]", 0, "关闭", "荣耀等级")]
    public Task Multiple_matches_enqueue_one_welcome_and_log_only_the_first_condition(
        string uids, int fanClub, string guard, string condition) =>
        CheckAsync(new()
        {
            ["welcome.uids"] = uids, ["welcome.fan_club_level"] = fanClub.ToString(CultureInfo.InvariantCulture),
            ["welcome.guard"] = guard, ["welcome.wealth_level"] = "45"
        }, EntryV2Fixture.Message(), condition, "欢迎舰长测试观众");

    private static async Task CheckAsync(Dictionary<string, string> settings, string entry, string? condition,
        string text = "欢迎测试观众")
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        settings["welcome.enabled"] = "true";
        settings.TryAdd("welcome.guard", "关闭");
        Settings.Save(h.DataDirectory, settings);
        var sent = CaptureSends(h);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, entry);
            if (condition is not null)
            {
                var log = Assert.Single(h.Logger.Entries, log => log.Message.StartsWith("进场欢迎："));
                Assert.Equal(LogLevel.Information, log.Level);
                Assert.EndsWith("，命中" + condition, log.Message);
                var form = await NextSendAsync(sent);
                Assert.Equal(text, form["msg"]);
                await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功：欢迎观众");
            }
            else
            {
                await AssertNoSendAsync(sent);
                Assert.DoesNotContain(h.Logger.Entries, log => log.Message.StartsWith("进场欢迎："));
            }
        }
        Assert.Equal(condition is null ? 0 : 1, SendAttempts(h));
        Assert.Contains(h.Logger.Entries, log => log.Message.Contains("丢掉 0 条"));
    }
}
