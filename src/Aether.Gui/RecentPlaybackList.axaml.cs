using Avalonia;
using Avalonia.Controls;
namespace Aether.Gui;
public partial class RecentPlaybackList : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<PlaybackItem>?> ItemsSourceProperty = AvaloniaProperty.Register<RecentPlaybackList, IReadOnlyList<PlaybackItem>?>(nameof(ItemsSource));
    public IReadOnlyList<PlaybackItem>? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public RecentPlaybackList() => InitializeComponent();
}
