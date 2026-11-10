using System.Text.Json;

namespace Aether.Core;

/// <summary>A character (角色): a folder under Pages/characters with her pictures and a manifest, character.json, that names them
/// and holds the numbers measured from them, in a section for each use, and her lines for the 桌宠, by kind of the 演出表. Code
/// knows her pictures by the manifest's names and draws with its numbers, never by who she is (#343).</summary>
public sealed record Character(CharacterGui Gui, CharacterSprite Sprite, CharacterGiftBoard GiftBoard,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Lines)
{
    /// <summary>The current character's number, said in this one place: #343 fixes her to 1 and a later setting picks her. Pages
    /// ask for her files as /characters/current/.</summary>
    internal const string CurrentNumber = "1";

    // A manifest is outside input: one that leaves out a picture or a number does not load.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true };

    /// <summary>The current character's folder, in the program's Pages.</summary>
    public static string CurrentFolder { get; } = Path.Combine(WebServer.PagesDirectory, "characters", CurrentNumber);

    /// <summary>The current character's manifest.</summary>
    public static Character Current { get; } = Load(CurrentFolder);

    /// <summary>The manifest in <paramref name="folder"/>.</summary>
    public static Character Load(string folder) => Read<Character>(folder, "character.json");

    internal static T Read<T>(string folder, string manifest)
    {
        var file = Path.Combine(folder, manifest);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) ?? throw new JsonException($"{file} is null");
    }
}

/// <summary>Her layers for the GUI's splash and login page, each as big as the background she picks under it, a folder under
/// Pages/backgrounds.</summary>
public sealed record CharacterGui(string Background, string Splash, string Login);

/// <summary>Her sprite (精灵), idle, blinking, looking right and happy, on one canvas at one scale.</summary>
public sealed record CharacterSprite(string Idle, string Blink, string LookRight, string Happy);

/// <summary>Her two poses on the gift board, idle and cheering, on one canvas at one scale, and what was measured on them in the
/// board's design pixels: how high a pose is drawn, where the ribbon cuts each off (a fraction of the picture's height), where the
/// sprite floats by her shoulder and where the cheer's rays come from (y from the ribbon's top), and how high the sprite's picture
/// is drawn beside her.</summary>
public sealed record CharacterGiftBoard(string Idle, string Cheer, double Height, double IdleCut, double CheerCut, BoardPoint Shoulder, BoardPoint CheerCenter, double SpriteSize);

/// <summary>A point on the gift board in design pixels, y counted from the ribbon's top.</summary>
public sealed record BoardPoint(double X, double Y);

/// <summary>A background for the splash and the login page (#343): a folder under Pages/backgrounds with a picture for each,
/// 1536 × 1024, and background.json, which names them and gives the colour shown until they are drawn. Characters share one
/// unless it does not suit her.</summary>
public sealed record Background(string Color, string Splash, string Login)
{
    /// <summary>The folder of the background called <paramref name="name"/>, in the program's Pages.</summary>
    public static string Folder(string name) => Path.Combine(WebServer.PagesDirectory, "backgrounds", name);

    /// <summary>The manifest in <paramref name="folder"/>.</summary>
    public static Background Load(string folder) => Character.Read<Background>(folder, "background.json");
}
