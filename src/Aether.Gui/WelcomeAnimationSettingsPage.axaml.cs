using Aether.Core;
using Avalonia.Interactivity;

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
        Title = "欢迎动画";
        Bind(AnimationInput, AnimationReadOnly, null, s => s.WelcomeAnimationEnabled);
        AddAnimationButton.Click += async (_, _) => await ChooseAnimationAsync();
        LoadSettings();
    }

    private void OpenGeneralSettings(object? sender, RoutedEventArgs e) => GeneralSettingsRequested?.Invoke();
    protected override bool KeepMessageUntilEdited => true;
    protected override void LoadRows() => LoadAnimations();
    protected override bool RowsChanged => AnimationDraft;
    protected override bool ShowRowErrors() => RefreshAnimations();
    internal override IEnumerable<string> FieldNames => base.FieldNames.Append("welcome_animation_videos");

    protected override Task<string> WriteSettings(Dictionary<string, string> changes)
    {
        var rows = Animations.ToDictionary(row => row.Uid!.Value, row => (row.FileName, row.SourcePath));
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
