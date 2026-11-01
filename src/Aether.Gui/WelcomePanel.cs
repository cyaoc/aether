using Avalonia.Controls;
namespace Aether.Gui;

public sealed class WelcomePanel : Grid
{
    public WelcomeSettingsPage Danmaku { get; }
    public WelcomeAnimationSettingsPage Animation { get; }
    public IEnumerable<SettingsPage> Pages => [Danmaku, Animation];
    public WelcomePanel(string dataDirectory)
    {
        Danmaku = new(dataDirectory);
        Animation = new(dataDirectory) { IsVisible = false };
        Children.Add(Danmaku);
        Children.Add(Animation);
        foreach (var page in Pages)
        {
            var tabs = new ListBox { ItemsSource = new[] { "弹幕", "动画" }, SelectedIndex = 0 };
            tabs.Classes.Add("viewTabs");
            tabs.SelectionChanged += (_, _) => Select(tabs.SelectedIndex);
            page.HeaderTabs = tabs;
        }
    }
    public void Select(int index)
    {
        Danmaku.IsVisible = index == 0;
        Animation.IsVisible = index == 1;
        foreach (var page in Pages) ((ListBox)page.HeaderTabs!).SelectedIndex = index;
    }
}
