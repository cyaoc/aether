using System.Text.Json.Nodes;

namespace Aether.Core.Tests;

/// <summary>Every character is drawn whole (#343): the code only knows pictures by the names in her manifest and draws with its
/// numbers, and nothing at run time makes up for a missing one.</summary>
public sealed class CharacterTests
{
    private static readonly string Characters = Path.Combine(AppContext.BaseDirectory, "Pages", "characters");

    public static TheoryData<string> Folders => [.. Directory.GetDirectories(Characters).Select(Path.GetFileName).OfType<string>()];

    [Theory]
    [MemberData(nameof(Folders))]
    public void A_character_and_her_background_have_every_picture_and_every_number_their_manifests_call_for(string name)
    {
        var folder = Path.Combine(Characters, name);
        // A manifest that leaves out a picture or a number does not load.
        var character = Character.Load(folder);
        AssertPictures(folder, "character.json");
        var background = Background.Folder(character.Gui.Background);
        Background.Load(background);
        AssertPictures(background, "background.json");
    }

    private static void AssertPictures(string folder, string manifest)
    {
        foreach (var picture in Strings(JsonNode.Parse(File.ReadAllText(Path.Combine(folder, manifest)))).Where(text => text.EndsWith(".webp", StringComparison.Ordinal)))
            Assert.True(File.Exists(Path.Combine(folder, picture)), $"{folder} has no {picture}");
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonObject members => members.SelectMany(member => Strings(member.Value)),
        JsonArray items => items.SelectMany(Strings),
        JsonValue value when value.TryGetValue<string>(out var text) => [text],
        _ => []
    };
}
