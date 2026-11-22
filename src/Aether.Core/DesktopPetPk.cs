using static Aether.Core.DanmakuProtocol;

namespace Aether.Core;

/// <summary>One PK as the 桌宠页 is told it, from its first PK message to its end; part of the room connection region's state
/// (ADR 0017). Only a 1-on-1 PK is told, once a PK_INFO says it is one: the other streamer, looked up as the PK is matched,
/// waits for that. One not matched on this connection, joined part way through after a reconnect, is not told at all: its
/// 胶囊 and score would play as new.</summary>
internal sealed class DesktopPetPk(long roomId, StepTimers timers, Action<string, object> tell, Func<PkMatched, Task<OpponentCard>> lookUp)
{
    // pk_basic.type: 2 a random PK, 6 one the streamer was invited to; 8, a PK of more, and any other is no 1-on-1.
    private const int RandomPk = 2, InvitedPk = 6;
    // A side's 胶囊 by capsule_type: 4 the 双倍彩蛋's condition and then its doubling, 5 how its challenge came out.
    private const int DoubleCapsule = 4, ChallengeCapsule = 5;
    private const string Doubling = "2倍加成中";
    private static readonly Dictionary<string, string> Challenges = new() { ["挑战成功"] = "Done", ["挑战失败"] = "Failed" };
    // How long before the moment it counts down to a 举牌 is told: her sign counts its last 10 seconds.
    private static readonly TimeSpan SignLead = TimeSpan.FromSeconds(10);
    // A PK's last seconds (s), from its pk_countdown to when B站 stops counting its votes.
    private const long LastSeconds = 10;
    private readonly List<ITimer> countdowns = [];
    // What one PK tells once only.
    private readonly HashSet<string> once = [];
    private bool? oneOnOne;
    private bool doubleCounted;
    // Looked up, waiting for a PK_INFO to say whether the PK is 1-on-1.
    private OpponentCard? card;
    private bool over;

    /// <summary>Whether its match (PK_BATTLE_PRE_NEW) came on this connection: only then is it told.</summary>
    public bool Matched { get; private set; }

    public void Take(PkReceived received)
    {
        switch (received.News)
        {
            case PkMatched matched:
                Matched = true;
                LookUp(matched);
                break;
            case PkStarted started:
                Countdown("last", started.Countdown + LastSeconds, started.Timestamp, received.ReceivedTimestamp);
                break;
            case PkScored scored:
                Score(Sides(scored.Sides), received.InPk ? DesktopPet.PkScore : DesktopPet.PkEnd);
                break;
            case PkInfo info:
                TakeInfo(info, received.ReceivedTimestamp);
                break;
        }
    }

    /// <summary>The PK is over: what it has not told yet it never tells, however late that comes.</summary>
    public void End()
    {
        over = true;
        foreach (var countdown in countdowns) countdown.Dispose();
    }

    private void LookUp(PkMatched matched) => _ = Task.Run(async () =>
    {
        var looked = await lookUp(matched);
        timers.Post(() =>
        {
            card = looked;
            TellMatch();
        });
    });

    private void TakeInfo(PkInfo info, long receivedAt)
    {
        var sides = Sides(info.Sides);
        oneOnOne = info.Type is RandomPk or InvitedPk && sides is not null;
        TellMatch();
        Score(sides, DesktopPet.PkScore);
        if (sides is var (ours, _))
            foreach (var capsule in ours.Capsules) TakeCapsule(capsule, info.Timestamp, receivedAt);
    }

    /// <summary>Only our 双倍彩蛋 is told: the condition the first time it is one, as "差10票翻倍" or "差1人翻倍" is, how the
    /// challenge came out and the doubling, each once; the doubling counted down to its end_time too, by B站's clock when the
    /// PK_INFO said what it was.</summary>
    private void TakeCapsule(PkCapsule capsule, long? sent, long receivedAt)
    {
        if (capsule is { Type: DoubleCapsule, Text: Doubling })
        {
            TellOnce(DesktopPet.PkDouble, new { });
            // counted down by the first PK_INFO with a timestamp, which need not be the first to tell the doubling
            if (sent is { } at && !doubleCounted) { doubleCounted = true; Countdown("double", capsule.EndTime, at, receivedAt); }
        }
        else if (capsule.Type == DoubleCapsule && capsule.Text.StartsWith('差') && capsule.Text.EndsWith("翻倍", StringComparison.Ordinal))
            TellOnce(DesktopPet.PkCondition, new { text = capsule.Text });
        else if (capsule.Type == ChallengeCapsule && Challenges.TryGetValue(capsule.Text, out var result))
            TellOnce(DesktopPet.PkChallenge, new { result });
    }

    /// <summary>Counts down to <paramref name="moment"/>, by the clock of B站 that sent the message at <paramref name="sent"/>,
    /// which came at <paramref name="receivedAt"/>: the local clock may be off. Told 10 seconds before; at once, with less left,
    /// when that is past; not at all once the moment is.</summary>
    private void Countdown(string kind, long moment, long sent, long receivedAt)
    {
        var left = Left();
        if (left > SignLead) countdowns.Add(timers.Start(left - SignLead, () => TellCountdown(Left())));
        else if (left > TimeSpan.Zero) TellCountdown(left);

        TimeSpan Left() => TimeSpan.FromSeconds(moment - sent) - timers.Time.GetElapsedTime(receivedAt);
        void TellCountdown(TimeSpan remaining) => Tell(DesktopPet.PkCountdown, new { kind, leftMs = (long)remaining.TotalMilliseconds });
    }

    private void TellOnce(string name, object data)
    {
        if (once.Add(name)) Tell(name, data);
    }

    private void TellMatch()
    {
        if (card is null || oneOnOne is null) return;
        Tell(DesktopPet.PkMatch, card);
        card = null;
    }

    private void Score((PkSide Ours, PkSide Theirs)? sides, string name)
    {
        if (sides is var (ours, theirs)) Tell(name, new { ours = ours.Votes, theirs = theirs.Votes });
    }

    /// <summary>Our side and theirs, by room: none unless there are two and one is ours.</summary>
    private (PkSide Ours, PkSide Theirs)? Sides(PkSide[] sides) => sides switch
    {
        [var first, var second] when first.RoomId == roomId => (first, second),
        [var first, var second] when second.RoomId == roomId => (second, first),
        _ => null
    };

    private void Tell(string name, object data)
    {
        if (oneOnOne == true && Matched && !over) tell(name, data);
    }
}
