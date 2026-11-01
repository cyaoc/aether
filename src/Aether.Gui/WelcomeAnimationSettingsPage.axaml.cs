using Aether.Core;

namespace Aether.Gui;

public partial class WelcomeAnimationSettingsPage : SettingsPage
{
    private long[] replaced = [];
    public event Action? GeneralSettingsRequested;

    public WelcomeAnimationSettingsPage() : this(DataDirectory.Locate()) { }

    public WelcomeAnimationSettingsPage(string dataDirectory, Func<Task<string?>>? pickAnimationVideo = null) : base(dataDirectory)
    {
        this.pickAnimationVideo = pickAnimationVideo;
        InitializeComponent();
        AnimationRows.ItemsSource = Animations;
        Animations.RowChanged += Validate;
        Title = "进场欢迎";
        PageStatus.GeneralSettingsRequested += () => GeneralSettingsRequested?.Invoke();
        TestBar.TestRequested += (tier, nickname) => viewModel?.WebService.TestEntry(tier, nickname);
        TestBar.StopRequested += id => viewModel?.WebService.StopEntry(id);
        TimingHint = "保存后，下一次进场生效";
        FanClubInput.ItemsSource = new[] { 0 }.Concat(EntryTier.All.Select(tier => tier.Value)).ToArray();
        FanClubInput.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<int>((value, _) => new Avalonia.Controls.TextBlock { Text = value == 0 ? "不按等级" : $"{value} 级起" });
        Bind(FanClubInput, FanClubReadOnly, null, s => s.EntryAnimationFanClubLevel);
        Bind(VolumeInput, VolumeReadOnly, null, s => s.EntryAnimationVolume);
        Bind(AnimationInput, AnimationReadOnly, null, s => s.WelcomeAnimationEnabled);
        AddAnimationButton.Click += (_, _) => Animations.AddNew(new("", "", true, true) { Choice = EntryTier.All[0].ToString() });
        LoadSettings();
    }

    protected override bool ReconnectAfterSave => false;
    protected override void LoadRows() => LoadAnimations();
    protected override bool RowsChanged => AnimationDraft;
    protected override bool ShowRowErrors() => RefreshAnimations();
    internal override IEnumerable<string> FieldNames => base.FieldNames.Append("entry_animation_viewers");

    protected override Task<string> WriteSettings(Dictionary<string, string> changes)
    {
        var rows = Animations.ToDictionary(row => row.Uid!.Value, row => (FileName: row.SettingValue, SourcePath: row.IsVideo ? row.SourcePath : null));
        replaced = rows.Where(pair => pair.Value.SourcePath is not null).Select(pair => pair.Key).ToArray();
        var snapshot = settings;
        return Task.Run(() => SaveAnimationFiles(changes, rows, snapshot));
    }

    protected override async Task<string> AfterSave(Settings saved, string result)
    {
        if (viewModel is not null)
        {
            await viewModel.WebService.ApplyWebSettingsAsync(saved);
            viewModel.WebService.ForgetAnimationFailures(replaced);
        }
        return result;
    }

    protected override void ReloadAfterFailure()
    {
        // Keep selected source paths after a failed import, so the user can retry or replace the video.
        ShowFileStatus(Settings.Load(dataDirectory));
        Validate();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (viewModel is not null) viewModel.WebService.AnimationRowsChanged -= OnAnimationRowsChanged;
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.WebService.AnimationRowsChanged += OnAnimationRowsChanged;
        if (settings is not null) RefreshAnimations();
    }

    private void OnAnimationRowsChanged() => RefreshAnimations();
}
