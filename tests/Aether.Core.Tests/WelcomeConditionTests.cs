using System.Globalization;
using Aether.Core.Entries;
using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.SentDanmaku;

namespace Aether.Core.Tests;

public sealed class WelcomeConditionTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "aether-welcome-tests-" + Guid.NewGuid());

    [Theory]
    [InlineData("[10001, 20002]", 10001, "UID")]
    [InlineData("[20002, 10001]", 10001, "UID")]
    [InlineData("[10001]", 10000, null)]
    [InlineData("[]", 10001, null)]
    [InlineData("[9223372036854775807]", long.MaxValue, "UID")]
    public void Specified_uids_match_exactly_including_int64_values(string uids, long uid, string? match) =>
        Assert.Equal(match, Match(new() { ["welcome.uids"] = uids }, Viewer(uid: uid)));

    [Theory]
    [InlineData(21, 21L, "粉丝团等级 21")]
    [InlineData(21, 20L, null)]
    [InlineData(21, 22L, "粉丝团等级 22")]
    [InlineData(0, 47L, null)]
    [InlineData(21, null, null)]
    public void Fan_club_threshold_is_inclusive_and_requires_a_fan_club(int threshold, long? level, string? match) =>
        Assert.Equal(match, Match(new() { ["welcome.fan_club_level"] = threshold.ToString(CultureInfo.InvariantCulture) },
            Viewer(fanClubLevel: level)));

    [Theory]
    [InlineData("舰长", 0, null)]
    [InlineData("舰长", 3, "大航海身份 舰长")]
    [InlineData("提督", 3, null)]
    [InlineData("提督", 2, "大航海身份 提督")]
    [InlineData("提督", 1, "大航海身份 总督")]
    [InlineData("关闭", 1, null)]
    [InlineData("关闭", 2, null)]
    [InlineData("关闭", 3, null)]
    public void Guard_threshold_is_inclusive_and_can_be_disabled(string threshold, long privilegeType, string? match) =>
        Assert.Equal(match, Match(new() { ["welcome.guard"] = threshold }, Viewer(privilegeType: privilegeType)));

    [Theory]
    [InlineData(45, 45, "荣耀等级 45")]
    [InlineData(45, 44, null)]
    [InlineData(45, 46, "荣耀等级 46")]
    [InlineData(0, 45, null)]
    public void Wealth_alone_can_welcome_a_viewer_without_guard_status(int threshold, long level, string? match) =>
        Assert.Equal(match, Match(new() { ["welcome.wealth_level"] = threshold.ToString(CultureInfo.InvariantCulture) },
            Viewer(wealthLevel: level)));

    [Theory]
    [InlineData("[10001]", 21, "舰长", "UID")]
    [InlineData("[]", 21, "舰长", "粉丝团等级 47")]
    [InlineData("[]", 0, "舰长", "大航海身份 舰长")]
    [InlineData("[]", 0, "关闭", "荣耀等级 45")]
    public void Multiple_matches_return_only_the_first_condition(string uids, int fanClub, string guard, string match) =>
        Assert.Equal(match, Match(new()
        {
            ["welcome.uids"] = uids, ["welcome.fan_club_level"] = fanClub.ToString(CultureInfo.InvariantCulture),
            ["welcome.guard"] = guard, ["welcome.wealth_level"] = "45"
        }, Viewer(privilegeType: 3, fanClubLevel: 47, wealthLevel: 45)));

    [Theory]
    [InlineData("{@}欢迎{昵称}", 8, "欢迎测试", true, null)]
    [InlineData("欢迎{大航海}{昵称}", 20, "欢迎测试观众", false, null)]
    [InlineData("{大航海}", 20, "", false, "欢迎语拼出来是空的")]
    [InlineData("{@}{昵称}", 1, "", true, "欢迎语拼出来是空的")]
    [InlineData("欢迎回家{昵称}", 7, "欢迎回家", false, "欢迎语超过弹幕字数上限 7")]
    [InlineData("欢迎回家{昵称}", 8, "欢迎回家", false, null)]
    public void Matched_result_includes_rendered_text_mention_and_rejection_reason(
        string template, int limit, string text, bool mention, string? reason)
    {
        var result = CreateWelcome(new() { ["welcome.uids"] = "[10001]", ["welcome.text"] = template }).Evaluate(Viewer(), limit);
        Assert.NotNull(result);
        Assert.Equal(text, result.Text);
        Assert.Equal(mention, result.Mention);
        Assert.Equal(reason, result.RejectionReason);
    }

    [Fact]
    public async Task Watch_logs_and_sends_one_welcome_when_multiple_conditions_match()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["welcome.uids"] = "[10001]", ["welcome.fan_club_level"] = "21",
            ["welcome.guard"] = "舰长", ["welcome.wealth_level"] = "45", ["welcome.text"] = "{@}欢迎{大航海}{昵称}"
        });
        var sent = CaptureSends(h);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
            var log = Assert.Single(h.Logger.Entries, log => log.Message.Contains("进场欢迎："));
            Assert.Equal(LogLevel.Information, log.Level);
            Assert.EndsWith("进场欢迎：测试观众（10001），命中UID", log.Message);
            var form = await NextSendAsync(sent);
            Assert.Equal("欢迎舰长测试观众", form["msg"]);
            Assert.Equal("10001", form["reply_mid"]);
            await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功：欢迎观众");
        }
        Assert.Equal(1, SendAttempts(h));
        Assert.Contains(h.Logger.Entries, log => log.Message.Contains("丢掉 0 条"));
    }

    /// <param name="privilegeType">B站's code for the 大航海身份: 3 舰长, 2 提督, 1 总督, 0 none.</param>
    private static Entry Viewer(long uid = 10001, long privilegeType = 0, long? fanClubLevel = null, long wealthLevel = 0) =>
        new(uid, "测试观众", 1, privilegeType, fanClubLevel, wealthLevel);

    /// <summary>The matched condition as the log names it, then its value; null when none matched.</summary>
    private string? Match(Dictionary<string, string> changes, Entry entry) =>
        CreateWelcome(changes).Evaluate(entry, 20) is { } result ? $"{result.ConditionName} {result.Value}".TrimEnd() : null;

    private Welcome CreateWelcome(Dictionary<string, string> changes)
    {
        changes.TryAdd("welcome.guard", "关闭");
        Settings.Save(dataDirectory, changes);
        var settings = Settings.Load(dataDirectory);
        Assert.Null(settings.Error);
        return new Welcome(settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
    }
}
