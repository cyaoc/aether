using Aether.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Aether.Gui;
public partial class AnimationTestBar : UserControl
{
    public string Source { get; set; } = "进场动画";
    public event Action<EntryTier, string>? TestRequested;
    public event Action<string>? StopRequested;
    private MainWindowViewModel? viewModel;
    public AnimationTestBar()
    {
        InitializeComponent();
        TierInput.ItemsSource = EntryTier.All;
        TierInput.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<EntryTier>((tier, _) => new TextBlock { Text = $"{tier} 档" });
        TierInput.SelectedIndex = 0;
    }
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (viewModel is not null) viewModel.WebService.AnimationRowsChanged -= Refresh;
        base.OnDataContextChanged(e);
        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null) viewModel.WebService.AnimationRowsChanged += Refresh;
        Refresh();
    }
    private void Refresh()
    {
        var playing = viewModel?.WebService.PlayingForSource(Source);
        PlayButton.Content = playing is { IsTest: true } ? "停止" : "▷ 试播";
        PlayButton.IsEnabled = playing is { IsTest: true } || viewModel?.WebService is { HasVideoConnections: true, AnimationBusy: false, AnimationReloading: false };
    }
    private void Play(object? sender, RoutedEventArgs e)
    {
        if (viewModel?.WebService.PlayingForSource(Source) is { IsTest: true } playing) StopRequested?.Invoke(playing.Id);
        else if (TierInput.SelectedItem is EntryTier tier) TestRequested?.Invoke(tier, NicknameInput.Text ?? "");
    }
}
