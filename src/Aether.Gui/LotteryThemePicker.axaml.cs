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
        MechaLockCard.Click += (_, _) => viewModel?.SelectLotteryTheme(LotteryTheme.MechaLock);
        CapsuleGachaCard.Click += (_, _) => viewModel?.SelectLotteryTheme(LotteryTheme.CapsuleGacha);
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
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= Changed;
            viewModel.WebService.PropertyChanged -= Changed;
        }
        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += Changed;
            viewModel.WebService.PropertyChanged += Changed;
        }
        Refresh();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.LotteryState) or nameof(WebServiceViewModel.WebServiceStatus)) Refresh();
    }

    private void Refresh()
    {
        if (ExampleButton is null) return;
        var playing = viewModel?.LotteryState.IsExamplePlaying == true;
        var running = viewModel?.WebService.WebServiceStatus.Running == true;
        StarSummonCard.IsChecked = viewModel is null || viewModel.LotteryState.Theme == LotteryTheme.StarSummon;
        MechaLockCard.IsChecked = viewModel?.LotteryState.Theme == LotteryTheme.MechaLock;
        CapsuleGachaCard.IsChecked = viewModel?.LotteryState.Theme == LotteryTheme.CapsuleGacha;
        ExampleText.Text = playing ? "示例播放中…" : "试一下";
        ExampleButton.IsEnabled = !playing && running;
        ExampleHint.Text = !running ? "网页服务未运行：" + (viewModel?.WebService.WebServiceStatus.Error ?? "请检查通用设置")
            : playing ? "抽奖页正在用示例数据播一遍，约 20 秒" : "用示例数据在抽奖页播一遍；直播中观众也会看到";
        ExampleHint.Classes.Set("muted", !playing);
        ExampleHint.Classes.Set("accent", playing);
    }
}
