using Aether.Core;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aether.Gui.Tests.Support;

/// <summary>Drives the 欢迎条件 list on a 进场欢迎 page the way its menu and × buttons do.</summary>
internal static class ConditionList
{
    public static WelcomeConditionRow[] Rows(SettingsPage page) =>
        ((WelcomeSettingsPage)page).WelcomeUids.Concat(((WelcomeSettingsPage)page).WelcomeThresholds).ToArray();

    public static MenuFlyout Menu(SettingsPage page) => Assert.IsType<MenuFlyout>(page.FindControl<Button>("AddConditionButton")!.Flyout);

    public static MenuFlyout ShowMenu(SettingsPage page)
    {
        var add = page.FindControl<Button>("AddConditionButton")!;
        add.BringIntoView();
        GuiHarness.Flush();
        var menu = Menu(page);
        menu.ShowAt(add);
        GuiHarness.Flush();
        return menu;
    }

    public static WelcomeConditionRow Add(SettingsPage page, WelcomeCondition kind)
    {
        Menu(page).Items.Cast<MenuItem>().Single(item => Equals(item.Tag, kind))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        GuiHarness.Flush();
        return Rows(page).Last(row => row.Kind == kind);
    }

    public static void Remove(SettingsPage page, WelcomeConditionRow row) => Assert.IsAssignableFrom<IInvokeProvider>(
        ControlAutomationPeer.CreatePeerForElement(GuiHarness.FindRowControl<Button>(page, row, "RemoveButton"))).Invoke();
}
