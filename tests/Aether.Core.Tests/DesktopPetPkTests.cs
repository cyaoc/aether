using System.Text.Encodings.Web;
using System.Text.Json;
using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.OpponentCardFixture;

namespace Aether.Core.Tests;

/// <summary>What the 桌宠页 is told of a PK (#397), as B站's PK messages looked on 2026-11-22, without viewers and with a
/// made-up other streamer.</summary>
public sealed class DesktopPetPkTests : IAsyncDisposable
{
    // The room the harness watches; B站's pk_id of the PK its messages are of, and of the next.
    private const long Ours = 7734200, PkId = 100001, NextPk = 100002;
    private readonly WatchHarness h = new();
    // The room watched and the 桌宠页 opened (WatchAsync), gone before the harness.
    private IAsyncEnumerator<WatchUpdate>? watch;
    private DesktopPetEvents? page;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_1_on_1_match_tells_the_other_streamer_as_looked_up_once_a_pk_info_says_it_is_one(bool infoFirst)
    {
        Answer(h.Http);
        var (updates, pet) = await WatchAsync();

        await h.PushAndSyncAsync(updates, infoFirst ? [Info(), PreNew()] : [PreNew(), Info()]);

        Assert.Equal("""{"name":"星星点灯","rank":{"name":"钻石传说","stars":3,"top":false,"place":458},"firstWin":false,"fans":34613,"guards":123,"top3":[{"score":12345,"wealth":31,"guard":3,"medal":25},{"score":5200,"wealth":22,"guard":0,"medal":0},{"score":880,"wealth":12,"guard":0,"medal":0}]}""",
            Text(await pet.NextAsync(DesktopPet.PkMatch)));
    }

    [Theory]
    [InlineData(101)]
    [InlineData(201)]
    [InlineData(301)]
    public async Task A_pk_info_while_a_pk_goes_on_puts_the_streamer_in_one(int status)
    {
        var (updates, pet) = await WatchAsync();

        await h.PushAndSyncAsync(updates, Info(status: status));

        var states = await PkStatesAsync(pet);
        Assert.Equal([true], states);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(601)]
    public async Task A_pk_info_after_the_end_does_not_put_the_streamer_back_in_one(int status)
    {
        var (updates, pet) = await WatchAsync();

        await h.PushAndSyncAsync(updates, Info(), Votes("PK_BATTLE_END", (Ours, 10), (OpponentRoom, 20)), Info(status: status));

        var states = await PkStatesAsync(pet);
        Assert.Equal([true, false], states);
    }

