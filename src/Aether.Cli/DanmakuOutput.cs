using Aether.Core;

namespace Aether.Cli;

internal static class DanmakuOutput
{
    public static string Format(Danmaku danmaku)
    {
        var tags = danmaku.GuardName.Length > 0 ? $"[{danmaku.GuardName}]" : "";
        if (danmaku.FanClubLevel is { } fanClub) tags += FormattableString.Invariant($"[粉丝团 {fanClub}]");
        if (danmaku.WealthLevel > 0) tags += FormattableString.Invariant($"[荣耀 {danmaku.WealthLevel}]");
        if (tags.Length > 0) tags = " " + tags;
        var reply = danmaku.ReplyName.Length > 0 ? $"@{OneLine(danmaku.ReplyName)} " : "";
        return FormattableString.Invariant($"[{danmaku.ReceivedAt:HH:mm:ss}] {OneLine(danmaku.Nickname)}{tags}: {reply}{OneLine(danmaku.Content)}");
    }

    private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');
}
