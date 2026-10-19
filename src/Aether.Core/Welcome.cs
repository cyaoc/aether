using Aether.Core.Entries;

namespace Aether.Core;

/// <summary>One room connection's 进场欢迎 decisions, without logging or sending.</summary>
internal sealed class Welcome(Settings settings, Random? random = null)
{
    private readonly WelcomeText[] welcomeTexts = settings.WelcomeTexts.Value.Select(text => new WelcomeText(text)).ToArray();
    private readonly Random random = random ?? Random.Shared;
    // On the same scale as Entry.GuardTier; 0 is 关闭.
    private readonly int guardThreshold = Settings.WelcomeGuards.ToList().IndexOf(settings.WelcomeGuard.Value);

    internal enum Rejection { EmptyText, TooLong }

    /// <param name="Value">The viewer's matched level or guard name; null for a UID match.</param>
    /// <param name="DanmakuLengthLimit">The 弹幕字数上限 the text was rendered and checked against.</param>
    internal sealed record Result(WelcomeCondition Condition, string? Value, string Text, bool Mention,
        Rejection? RejectedBecause, int DanmakuLengthLimit)
    {
        /// <summary>The condition's name in the log; shells get the enum and the value.</summary>
        public string ConditionName => Condition switch
        {
            WelcomeCondition.Uid => "UID",
            WelcomeCondition.FanClubLevel => "粉丝团等级",
            WelcomeCondition.Guard => "大航海身份",
            _ => "荣耀等级"
        };

        public int Length => WelcomeText.Length(Text);

        /// <summary>Why it is not sent, as shells show it; null when it can be sent.</summary>
        public string? RejectionReason => RejectedBecause switch
        {
            Rejection.EmptyText => "欢迎语拼出来是空的",
            Rejection.TooLong => FormattableString.Invariant($"欢迎语超过弹幕字数上限 {DanmakuLengthLimit}"),
            _ => null
        };
    }

    /// <summary>Null when the viewer meets no 欢迎条件; otherwise the first one met, in WelcomeCondition order.</summary>
    public Result? Evaluate(Entry entry, int danmakuLengthLimit)
    {
        (WelcomeCondition Condition, string? Value) match;
        if (settings.WelcomeUids.Value.Contains(entry.Uid))
            match = (WelcomeCondition.Uid, null);
        else if (settings.WelcomeFanClubLevel.Value > Settings.WelcomeLevelOff && entry.FanClubLevel >= settings.WelcomeFanClubLevel.Value)
            match = (WelcomeCondition.FanClubLevel, FormattableString.Invariant($"{entry.FanClubLevel}"));
        else if (guardThreshold > 0 && entry.GuardTier >= guardThreshold)
            match = (WelcomeCondition.Guard, entry.GuardName);
        else if (settings.WelcomeWealthLevel.Value > Settings.WelcomeLevelOff && entry.WealthLevel >= settings.WelcomeWealthLevel.Value)
            match = (WelcomeCondition.WealthLevel, FormattableString.Invariant($"{entry.WealthLevel}"));
        else return null;

        var welcomeText = welcomeTexts[random.Next(welcomeTexts.Length)];
        var text = welcomeText.Render(entry.Nickname, entry.GuardName, danmakuLengthLimit);
        // B站 refuses an empty danmaku, as "{大航海}" for a non-guard or a fully truncated nickname gives.
        Rejection? rejection = string.IsNullOrWhiteSpace(text) ? Rejection.EmptyText
            : WelcomeText.Length(text) > danmakuLengthLimit ? Rejection.TooLong : null;
        return new(match.Condition, match.Value, text, welcomeText.Mention, rejection, danmakuLengthLimit);
    }
}
