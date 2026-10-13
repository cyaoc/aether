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
    public void Specified_uids_match_exactly_including_int64_values(string uids, long uid, string? condition)
    {
        var welcome = CreateWelcome(new() { ["welcome.uids"] = uids });
        var result = welcome.Evaluate(new Entry(uid, "测试观众", 1, 0, null, 0), 20);
        Assert.Equal(condition, result?.ConditionName);
        if (condition is null) Assert.Null(result);
        else
        {
            Assert.NotNull(result);
            Assert.Equal(WelcomeCondition.Uid, result.Condition);
            Assert.Null(result.Value);
            Assert.Equal("欢迎测试观众", result.Text);
            Assert.False(result.Mention);
            Assert.Null(result.RejectionReason);
        }
    }

    [Theory]
    [InlineData(21, 21L, "21")]
    [InlineData(21, 20L, null)]
    [InlineData(21, 22L, "22")]
    [InlineData(0, 47L, null)]
    [InlineData(21, null, null)]
    public void Fan_club_threshold_is_inclusive_and_requires_a_fan_club(int threshold, long? level, string? value)
    {
        var welcome = CreateWelcome(new() { ["welcome.fan_club_level"] = threshold.ToString(CultureInfo.InvariantCulture) });
        var result = welcome.Evaluate(new Entry(10001, "测试观众", 1, 0, level, 0), 20);
        if (value is null) Assert.Null(result);
        else
        {
            Assert.NotNull(result);
            Assert.Equal(WelcomeCondition.FanClubLevel, result.Condition);
            Assert.Equal("粉丝团等级", result.ConditionName);
            Assert.Equal(value, result.Value);
        }
    }

    [Theory]
    [InlineData(45, 45, "45")]
    [InlineData(45, 44, null)]
    [InlineData(45, 46, "46")]
    [InlineData(0, 45, null)]
    public void Wealth_alone_can_welcome_a_viewer_without_guard_status(int threshold, long level, string? value)
    {
        var welcome = CreateWelcome(new() { ["welcome.wealth_level"] = threshold.ToString(CultureInfo.InvariantCulture) });
        var result = welcome.Evaluate(new Entry(10001, "测试观众", 1, 0, null, level), 20);
        if (value is null) Assert.Null(result);
        else
        {
            Assert.NotNull(result);
            Assert.Equal(WelcomeCondition.WealthLevel, result.Condition);
            Assert.Equal("荣耀等级", result.ConditionName);
            Assert.Equal(value, result.Value);
        }
    }

    [Theory]
    [InlineData("[10001]", 21, "舰长", WelcomeCondition.Uid, "UID", null)]
    [InlineData("[]", 21, "舰长", WelcomeCondition.FanClubLevel, "粉丝团等级", "47")]
    [InlineData("[]", 0, "舰长", WelcomeCondition.Guard, "大航海身份", "舰长")]
    [InlineData("[]", 0, "关闭", WelcomeCondition.WealthLevel, "荣耀等级", "45")]
    public void Multiple_matches_return_only_the_first_condition(
        string uids, int fanClub, string guard, WelcomeCondition condition, string conditionName, string? value)
    {
        var welcome = CreateWelcome(new()
        {
            ["welcome.uids"] = uids, ["welcome.fan_club_level"] = fanClub.ToString(CultureInfo.InvariantCulture),
            ["welcome.guard"] = guard, ["welcome.wealth_level"] = "45"
        });
        var result = welcome.Evaluate(new Entry(10001, "测试观众", 1, 3, 47, 45), 20);
        Assert.NotNull(result);
        Assert.Equal(condition, result.Condition);
        Assert.Equal(conditionName, result.ConditionName);
        Assert.Equal(value, result.Value);
        Assert.Equal("欢迎舰长测试观众", result.Text);
        Assert.Null(result.RejectionReason);
    }

    [Theory]
    [InlineData("舰长", 0, null)]
    [InlineData("舰长", 3, "舰长")]
    [InlineData("提督", 3, null)]
    [InlineData("提督", 2, "提督")]
    [InlineData("提督", 1, "总督")]
    [InlineData("关闭", 1, null)]
    [InlineData("关闭", 2, null)]
    [InlineData("关闭", 3, null)]
    public void Guard_threshold_is_inclusive_and_can_be_disabled(string threshold, long guard, string? value)
    {
        var welcome = CreateWelcome(new() { ["welcome.guard"] = threshold });
        var result = welcome.Evaluate(new Entry(10001, "测试观众", 1, guard, null, 0), 20);
        if (value is null) Assert.Null(result);
        else
        {
            Assert.NotNull(result);
            Assert.Equal(WelcomeCondition.Guard, result.Condition);
            Assert.Equal("大航海身份", result.ConditionName);
            Assert.Equal(value, result.Value);
        }
    }

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
        var welcome = CreateWelcome(new() { ["welcome.uids"] = "[10001]", ["welcome.text"] = template });
        var result = welcome.Evaluate(new Entry(10001, "测试观众", 1, 0, null, 0), limit);
        Assert.NotNull(result);
        Assert.Equal(WelcomeCondition.Uid, result.Condition);
        Assert.Equal(text, result.Text);
        Assert.Equal(mention, result.Mention);
        Assert.Equal(reason, result.RejectionReason);
    }

    [Fact]
    public void Disabled_welcomes_do_not_match_any_condition()
    {
        var welcome = CreateWelcome(new()
        {
            ["welcome.enabled"] = "false", ["welcome.uids"] = "[10001]", ["welcome.fan_club_level"] = "21",
            ["welcome.guard"] = "舰长", ["welcome.wealth_level"] = "45"
        });
        Assert.Null(welcome.Evaluate(new Entry(10001, "测试观众", 1, 3, 47, 45), 20));
    }

    private Welcome CreateWelcome(Dictionary<string, string> changes)
    {
        changes.TryAdd("welcome.enabled", "true");
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
            var log = Assert.Single(h.Logger.Entries, log => log.Message.StartsWith("进场欢迎："));
            Assert.Equal(LogLevel.Information, log.Level);
            Assert.Equal("进场欢迎：测试观众（10001），命中UID", log.Message);
            var form = await NextSendAsync(sent);
            Assert.Equal("欢迎舰长测试观众", form["msg"]);
            Assert.Equal("10001", form["reply_mid"]);
            await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功：欢迎观众");
        }
        Assert.Equal(1, SendAttempts(h));
        Assert.Contains(h.Logger.Entries, log => log.Message.Contains("丢掉 0 条"));
    }
}
