using Avalonia.Controls;

namespace Aether.Gui.Tests.Support;

internal static class WelcomeTextList
{
    public static WelcomeTextRow[] Rows(SettingsPage page) => ((WelcomeSettingsPage)page).WelcomeTexts.ToArray();

    public static TextBox Input(SettingsPage page, int index = 0) => GuiHarness.FindRowControl<TextBox>(page, Rows(page)[index], "ValueInput");
    public static TextBlock Error(SettingsPage page, int index = 0) => GuiHarness.FindRowControl<TextBlock>(page, Rows(page)[index], "ErrorMessage");
}