    [Fact]
    public async Task An_opponent_B站_does_not_answer_about_in_3_seconds_is_told_without_it()
    {
        var asked = LookUpsHang();
        var (updates, pet) = await WatchAsync();

        await h.PushAndSyncAsync(updates, PreNew(), Info());
        await GiveUpAsync(asked);

        Assert.Equal("""{"name":"星星点灯","rank":null,"firstWin":null,"fans":null,"guards":null,"top3":[]}""",
            Text(await pet.NextAsync(DesktopPet.PkMatch)));
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(2, true)]
    public async Task A_pk_of_more_than_two_tells_only_that_the_streamer_is_in_a_pk(int type, bool third)
    {
        Answer(h.Http);
        var (updates, pet) = await WatchAsync();
        string[] members = [Member(Ours, 10, Capsules((4, "差10票翻倍", Now() + 190), (5, "挑战成功", 0), (4, "2倍加成中", Now() + 5))),
            Member(OpponentRoom, 20), .. third ? [Member(3579000, 30)] : Array.Empty<string>()];

        await h.PushAndSyncAsync(updates, Info(type, members: members), PreNew(), Started(Now(), countdown: Now() - 4),
            Votes("PK_BATTLE_PROCESS_NEW", (Ours, 10), (OpponentRoom, 20)), Votes("PK_BATTLE_END", (Ours, 10), (OpponentRoom, 20)));
        Assert.Empty(await ToldAsync(pet));
        // The other streamer of the next PK, a 1-on-1, is the first one told: the first PK's never is, however late it comes.
        await h.PushAndSyncAsync(updates, PreNew("月亮"), Info());

        Assert.Equal("月亮", (await pet.NextAsync(DesktopPet.PkMatch)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_match_after_a_pk_whose_end_never_came_is_a_new_pk_its_other_streamer_told()
    {
        Answer(h.Http);
        var (updates, pet) = await WatchAsync();
        await h.PushAndSyncAsync(updates, PreNew(), Info(), PreNew());
        Assert.Equal(OpponentName, (await pet.NextAsync(DesktopPet.PkMatch)).GetProperty("name").GetString());

        await h.PushAndSyncAsync(updates, PreNew("月亮", NextPk), Info(pk: NextPk));

        Assert.Equal("月亮", (await pet.NextAsync(DesktopPet.PkMatch)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_pk_joined_part_way_through_whose_end_never_came_leaves_the_next_pk_its_doubling_and_countdown()
    {
        var (updates, pet) = await WatchAsync();
        await h.Server.DisconnectAsync();
        Assert.IsType<Reconnecting>(await h.NextAsync(updates));
        await h.AdvanceRetryAsync(updates, 1);
        string Doubling(long pk) => Info(pk: pk, members: [Member(Ours, capsules: Capsules((4, "2倍加成中", Now() + 20))), Member(OpponentRoom)]);

        await h.PushAndSyncAsync(updates, Doubling(PkId), PreNew(pk: NextPk), Doubling(NextPk));
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.PushAndSyncAsync(updates);
        Assert.Equal(["pkDouble {}"], await ToldAsync(pet, DesktopPet.PkDouble));

        Assert.Equal(["pkCountdown {\"kind\":\"double\",\"leftMs\":10000}"], await ToldAsync(pet, DesktopPet.PkCountdown));
    }

    [Fact]
    public async Task A_pk_joined_part_way_through_after_a_reconnect_only_puts_the_streamer_in_one()
    {
        var (updates, pet) = await WatchAsync();
        await h.PushAndSyncAsync(updates, PreNew(), Info());
        await ToldAsync(pet);
        await h.Server.DisconnectAsync();
        Assert.IsType<Reconnecting>(await h.NextAsync(updates));
        await h.AdvanceRetryAsync(updates, 1);

        await h.PushAndSyncAsync(updates, Info(members: [Member(Ours, 500, Capsules((4, "2倍加成中", Now() + 100), (5, "挑战成功", 0))), Member(OpponentRoom, 100)]),
            Votes("PK_BATTLE_PROCESS_NEW", (Ours, 900), (OpponentRoom, 100)), Votes("PK_BATTLE_END", (Ours, 1000), (OpponentRoom, 100)));

        // each state the page was told as whether it says the streamer is in a PK, and anything else as it was told
        Assert.Equal(["no PK", "PK", "no PK"], (await ToldAsync(pet, "")).Select(told => told.StartsWith("state ", StringComparison.Ordinal)
            ? SaysInPk(told) ? "PK" : "no PK" : told));
    }

    [Fact]
    public async Task Scores_and_the_end_say_ours_and_theirs_by_room_on_whichever_side_ours_is()
    {
        var (updates, pet) = await WatchAsync();

        await h.PushAndSyncAsync(updates, PreNew(), Info(members: [Member(OpponentRoom, 300), Member(Ours, 120)]),
            Votes("PK_BATTLE_PROCESS_NEW", (Ours, 500), (OpponentRoom, 300)), Votes("PK_BATTLE_PROCESS_NEW", (OpponentRoom, 900), (Ours, 700)),
            Votes("PK_BATTLE_END", (OpponentRoom, 1000), (Ours, 1200)));

        Assert.Equal(["pkScore {\"ours\":120,\"theirs\":300}", "pkScore {\"ours\":500,\"theirs\":300}", "pkScore {\"ours\":700,\"theirs\":900}",
            "pkEnd {\"ours\":1200,\"theirs\":1000}"], await ToldAsync(pet));
    }

    [Fact]
    public async Task Our_capsules_tell_the_condition_the_challenge_and_the_doubling_once_each_and_theirs_nothing()
    {
        var (updates, pet) = await WatchAsync();
        var start = Now();
        string Both(string ours, string theirs) => Info(members: [Member(Ours, capsules: ours), Member(OpponentRoom, capsules: theirs)]);
        var first = Capsules((1, "首次送礼翻倍中", start + 30));
        var condition = Capsules((4, "差<%10%>票翻倍", start + 190), (5, "完成挑战获得<%2倍%>PK值", 0));
        var theirCondition = Capsules((4, "差1人翻倍", start + 190), (5, "完成挑战获得<%2倍%>PK值", 0));

        // As a PK went on 2026-11-22 (docs/research/2026-11-22-pk-data.md), but their challenge failing first.
        await h.PushAndSyncAsync(updates, PreNew(), Both(first, first), Both(condition, theirCondition), Both(condition, theirCondition),
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
        var (updates, pet) = await WatchAsync();
        var sent = Now() - localAhead;

        await h.PushAndSyncAsync(updates, PreNew(), Info(), Started(sent, countdown: sent + 12));
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
        var (updates, pet) = await WatchAsync();

        await h.PushAndSyncAsync(updates, PreNew(), Info(), Started(Now(), countdown: Now() - late));

        Assert.Equal(told is null ? [] : [told], await ToldAsync(pet, DesktopPet.PkCountdown));
    }

    [Fact]
    public async Task The_doubling_is_counted_down_10_seconds_before_it_ends()
    {
        var (updates, pet) = await WatchAsync();

        await h.PushAndSyncAsync(updates, PreNew(), Info(members: [Member(Ours, capsules: Capsules((4, "2倍加成中", Now() + 20))), Member(OpponentRoom)]));
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.PushAndSyncAsync(updates);
        Assert.Equal(["pkDouble {}"], await ToldAsync(pet, DesktopPet.PkDouble));

        Assert.Equal(["pkCountdown {\"kind\":\"double\",\"leftMs\":10000}"], await ToldAsync(pet, DesktopPet.PkCountdown));
    }

    [Fact]
    public async Task A_pk_info_without_its_timestamp_is_told_all_the_same_but_the_doubling_is_not_counted_down()
    {
        var asked = LookUpsHang();
        var (updates, pet) = await WatchAsync();
        var info = Info(members: [Member(Ours, 10, Capsules((4, "2倍加成中", Now() + 20), (5, "挑战成功", 0))), Member(OpponentRoom, 20)]);

        await h.PushAndSyncAsync(updates, PreNew(), WithoutTimestamp(info));
        Assert.Equal(["pkScore {\"ours\":10,\"theirs\":20}", "pkDouble {}", "pkChallenge {\"result\":\"Done\"}"], await ToldAsync(pet));
        await GiveUpAsync(asked);
        Assert.Equal(OpponentName, (await pet.NextAsync(DesktopPet.PkMatch)).GetProperty("name").GetString());
        h.Time.Advance(TimeSpan.FromSeconds(7));
        await h.PushAndSyncAsync(updates);

        Assert.Empty(await ToldAsync(pet));
    }

    [Fact]
    public async Task A_doubling_first_told_without_a_timestamp_is_counted_down_by_the_next_pk_info_that_has_one()
    {
        var (updates, pet) = await WatchAsync();
        var info = Info(members: [Member(Ours, capsules: Capsules((4, "2倍加成中", Now() + 20))), Member(OpponentRoom)]);

        await h.PushAndSyncAsync(updates, PreNew(), WithoutTimestamp(info), info, info);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.PushAndSyncAsync(updates);
        Assert.Equal(["pkDouble {}"], await ToldAsync(pet, DesktopPet.PkDouble));

        Assert.Equal(["pkCountdown {\"kind\":\"double\",\"leftMs\":10000}"], await ToldAsync(pet, DesktopPet.PkCountdown));
    }

    [Theory]
    [InlineData("""{"capsule_type":5,"end_time":0,"url":""}""")]
    [InlineData("""{"capsule_type":5,"text":"挑战成功","url":""}""")]
    public async Task A_capsule_without_its_text_or_end_time_is_left_out_and_the_others_told(string capsule)
    {
        var (updates, pet) = await WatchAsync();
        var capsules = $$"""[{{capsule}},{"capsule_type":4,"text":"差10票翻倍","end_time":{{Now() + 190}},"url":""}]""";

        await h.PushAndSyncAsync(updates, PreNew(), Info(members: [Member(Ours, capsules: capsules), Member(OpponentRoom)]));

        Assert.Equal(["pkScore {\"ours\":0,\"theirs\":0}", "pkCondition {\"text\":\"差10票翻倍\"}"], await ToldAsync(pet));
    }

    [Fact]
    public async Task A_countdown_not_told_yet_is_not_told_once_the_pk_ends()
    {
        var (updates, pet) = await WatchAsync();

        await h.PushAndSyncAsync(updates, PreNew(), Info(members: [Member(Ours, capsules: Capsules((4, "2倍加成中", Now() + 15))), Member(OpponentRoom)]),
            Started(Now(), countdown: Now() + 5), Votes("PK_BATTLE_END", (Ours, 0), (OpponentRoom, 0)));
        h.Time.Advance(TimeSpan.FromSeconds(6));
        await h.PushAndSyncAsync(updates);

        Assert.Equal(["pkScore {\"ours\":0,\"theirs\":0}", "pkDouble {}", "pkEnd {\"ours\":0,\"theirs\":0}"], await ToldAsync(pet));
        Assert.Empty(await ToldAsync(pet));
    }

    /// <summary>Turns the 桌宠 on, watches the room and opens the 桌宠页 (DesktopPetEvents.WatchAsync), both gone with the test.</summary>
    private async Task<(IAsyncEnumerator<WatchUpdate> Updates, DesktopPetEvents Pet)> WatchAsync()
    {
        var watched = await DesktopPetEvents.WatchAsync(h);
        (watch, page) = watched;
        return watched;
    }

    public async ValueTask DisposeAsync()
    {
        if (watch is not null) await watch.DisposeAsync();
        page?.Dispose();
        await h.DisposeAsync();
    }

    private long Now() => h.Time.GetUtcNow().ToUnixTimeSeconds();

    private static string PreNew(string name = OpponentName, long pk = PkId) =>
        $$"""{"cmd":"PK_BATTLE_PRE_NEW","pk_status":101,"pk_id":{{pk}},"timestamp":1702204169,"data":{"battle_type":1,"match_type":1,"uname":"{{name}}","face":"","uid":{{OpponentUid}},"room_id":{{OpponentRoom}},"season_id":61,"pre_timer":10,"pk_votes_name":"PK值","end_win_task":null},"roomid":{{Ours}}}""";

    /// <summary>A PK_INFO sent now by B站's clock, by default of a random 1-on-1 PK with no votes yet.</summary>
    private string Info(int type = 2, int status = 201, long pk = PkId, params string[] members) =>
        $$$"""{"cmd":"PK_INFO","data":{"members":[{{{string.Join(",", members is [] ? [Member(Ours), Member(OpponentRoom)] : members)}}}],"pk_basic":{"pk_id":{{{pk}}},"status":{{{status}}},"type":{{{type}}},"sub_type":0,"start_time":{{{Now()}}},"end_time":{{{Now() + 300}}}},"pk_play":{},"timestamp":{{{Now()}}}}}""";

    private string WithoutTimestamp(string info) => info.Replace($",\"timestamp\":{Now()}", "", StringComparison.Ordinal);

    private static string Member(long room, long votes = 0, string capsules = "[]") =>
        $$"""{"uid":{{room + 1}},"room_id":{{room}},"uname":"主播","votes":{{votes}},"golds":{{votes * 100}},"capsules_v2":{{capsules}}}""";

    private static string Capsules(params (int Type, string Text, long End)[] capsules) => JsonSerializer.Serialize(
        capsules.Select(capsule => new { capsule_type = capsule.Type, text = capsule.Text, end_time = capsule.End, url = "" }));

    /// <summary>A PK_BATTLE_PROCESS_NEW or PK_BATTLE_END with these votes, init_info's then match_info's.</summary>
    private static string Votes(string command, (long Room, long Votes) init, (long Room, long Votes) match) => JsonSerializer.Serialize(new
    {
        cmd = command, pk_id = PkId, pk_status = 201, timestamp = 1702204469,
        data = new
        {
            battle_type = 1, init_info = new { room_id = init.Room, votes = init.Votes, best_uname = "" },
            match_info = new { room_id = match.Room, votes = match.Votes, best_uname = "" }
        }
    });

    /// <summary>The PK events, or those named <paramref name="only"/>, the page was told before the danmaku a push synced with,
    /// each its name and what it said; but the other streamer, told whenever B站 answers about them.</summary>
    private static async Task<string[]> ToldAsync(DesktopPetEvents pet, string only = "pk")
    {
        var told = new List<string>();
        while (await pet.NextAsync() is var (name, data) && name != DesktopPet.Danmaku)
            if (name.StartsWith(only, StringComparison.Ordinal) && name != DesktopPet.PkMatch) told.Add($"{name} {Text(data)}");
        return [.. told];
    }

    /// <summary>Has B站 not answer about the other streamer until the lookup gives up; done once it was asked all four.</summary>
    private Task LookUpsHang() => Task.WhenAll(LookUps.Select(url =>
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Http.Intercept(url, (_, token, _) => FakeBilibiliHttp.HangUntilCancelledAsync(requested, token));
        return requested.Task;
    }));

    /// <summary>Once the lookup has <paramref name="asked"/> all four, lets its 3 seconds pass: it gives up.</summary>
    private async Task GiveUpAsync(Task asked)
    {
        await asked.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        h.Time.Advance(TimeSpan.FromSeconds(3));
    }

    /// <summary>Whether each state the page was told before the danmaku a push synced with says the streamer is in a PK.</summary>
    private static async Task<bool[]> PkStatesAsync(DesktopPetEvents pet) => [.. (await ToldAsync(pet, "state")).Select(SaysInPk)];

    private static bool SaysInPk(string state) => state.Contains("\"pk\":true", StringComparison.Ordinal);

    /// <summary>A PK_BATTLE_START_NEW sent at <paramref name="sent"/> by B站's clock, its last 10 seconds starting at
    /// <paramref name="countdown"/>.</summary>
    private static string Started(long sent, long countdown) => JsonSerializer.Serialize(new
    {
        cmd = "PK_BATTLE_START_NEW", pk_id = PkId, pk_status = 201, timestamp = sent,
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
