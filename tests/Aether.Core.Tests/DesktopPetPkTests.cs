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

    private static long Now(WatchHarness h) => h.Time.GetUtcNow().ToUnixTimeSeconds();


    /// <summary>A PK_INFO sent now by B站's clock, by default of a random 1-on-1 PK with no votes yet.</summary>
    private static string Info(WatchHarness h, int type = 2, int status = 201, params string[] members) =>
        $$$"""{"cmd":"PK_INFO","data":{"members":[{{{string.Join(",", members is [] ? [Member(Ours), Member(OpponentRoom)] : members)}}}],"pk_basic":{"pk_id":100001,"status":{{{status}}},"type":{{{type}}},"sub_type":0,"start_time":{{{Now(h)}}},"end_time":{{{Now(h) + 300}}}},"pk_play":{},"timestamp":{{{Now(h)}}}}}""";

    private static string Member(long room, long votes = 0, string capsules = "[]") =>
        $$"""{"uid":{{room + 1}},"room_id":{{room}},"uname":"主播","votes":{{votes}},"golds":{{votes * 100}},"capsules_v2":{{capsules}}}""";

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

    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>What the page was told, as compact JSON that writes Chinese as it is.</summary>
    private static string Text(JsonElement told) => JsonSerializer.Serialize(told, Readable);
}
