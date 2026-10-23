using System.ComponentModel;
using Aether.Core;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Aether.Gui;

public partial class LotteryPrizeSetPicker : UserControl
{
    private LotteryViewModel? viewModel;
    private string? loadedName;
    private long? roomId;
    public LotteryMode Mode { get; set; }
    private MenuFlyout LoadFlyout => (MenuFlyout)LoadButton.Flyout!;
    private Flyout SaveFlyout => (Flyout)SaveButton.Flyout!;

    public LotteryPrizeSetPicker()
    {
        InitializeComponent();
        LoadFlyout.Opening += (_, _) => LoadFlyout.HorizontalOffset = SaveButton.Bounds.Width + 6;
        LoadFlyout.Opened += (_, _) => { LoadButton.Classes.Add("open"); RefreshMenu(); };
        LoadFlyout.Closed += (_, _) => LoadButton.Classes.Remove("open");
        SaveFlyout.Closed += (_, _) => SaveButton.Classes.Remove("open");
        SaveFlyout.Opened += (_, _) => { SaveButton.Classes.Add("open"); NameInput.Text = loadedName ?? ""; Refresh(); };
        NameInput.TextChanged += (_, _) => Refresh();
        CancelButton.Click += (_, _) => SaveFlyout.Hide();
        ConfirmButton.Click += async (_, _) =>
        {
            if (!ConfirmButton.IsEnabled || viewModel is null) return;
            var name = NameInput.Text!.Trim();
            if (await ChangeAsync(() => viewModel.SavePrizeSetAsync(Mode, name)))
            {
                loadedName = name;
                SaveFlyout.Hide();
            }
        };
        Refresh();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.PropertyChanged -= Changed;
        viewModel = (DataContext as MainWindowViewModel)?.Lottery;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += Changed;
            _ = viewModel.RefreshPrizeSetsAsync();
        }
        Refresh();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LotteryViewModel.PrizeSets) or nameof(LotteryViewModel.LotteryState)) RefreshMenu();
        Refresh();
    }

    private void Refresh()
    {
        if (LoadButton is null) return;
        var canEdit = viewModel is not null && (Mode == LotteryMode.Pool ? viewModel.CanEditPool : viewModel.CanEditEntry);
        LoadButton.IsEnabled = SaveButton.IsEnabled = canEdit;
        foreach (var item in LoadFlyout.Items.OfType<MenuItem>().Where(item => item.Tag is LotteryPrizeSet)) item.IsEnabled = canEdit;
        if (roomId != viewModel?.PrizeSetRoomId) { roomId = viewModel?.PrizeSetRoomId; loadedName = null; }
        var name = NameInput.Text?.Trim();
        var exists = viewModel?.PrizeSets.Any(set => set.Mode == Mode && set.Name == name) == true;
        ConfirmButton.IsEnabled = canEdit && !string.IsNullOrEmpty(name);
        ConfirmButton.Content = exists ? "替换" : "保存";
        SaveHint.Classes.Set("replacement", exists);
        SaveHint.Text = string.IsNullOrEmpty(name) ? "先起个名字"
            : exists ? "已有同名方案，保存会替换它"
            : Mode == LotteryMode.Pool ? $"存下这 {viewModel?.Prizes.Count ?? 0} 件奖品，名称、占比和档次"
            : $"存下这 {viewModel?.Awards.Count ?? 0} 个奖项，名称、奖品、名额和档次";
    }

    private void RefreshMenu()
    {
        LoadFlyout.Items.Clear();
        var sets = viewModel?.PrizeSets.Where(set => set.Mode == Mode).ToArray() ?? [];
        foreach (var set in sets)
        {
            var text = new StackPanel { Spacing = 3, Margin = new Thickness(10, 7) };
            var name = new TextBlock { Text = set.Name, Classes = { "label", "singleLine" },
                MaxWidth = 210, FontWeight = loadedName == set.Name ? FontWeight.SemiBold : FontWeight.Medium };
            name.Classes.Set("accent", loadedName == set.Name);
            var count = new TextBlock { Text = Mode == LotteryMode.Pool ? $"{set.Prizes.Length} 件奖品" : $"{set.Awards.Length} 个奖项",
                Classes = { "caption", "muted", "numeric" }, VerticalAlignment = VerticalAlignment.Center };
            var title = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 6, Children = { name, count } };
            Grid.SetColumn(count, 1);
            text.Children.Add(title);
            text.Children.Add(new TextBlock { Text = Mode == LotteryMode.Pool
                ? string.Join("、", set.Prizes.Select(prize => prize.Name))
                : string.Join("、", set.Awards.Select(award => $"{award.Prize} ×{award.Capacity}")),
                Classes = { "caption", "muted", "singleLine" } });
            var remove = new Button { Content = new Viewbox { Classes = { "icon" }, Width = 12, Height = 12,
                Child = new Avalonia.Controls.Shapes.Path { Data = (Geometry)this.FindResource("Icon.Remove")!, StrokeThickness = 2.2 } },
                Classes = { "ghost", "prizeSetDelete" }, Width = 24, Height = 24, MinHeight = 0,
                Padding = new Thickness(0), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(remove, $"删除方案{set.Name}");
            remove.Click += async (_, args) =>
            {
                args.Handled = true;
                if (viewModel is not null && await ChangeAsync(() => viewModel.DeletePrizeSetAsync(set)) && loadedName == set.Name) loadedName = null;
            };
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4, Children = { text, remove } };
            Grid.SetColumn(remove, 1);
            var item = new MenuItem { Header = header, Tag = set, Padding = new Thickness(0, 0, 4, 0), Margin = new Thickness(0, 0, 0, 2) };
            item.Classes.Set("chosen", loadedName == set.Name);
            AutomationProperties.SetName(item, $"载入方案：{set.Name}");
            item.Click += (_, _) =>
            {
                if (!LoadButton.IsEnabled || viewModel is null) return;
                viewModel.LoadPrizeSet(set);
                loadedName = set.Name;
                LoadFlyout.Hide();
            };
            LoadFlyout.Items.Add(item);
        }
        if (sets.Length == 0) LoadFlyout.Items.Add(new MenuItem
        {
            Header = new TextBlock { Text = "还没有方案，填好后点“存为方案”", Classes = { "caption", "muted" } },
            IsEnabled = false, Padding = new Thickness(10),
        });
        LoadFlyout.Items.Add(new Separator { Margin = new Thickness(0, 2, 0, 0) });
        LoadFlyout.Items.Add(new MenuItem
        {
            Header = new TextBlock { Text = Mode == LotteryMode.Pool
                ? "点一个方案，替换上面的奖品；换完还能接着改" : "点一个方案，替换上面的奖项；换完还能接着改", Classes = { "caption", "muted" } },
            IsEnabled = false, Padding = new Thickness(10, 8, 10, 6),
        });
    }

    private async Task<bool> ChangeAsync(Func<Task> change)
    {
        if (viewModel is null || viewModel.Busy) return false;
        if (Mode == LotteryMode.Pool) viewModel.PoolLotteryError = null;
        else viewModel.EntryLotteryError = null;
        try { await change(); return true; }
        catch (Exception error)
        {
            if (Mode == LotteryMode.Pool) viewModel.PoolLotteryError = error.Message;
            else viewModel.EntryLotteryError = error.Message;
            return false;
        }
    }
}
