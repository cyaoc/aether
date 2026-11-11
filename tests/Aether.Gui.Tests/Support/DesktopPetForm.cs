using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests.Support;

/// <summary>The 桌宠 page's tabs, lists and inputs, found as the streamer sees them.</summary>
internal static class DesktopPetForm
{
    /// <summary>Picks the 演出 group or the kind of lines labelled <paramref name="label"/> in the list named <paramref name="list"/>.</summary>
    public static void Pick(DesktopPetSettingsPage page, string list, string label)
    {
        var items = page.FindControl<ListBox>(list)!;
        items.SelectedItem = items.Items.Single(item => item is DesktopPetShowGroup group ? group.Group.Label == label
            : item is DesktopPetLineKind kind && kind.Kind.Label == label);
        GuiHarness.Flush();
    }

    /// <summary>Picks the 试演 labelled <paramref name="label"/>.</summary>
    public static void PickTest(DesktopPetSettingsPage page, string label)
    {
        var tests = page.FindControl<ComboBox>("TestShowInput")!;
        tests.SelectedItem = tests.Items.Cast<DesktopPetTest>().Single(test => test.Label == label);
    }

    public static ToggleSwitch Switch(DesktopPetSettingsPage page, string name) => Shown<ToggleSwitch>(page, name);

    public static NumericUpDown Number(DesktopPetSettingsPage page, string name) => Shown<NumericUpDown>(page, name);

    public static TextBox Line(DesktopPetSettingsPage page, int number) => Shown<TextBox>(page, $"第 {number} 句");

    public static IEnumerable<string?> Texts(DesktopPetSettingsPage page) =>
        page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text);

    private static T Shown<T>(DesktopPetSettingsPage page, string name) where T : Control =>
        page.GetVisualDescendants().OfType<T>().Single(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == name);
}
