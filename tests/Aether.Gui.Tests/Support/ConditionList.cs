using Aether.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aether.Gui.Tests.Support;

/// <summary>Drives the 欢迎条件 list on a 进场欢迎 page the way its menu and × buttons do.</summary>
internal static class ConditionList
{
    public static WelcomeConditionRow[] Rows(SettingsPage page) => page.FindControl<StackPanel>("ConditionRows")!
        .Children.Cast<WelcomeConditionRow>().ToArray();

    public static MenuFlyout Menu(SettingsPage page) => Assert.IsType<MenuFlyout>(page.FindControl<Button>("AddConditionButton")!.Flyout);

    public static WelcomeConditionRow Add(SettingsPage page, WelcomeCondition kind)
    {
        Menu(page).Items.Cast<MenuItem>().Single(item => Equals(item.Tag, kind))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        GuiHarness.Flush();
        return Rows(page).Last(row => row.Kind == kind);
    }

    public static void Remove(WelcomeConditionRow row) => row.FindControl<Button>("RemoveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
