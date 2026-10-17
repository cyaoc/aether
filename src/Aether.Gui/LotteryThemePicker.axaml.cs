using System.ComponentModel;
using Aether.Core;
using Avalonia.Controls;

namespace Aether.Gui;

public partial class LotteryThemePicker : UserControl
{
    private MainWindowViewModel? viewModel;

    public LotteryThemePicker()
    {
        InitializeComponent();
        StarSummonCard.Click += (_, _) => viewModel?.SelectLotteryTheme(LotteryTheme.StarSummon);
        ExampleButton.Click += (_, _) =>
        {
            if (!ExampleButton.IsEnabled || viewModel is null) return;
            ErrorText.IsVisible = false;
            try { viewModel.PlayLotteryExample(); }
            catch (Exception error) { ErrorText.Text = error.Message; ErrorText.IsVisible = true; }
            Refresh();
        };
        Refresh();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.PropertyChanged -= Changed;
        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null) viewModel.PropertyChanged += Changed;
        Refresh();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.LotteryState) or nameof(MainWindowViewModel.WebServiceStatus)) Refresh();
    }

    private void Refresh()
    {
        if (ExampleButton is null) return;
        var playing = viewModel?.LotteryState.IsExamplePlaying == true;
        StarSummonCard.IsChecked = viewModel is null || viewModel.LotteryState.Theme == LotteryTheme.StarSummon;
        ExampleButton.Content = playing ? "示例播放中…" : "试一下";
        ExampleButton.IsEnabled = !playing && viewModel?.WebServiceStatus.Running == true;
        ExampleHint.Text = viewModel?.WebServiceStatus is not { Running: true }
            ? "网页服务未运行：" + (viewModel?.WebServiceStatus.Error ?? "请检查通用设置")
            : "用示例数据播约 20 秒；直播中观众也会看到";
    }
}
