using Avalonia.Controls;

namespace Aether.Gui.Tests.Support;

internal static class WelcomeTextList
{
    public static WelcomeTextRow[] Rows(SettingsPage page) => page.FindControl<StackPanel>("WelcomeTextRows")!
        .Children.Cast<WelcomeTextRow>().ToArray();

    public static TextBox Input(SettingsPage page, int index = 0) => Rows(page)[index].FindControl<TextBox>("ValueInput")!;
    public static TextBlock Error(SettingsPage page, int index = 0) => Rows(page)[index].FindControl<TextBlock>("ErrorMessage")!;
}
