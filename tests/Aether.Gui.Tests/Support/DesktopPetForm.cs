using Aether.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests.Support;

/// <summary>The 桌宠 page's lists and inputs, found as the streamer sees them.</summary>
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

    /// <summary>The labels of the 试演 offered.</summary>
    public static IEnumerable<string> TestLabels(DesktopPetSettingsPage page) =>
        page.FindControl<ComboBox>("TestShowInput")!.Items.Cast<DesktopPetTest>().Select(test => test.Label);

    /// <summary>The labels of the kinds of lines listed.</summary>
    public static IEnumerable<string> LineKindLabels(DesktopPetSettingsPage page) =>
        page.FindControl<ListBox>("LineKindList")!.Items.OfType<DesktopPetLineKind>().Select(kind => kind.Kind.Label);

    /// <summary>Fills 做哪几样 up to <paramref name="count"/> switches with placeholders, to see how many daily actions fit.</summary>
    public static void PadDailyActions(DesktopPetSettingsPage page, int count)
    {
        var rows = page.FindControl<ItemsControl>("DailyRows")!;
        var real = rows.Items.Cast<DesktopPetShowRow>().ToArray();
        rows.ItemsSource = real.Concat(Enumerable.Range(real.Length + 1, count - real.Length)
            .Select(i => new DesktopPetShowRow(new Show($"later{i}", ShowGroup.Daily, $"第{i}样", "", [], [])))).ToArray();
        GuiHarness.Flush();
    }

    public static ToggleSwitch Switch(DesktopPetSettingsPage page, string name) => Shown<ToggleSwitch>(page, name);

    public static NumericUpDown Number(DesktopPetSettingsPage page, string name) => Shown<NumericUpDown>(page, name);

    public static TextBox Input(DesktopPetSettingsPage page, string name) => Shown<TextBox>(page, name);

    public static TextBox Line(DesktopPetSettingsPage page, int number) => Input(page, $"第 {number} 句");

    public static IEnumerable<string?> Texts(DesktopPetSettingsPage page) =>
        page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text);

    private static T Shown<T>(DesktopPetSettingsPage page, string name) where T : Control =>
        page.GetVisualDescendants().OfType<T>().Single(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == name);
}
