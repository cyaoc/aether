using System.Text.Json;

namespace Aether.Core.Entries;

internal static class EntryEffect
{
    public static Entry Decode(JsonElement data)
    {
        var user = Field(data, "uinfo");
        var medal = Field(user, "medal");
        var anchor = Number(data, "target_id");
        var name = Field(Field(user, "base"), "name");
        return new Entry(Number(data, "uid"), name.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "" : name.GetString()!, 1,
            Number(Field(user, "guard"), "level"),
            anchor > 0 && Number(medal, "ruid") == anchor ? Number(medal, "level") : null,
            Number(Field(user, "wealth"), "level"), Number(data, "trigger_time"));
    }

    private static JsonElement Field(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) ? field : default;

    private static long Number(JsonElement value, string name) =>
        Field(value, name) is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } field ? field.GetInt64() : 0;
}
