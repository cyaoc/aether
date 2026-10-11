using System.Text.Json.Nodes;

namespace Aether.Core.Tests.Support;

/// <summary>The V1 blind box SEND_GIFT: BAC's SEND_GIFT shape with third-party blind_gift fields, not our own capture;
/// see Fixtures/README.md.</summary>
internal static class BlindGiftFixture
{
    public static string Json => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "blind-gift-v1.json"));

    /// <summary>The fixture with <paramref name="edit"/> applied to its data object.</summary>
    public static string With(Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(Json)!;
        edit(root["data"]!.AsObject());
        return root.ToJsonString();
    }

    /// <summary>One fresh blind box (its own tid) sent at <paramref name="sentAt"/>; the rest keeps the fixture's values
    /// unless given.</summary>
    public static string Gift(DateTimeOffset sentAt, long uid = 10001, long spend = 5000, long price = 1500,
        string? nickname = null, long? blindId = null) => With(data =>
    {
        data["uid"] = uid;
        data["total_coin"] = spend;
        data["price"] = price;
        data["timestamp"] = sentAt.ToUnixTimeSeconds();
        data["tid"] = Guid.NewGuid().ToString();
        if (nickname is not null) data["uname"] = nickname;
        if (blindId is not null) data["blind_gift"]!["original_gift_id"] = blindId;
    });
}
