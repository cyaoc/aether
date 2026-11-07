using Aether.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Aether.Gui;
public partial class AnimationTestBar : UserControl
{
    public string Source { get; set; } = PlaybackSource.Entry;
    private MainWindowViewModel? viewModel;
    public AnimationTestBar()
    {
        InitializeComponent();
        TierInput.ItemsSource = EntryTier.All;
        TierInput.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<EntryTier>((tier, _) => new TextBlock { Text = $"{tier} 档" });
        TierInput.SelectedIndex = EntryTier.All.Count - 1;
        NicknameInput.TextChanged += (_, _) => Refresh();
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
        PlayButton.IsEnabled = playing is { IsTest: true } || !string.IsNullOrWhiteSpace(NicknameInput.Text)
            && viewModel?.WebService is { HasVideoConnections: true, AnimationBusy: false, AnimationReloading: false };
    }
    private void Play(object? sender, RoutedEventArgs e)
    {
        if (viewModel?.WebService.PlayingForSource(Source) is { IsTest: true } playing) viewModel.WebService.StopAnimation(playing.Id);
        else if (TierInput.SelectedItem is EntryTier tier) viewModel?.WebService.TestAnimation(Source, tier, NicknameInput.Text ?? "");
    }
}
