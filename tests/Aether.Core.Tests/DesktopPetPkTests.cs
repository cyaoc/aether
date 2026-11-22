using System.Text.Encodings.Web;
using System.Text.Json;
using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.OpponentCardFixture;

namespace Aether.Core.Tests;

/// <summary>What the 桌宠页 is told of a PK (#397), as B站's PK messages looked on 2026-11-22, without viewers and with a
/// made-up other streamer.</summary>
public sealed class DesktopPetPkTests
{
    // The room the harness watches.
    private const long Ours = 7734200;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_1_on_1_match_tells_the_other_streamer_as_looked_up_once_a_pk_info_says_it_is_one(bool infoFirst)
    {
        await using var h = new WatchHarness();
        Answer(h.Http);
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;

        await h.PushAndSyncAsync(updates, infoFirst ? [Info(h), PreNew()] : [PreNew(), Info(h)]);

        Assert.Equal("""{"name":"星星点灯","rank":{"name":"钻石传说","stars":3,"top":false,"place":458},"firstWin":false,"fans":34613,"guards":123,"top3":[{"score":12345,"wealth":31,"guard":3,"medal":25},{"score":5200,"wealth":22,"guard":0,"medal":0},{"score":880,"wealth":12,"guard":0,"medal":0}]}""",
            Text(await pet.NextAsync(DesktopPet.PkMatch)));
    }

    [Theory]
    [InlineData(101)]
    [InlineData(201)]
    [InlineData(301)]
    public async Task A_pk_info_while_a_pk_goes_on_puts_the_streamer_in_one(int status)
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;

        await h.PushAndSyncAsync(updates, Info(h, status: status));

