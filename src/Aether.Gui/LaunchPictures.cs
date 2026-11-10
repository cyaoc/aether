using Aether.Core;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Aether.Gui;

/// <summary>The splash's and the login page's pictures (#343): the current character's layer over the background she picks, read
/// from the program's Pages. Both layers fill the window alike, so they stay together at any size, on the background's colour
/// until they are drawn.</summary>
internal static class LaunchPictures
{
    private static readonly Character Current = Character.Load(Character.CurrentFolder);
    private static readonly string BackgroundFolder = Background.Folder(Current.Gui.Background);
    private static readonly Background Behind = Background.Load(BackgroundFolder);

    public static IBrush Color { get; } = Brush.Parse(Behind.Color);
    public static Bitmap SplashBackground { get; } = new(Path.Combine(BackgroundFolder, Behind.Splash));
    public static Bitmap SplashCharacter { get; } = new(Path.Combine(Character.CurrentFolder, Current.Gui.Splash));
    public static Bitmap LoginBackground { get; } = new(Path.Combine(BackgroundFolder, Behind.Login));
    public static Bitmap LoginCharacter { get; } = new(Path.Combine(Character.CurrentFolder, Current.Gui.Login));
}
