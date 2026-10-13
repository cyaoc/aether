using Aether.Core.Entries;

namespace Aether.Core;

/// <summary>One room connection's 进场欢迎 decisions, without logging or sending.</summary>
internal sealed class Welcome(Settings settings)
{
    private readonly WelcomeText welcomeText = new(settings.WelcomeText.Value);
    // On the same scale as Entry.GuardTier; 0 is 关闭.
    private readonly int guardThreshold = Settings.WelcomeGuards.ToList().IndexOf(settings.WelcomeGuard.Value);

    internal enum Rejection { EmptyText, TooLong }

    internal sealed record Result(WelcomeCondition Condition, string ConditionName, string? Value, string Text,
        bool Mention, Rejection? RejectedBecause, string? RejectionReason);

    public Result? Evaluate(Entry entry, int danmakuLengthLimit)
    {
        if (!settings.WelcomeEnabled.Value) return null;
        (WelcomeCondition Condition, string Name, string? Value) match;
        if (settings.WelcomeUids.Value.Contains(entry.Uid))
            match = (WelcomeCondition.Uid, "UID", null);
        else if (settings.WelcomeFanClubLevel.Value > 0 && entry.FanClubLevel >= settings.WelcomeFanClubLevel.Value)
            match = (WelcomeCondition.FanClubLevel, "粉丝团等级", FormattableString.Invariant($"{entry.FanClubLevel}"));
        else if (guardThreshold > 0 && entry.GuardTier >= guardThreshold)
            match = (WelcomeCondition.Guard, "大航海身份", entry.GuardName);
        else if (settings.WelcomeWealthLevel.Value > 0 && entry.WealthLevel >= settings.WelcomeWealthLevel.Value)
            match = (WelcomeCondition.WealthLevel, "荣耀等级", FormattableString.Invariant($"{entry.WealthLevel}"));
        else return null;

        var text = welcomeText.Render(entry.Nickname, entry.GuardName, danmakuLengthLimit);
        // B站 refuses an empty danmaku, as "{大航海}" for a non-guard or a fully truncated nickname gives.
        Rejection? rejection = string.IsNullOrWhiteSpace(text) ? Rejection.EmptyText
            : WelcomeText.Length(text) > danmakuLengthLimit ? Rejection.TooLong : null;
        var reason = rejection switch
        {
            Rejection.EmptyText => "欢迎语拼出来是空的",
            Rejection.TooLong => FormattableString.Invariant($"欢迎语超过弹幕字数上限 {danmakuLengthLimit}"),
            _ => null
        };
        return new(match.Condition, match.Name, match.Value, text, welcomeText.Mention, rejection, reason);
    }
}
