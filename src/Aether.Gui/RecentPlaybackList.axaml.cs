using Avalonia;
using Avalonia.Controls;
namespace Aether.Gui;
public partial class RecentPlaybackList : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<WelcomeAnimationItem>?> ItemsSourceProperty = AvaloniaProperty.Register<RecentPlaybackList, IReadOnlyList<WelcomeAnimationItem>?>(nameof(ItemsSource));
    public IReadOnlyList<WelcomeAnimationItem>? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public RecentPlaybackList() => InitializeComponent();
}