        var states = await PkStatesAsync(pet);
        Assert.Equal([true], states);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(601)]
    public async Task A_pk_info_after_the_end_does_not_put_the_streamer_back_in_one(int status)
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;

        await h.PushAndSyncAsync(updates, Info(h), Votes("PK_BATTLE_END", (Ours, 10), (OpponentRoom, 20)), Info(h, status: status));

        var states = await PkStatesAsync(pet);
        Assert.Equal([true, false], states);
    }

    [Fact]
    public async Task An_opponent_B站_does_not_answer_about_in_3_seconds_is_told_without_it()
    {
        await using var h = new WatchHarness();
        var asked = new[] { Battle, RoomInfo, Guards, OnlineRankUrl }.Select(url =>
        {
            var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Http.Intercept(url, (_, token, _) => FakeBilibiliHttp.HangUntilCancelledAsync(requested, token));
            return requested.Task;
        }).ToArray();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;

        await h.PushAndSyncAsync(updates, PreNew(), Info(h));
        await Task.WhenAll(asked).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        h.Time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal("""{"name":"星星点灯","rank":null,"firstWin":null,"fans":null,"guards":null,"top3":[]}""",
            Text(await pet.NextAsync(DesktopPet.PkMatch)));
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(2, true)]
    public async Task A_pk_of_more_than_two_tells_only_that_the_streamer_is_in_a_pk(int type, bool third)
    {
        await using var h = new WatchHarness();
        Answer(h.Http);
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;
        string[] members = [Member(Ours, 10, Capsules((4, "差10票翻倍", Now(h) + 190), (5, "挑战成功", 0), (4, "2倍加成中", Now(h) + 5))),
            Member(OpponentRoom, 20), .. third ? [Member(3579000, 30)] : Array.Empty<string>()];

        await h.PushAndSyncAsync(updates, Info(h, type, members: members), PreNew(), Started(Now(h), countdown: Now(h) - 4),
            Votes("PK_BATTLE_PROCESS_NEW", (Ours, 10), (OpponentRoom, 20)), Votes("PK_BATTLE_END", (Ours, 10), (OpponentRoom, 20)));
        Assert.Empty(await ToldAsync(pet));
        // The other streamer of the next PK, a 1-on-1, is the first one told: the first PK's never is, however late it comes.
        await h.PushAndSyncAsync(updates, PreNew("月亮"), Info(h));

        Assert.Equal("月亮", (await pet.NextAsync(DesktopPet.PkMatch)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Scores_and_the_end_say_ours_and_theirs_by_room_on_whichever_side_ours_is()
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;

        await h.PushAndSyncAsync(updates, Info(h, members: [Member(OpponentRoom, 300), Member(Ours, 120)]),
            Votes("PK_BATTLE_PROCESS_NEW", (Ours, 500), (OpponentRoom, 300)), Votes("PK_BATTLE_PROCESS_NEW", (OpponentRoom, 900), (Ours, 700)),
            Votes("PK_BATTLE_END", (OpponentRoom, 1000), (Ours, 1200)));

        Assert.Equal(["pkScore {\"ours\":120,\"theirs\":300}", "pkScore {\"ours\":500,\"theirs\":300}", "pkScore {\"ours\":700,\"theirs\":900}",
            "pkEnd {\"ours\":1200,\"theirs\":1000}"], await ToldAsync(pet));
    }

    [Fact]
    public async Task Our_capsules_tell_the_condition_the_challenge_and_the_doubling_once_each_and_theirs_nothing()
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;
        var start = Now(h);
        string Both(string ours, string theirs) => Info(h, members: [Member(Ours, capsules: ours), Member(OpponentRoom, capsules: theirs)]);
        var first = Capsules((1, "首次送礼翻倍中", start + 30));
        var condition = Capsules((4, "差<%10%>票翻倍", start + 190), (5, "完成挑战获得<%2倍%>PK值", 0));
        var theirCondition = Capsules((4, "差1人翻倍", start + 190), (5, "完成挑战获得<%2倍%>PK值", 0));

        // As 31445880 had it on 2026-11-22, but their challenge failing first.
        await h.PushAndSyncAsync(updates, Both(first, first), Both(condition, theirCondition), Both(condition, theirCondition),
            Both(condition, Capsules((5, "挑战失败", 0))), Both(Capsules((4, "差0票翻倍", -1), (5, "挑战成功", 0)), "[]"),
            Both(Capsules((4, "即将2倍加成", start + 190), (5, "挑战成功", 0)), "[]"),
            Both(Capsules((4, "2倍加成中", start + 250), (5, "挑战成功", 0)), Capsules((4, "2倍加成中", start + 250))),
            Both(Capsules((4, "2倍加成中", start + 250), (5, "挑战成功", 0)), "[]"));

        Assert.Equal(["pkCondition {\"text\":\"差10票翻倍\"}", "pkChallenge {\"result\":\"Done\"}", "pkDouble {}"],
            (await ToldAsync(pet)).Where(told => !told.StartsWith(DesktopPet.PkScore, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task The_last_10_seconds_are_told_at_pk_countdown_by_the_servers_clock_however_far_ahead_the_local_one_is(int localAhead)
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;
        var sent = Now(h) - localAhead;

        await h.PushAndSyncAsync(updates, Info(h), Started(sent, countdown: sent + 12));
        Assert.Empty(await ToldAsync(pet, DesktopPet.PkCountdown));
        h.Time.Advance(TimeSpan.FromSeconds(12) - TimeSpan.FromMilliseconds(1));
        await h.PushAndSyncAsync(updates);
        Assert.Empty(await ToldAsync(pet, DesktopPet.PkCountdown));
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        await h.PushAndSyncAsync(updates);

        Assert.Equal(["pkCountdown {\"kind\":\"last\",\"leftMs\":10000}"], await ToldAsync(pet, DesktopPet.PkCountdown));
    }

    [Theory]
    [InlineData(4, "pkCountdown {\"kind\":\"last\",\"leftMs\":6000}")]
    [InlineData(10, null)]
    public async Task Known_late_the_countdown_is_told_at_once_with_less_left_and_not_once_its_moment_passed(int late, string? told)
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;

        await h.PushAndSyncAsync(updates, Info(h), Started(Now(h), countdown: Now(h) - late));

        Assert.Equal(told is null ? [] : [told], await ToldAsync(pet, DesktopPet.PkCountdown));
    }

    [Fact]
    public async Task The_doubling_is_counted_down_10_seconds_before_it_ends()
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;

        await h.PushAndSyncAsync(updates, Info(h, members: [Member(Ours, capsules: Capsules((4, "2倍加成中", Now(h) + 20))), Member(OpponentRoom)]));
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.PushAndSyncAsync(updates);
        Assert.Equal(["pkDouble {}"], await ToldAsync(pet, DesktopPet.PkDouble));

        Assert.Equal(["pkCountdown {\"kind\":\"double\",\"leftMs\":10000}"], await ToldAsync(pet, DesktopPet.PkCountdown));
    }

    [Fact]
    public async Task A_countdown_not_told_yet_is_not_told_once_the_pk_ends()
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;

        await h.PushAndSyncAsync(updates, Info(h, members: [Member(Ours, capsules: Capsules((4, "2倍加成中", Now(h) + 15))), Member(OpponentRoom)]),
            Started(Now(h), countdown: Now(h) + 5), Votes("PK_BATTLE_END", (Ours, 0), (OpponentRoom, 0)));
        h.Time.Advance(TimeSpan.FromSeconds(6));
        await h.PushAndSyncAsync(updates);

        Assert.Equal(["pkScore {\"ours\":0,\"theirs\":0}", "pkDouble {}", "pkEnd {\"ours\":0,\"theirs\":0}"], await ToldAsync(pet));
        Assert.Empty(await ToldAsync(pet));
    }

    private static long Now(WatchHarness h) => h.Time.GetUtcNow().ToUnixTimeSeconds();

    private static string PreNew(string name = OpponentName) =>
        $$"""{"cmd":"PK_BATTLE_PRE_NEW","pk_status":101,"pk_id":100001,"timestamp":1702204169,"data":{"battle_type":1,"match_type":1,"uname":"{{name}}","face":"","uid":{{OpponentUid}},"room_id":{{OpponentRoom}},"season_id":61,"pre_timer":10,"pk_votes_name":"PK值","end_win_task":null},"roomid":{{Ours}}}""";

    /// <summary>A PK_INFO sent now by B站's clock, by default of a random 1-on-1 PK with no votes yet.</summary>
    private static string Info(WatchHarness h, int type = 2, int status = 201, params string[] members) =>
        $$$"""{"cmd":"PK_INFO","data":{"members":[{{{string.Join(",", members is [] ? [Member(Ours), Member(OpponentRoom)] : members)}}}],"pk_basic":{"pk_id":100001,"status":{{{status}}},"type":{{{type}}},"sub_type":0,"start_time":{{{Now(h)}}},"end_time":{{{Now(h) + 300}}}},"pk_play":{},"timestamp":{{{Now(h)}}}}}""";

    private static string Member(long room, long votes = 0, string capsules = "[]") =>
        $$"""{"uid":{{room + 1}},"room_id":{{room}},"uname":"主播","votes":{{votes}},"golds":{{votes * 100}},"capsules_v2":{{capsules}}}""";

    private static string Capsules(params (int Type, string Text, long End)[] capsules) => JsonSerializer.Serialize(
        capsules.Select(capsule => new { capsule_type = capsule.Type, text = capsule.Text, end_time = capsule.End, url = "" }));

    /// <summary>A PK_BATTLE_PROCESS_NEW or PK_BATTLE_END with these votes, init_info's then match_info's.</summary>
    private static string Votes(string command, (long Room, long Votes) init, (long Room, long Votes) match) => JsonSerializer.Serialize(new
    {
        cmd = command, pk_id = 100001, pk_status = 201, timestamp = 1702204469,
        data = new
        {
            battle_type = 1, init_info = new { room_id = init.Room, votes = init.Votes, best_uname = "" },
            match_info = new { room_id = match.Room, votes = match.Votes, best_uname = "" }
        }
    });

    /// <summary>The PK events, or those named <paramref name="only"/>, the page was told before the danmaku a push synced with,
    /// each its name and what it said.</summary>
    private static async Task<string[]> ToldAsync(DesktopPetEvents pet, string only = "pk")
    {
        var told = new List<string>();
        while (await pet.NextAsync() is var (name, data) && name != DesktopPet.Danmaku)
            if (name.StartsWith(only, StringComparison.Ordinal)) told.Add($"{name} {Text(data)}");
        return [.. told];
    }

    /// <summary>Whether each state the page was told before the danmaku a push synced with says the streamer is in a PK.</summary>
    private static async Task<bool[]> PkStatesAsync(DesktopPetEvents pet) =>
        [.. (await ToldAsync(pet, "state")).Select(state => state.Contains("\"pk\":true", StringComparison.Ordinal))];

    /// <summary>A PK_BATTLE_START_NEW sent at <paramref name="sent"/> by B站's clock, its last 10 seconds starting at
    /// <paramref name="countdown"/>.</summary>
    private static string Started(long sent, long countdown) => JsonSerializer.Serialize(new
    {
        cmd = "PK_BATTLE_START_NEW", pk_id = 100001, pk_status = 201, timestamp = sent,
        data = new
        {
            battle_type = 1, final_hit_votes = 0, pk_start_time = sent, pk_frozen_time = countdown + 10, pk_end_time = countdown + 20,
            pk_votes_type = 0, pk_votes_add = 0, pk_votes_name = "PK值", star_light_msg = "", pk_countdown = countdown,
            final_conf = new { @switch = 0, start_time = 0, end_time = 0 },
            init_info = new { room_id = Ours, date_streak = 0 }, match_info = new { room_id = OpponentRoom, date_streak = 0 }
        },
        roomid = $"{Ours}"
    });

    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>What the page was told, as compact JSON that writes Chinese as it is.</summary>
    private static string Text(JsonElement told) => JsonSerializer.Serialize(told, Readable);
}
