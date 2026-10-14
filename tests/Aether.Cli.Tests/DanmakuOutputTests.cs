using Aether.Core;

namespace Aether.Cli.Tests;

public sealed class DanmakuOutputTests
{
    [Theory]
    [InlineData(1, 47L, 45, "深蓝色的机器人", "开潜艇的3342_", "今日盲盒", "开潜艇的3342_ [舰长][粉丝团 47][荣耀 45]: @深蓝色的机器人 今日盲盒")]
    [InlineData(0, null, 0, "", "路人甲", "晚上好", "路人甲: 晚上好")]
    [InlineData(0, null, 12, "", "路人乙", "主播好", "路人乙 [荣耀 12]: 主播好")]
    [InlineData(2, null, 0, "", "观众", "来了", "观众 [提督]: 来了")]
    [InlineData(3, null, 0, "", "观众", "来了", "观众 [总督]: 来了")]
    [InlineData(0, 5L, 0, "", "观众", "来了", "观众 [粉丝团 5]: 来了")]
    [InlineData(0, null, 0, "被\r@\n的人", "观\r众\n名", "内\r容\n", "观 众 名: @被 @ 的人 内 容 ")]
    public void Formats_guard_fan_club_wealth_and_reply_as_one_plain_text_line(int tier, long? fanClub, long wealth,
        string reply, string nickname, string content, string expected)
    {
        var danmaku = new Danmaku(new DateTimeOffset(2026, 10, 14, 20, 15, 3, TimeSpan.FromHours(8)), nickname, content)
            { GuardTier = tier, FanClubLevel = fanClub, WealthLevel = wealth, ReplyName = reply };

        Assert.Equal("[20:15:03] " + expected, DanmakuOutput.Format(danmaku));
    }
}
