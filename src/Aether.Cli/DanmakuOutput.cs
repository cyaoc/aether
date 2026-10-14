using Aether.Core;

namespace Aether.Cli;

internal static class DanmakuOutput
{
    public static string Format(Danmaku danmaku)
    {
        var identity = danmaku.GuardName.Length > 0 ? $"[{danmaku.GuardName}]" : "";
        if (danmaku.FanClubLevel is > 0) identity += FormattableString.Invariant($"[粉丝团 {danmaku.FanClubLevel}]");
        if (danmaku.WealthLevel > 0) identity += FormattableString.Invariant($"[荣耀 {danmaku.WealthLevel}]");
        if (identity.Length > 0) identity = " " + identity;
        var reply = danmaku.ReplyName.Length > 0 ? $"@{OneLine(danmaku.ReplyName)} " : "";
        return FormattableString.Invariant($"[{danmaku.ReceivedAt:HH:mm:ss}] {OneLine(danmaku.Nickname)}{identity}: {reply}{OneLine(danmaku.Content)}");
    }

    private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');
}
