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
}
