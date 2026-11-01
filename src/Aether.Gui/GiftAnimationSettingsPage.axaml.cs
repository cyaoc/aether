using Aether.Core;
using Avalonia;
using Avalonia.Controls;

namespace Aether.Gui;

public partial class GiftAnimationSettingsPage : SettingsPage
{
    private readonly List<NumericUpDown> thresholds = [];
    public event Action? GeneralSettingsRequested;
    public GiftAnimationSettingsPage() : this(DataDirectory.Locate()) { }
    public GiftAnimationSettingsPage(string dataDirectory) : base(dataDirectory)
    {
        InitializeComponent();
        Title = "送礼动画";
        TimingHint = "保存后，下一次送礼生效";
        Bind(EnabledInput, EnabledReadOnly, null, settings => settings.GiftAnimationEnabled);
        foreach (var (tier, index) in EntryTier.All.Select((tier, index) => (tier, index)))
        {
            var column = new StackPanel { Width = 150, Spacing = 8, Margin = new Thickness(0, 0, 8, 8) };
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            column.Children.Add(new TextBlock { Text = $"{tier} 档" });
            var input = new NumericUpDown { Minimum = 1, FormatString = "0.############################" };
            Avalonia.Automation.AutomationProperties.SetName(input, $"{tier} 档投入门槛");
            thresholds.Add(input);
            row.Children.Add(input);
            row.Children.Add(new TextBlock { Text = "电池", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var readOnly = new TextBlock { Text = "这项不能逐行编辑，请在设置文件里改" };
            readOnly.Classes.Add("hint");
            column.Children.Add(row);
            column.Children.Add(readOnly);
            ThresholdRows.Children.Add(column);
            Bind(input, readOnly, null, settings => settings.GiftAnimationThresholds[index]);
        }
        PageStatus.GeneralSettingsRequested += () => GeneralSettingsRequested?.Invoke();
        LoadSettings();
    }
    protected override bool ReconnectAfterSave => false;
    private bool notPositive;
    protected override bool ShowRowErrors()
    {
        var values = thresholds.Select(input => int.TryParse(input.Text, out var value) ? value : 0).ToArray();
        var invalid = Settings.InvalidGiftThreshold(values);
        notPositive = values.Any(value => value <= 0);
        ThresholdError.Text = invalid is null ? "" : notPositive ? "门槛要填正整数" : "每一档的门槛要比上一档高";
        ThresholdError.IsVisible = DirtyBlocked = invalid is not null;
        foreach (var (input, index) in thresholds.Select((input, index) => (input, index)))
            input.Classes.Set("invalid", values[index] <= (index == 0 ? 0 : values[index - 1]));
        return invalid is not null;
    }
    protected override string DirtyDescription => !ThresholdError.IsVisible ? base.DirtyDescription
        : notPositive ? "门槛要填正整数，改好后才能保存" : "门槛要一档比一档高，改好后才能保存";
    protected override async Task<string> AfterSave(Settings saved, string result)
    {
        if (viewModel is not null) await viewModel.WebService.ApplyWebSettingsAsync(saved);
        return result;
    }
}
