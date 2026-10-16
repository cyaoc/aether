using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Aether.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Aether.Gui;

public enum SettingsSection { BlindBox, Welcome, WelcomeAnimation, Send, General }

public partial class SettingsPage : UserControl
{
    private readonly string dataDirectory;
    private readonly string group;
    private Settings settings = null!;

    private Dictionary<string, string?> original = [];
    private bool loading;
    private bool saving;
    private TaskCompletionSource? saveCompletion;
    internal Task PendingSave => saveCompletion?.Task ?? Task.CompletedTask;
    private SettingsException? fileError;
    private MainWindowViewModel? viewModel;
    private double firstPendingRowsHeight;
    public SettingsSection Section { get; }
    /// <summary>Raised once Save has written the file or failed to, so other views can reread it.</summary>
    public event Action? SaveCompleted;

    public SettingsPage() : this(DataDirectory.Locate(), SettingsSection.General) { }

    public SettingsPage(string dataDirectory, SettingsSection section, Func<Task<string?>>? pickAnimationVideo = null)
    {
        this.dataDirectory = dataDirectory;
        this.pickAnimationVideo = pickAnimationVideo;
        Section = section;
        InitializeComponent();
        WelcomeTextFold.Toggled += RefreshWelcomeTextFold;
        UidFold.Toggled += RefreshUidFold;
        (PageTitle.Text, group) = section switch
        {
            SettingsSection.BlindBox => ("盲盒统计", "blind_box."),
            SettingsSection.Welcome => ("进场欢迎", "welcome."),
            SettingsSection.WelcomeAnimation => ("欢迎动画", "welcome_animation."),
            SettingsSection.Send => ("发送队列", "send."),
            _ => ("通用设置", "log.")
        };
        TimingHint.Text = AppliesOnRestart ? "重启后生效" : "下次连接直播间时生效";
        BlindBoxHeader.IsVisible = BlindBoxFields.IsVisible = section == SettingsSection.BlindBox;
        WelcomeHeader.IsVisible = WelcomeFields.IsVisible = section == SettingsSection.Welcome;
        AnimationHeader.IsVisible = AnimationFields.IsVisible = section == SettingsSection.WelcomeAnimation;
        SendFields.IsVisible = section == SettingsSection.Send;
        GeneralFields.IsVisible = section == SettingsSection.General;
        SendActivity.IsVisible = section == SettingsSection.Send;
        if (SendActivity.IsVisible)
        {
            FieldsAndSendActivity.RowDefinitions[0].Height = GridLength.Auto;
            FieldsAndSendActivity.RowDefinitions[1].Height = GridLength.Star;
            // Row heights are only known after layout, so the cap follows every layout pass.
            SendActivity.LayoutUpdated += (_, _) => ArrangePendingActivity();
        }
        RecentActivity.IsVisible = section is SettingsSection.Welcome or SettingsSection.BlindBox or SettingsSection.WelcomeAnimation;
        RecentActivityTitle.Text = section switch { SettingsSection.Welcome => "最近欢迎", SettingsSection.WelcomeAnimation => "最近播放", _ => "最近回复和播报" };
        RecentWelcomesList.IsVisible = WelcomeTotal.IsVisible = section == SettingsSection.Welcome;
        RecentQueriesAndAnnouncementsList.IsVisible = section == SettingsSection.BlindBox;
        RecentAnimationsList.IsVisible = section == SettingsSection.WelcomeAnimation;
        SizeChanged += (_, _) => ArrangeActivity();
        LogLevelInput.ItemsSource = Settings.LogLevels;
        foreach (var number in new[] { RetentionInput, SendIntervalInput, AnimationPortInput })
            number.PropertyChanged += (_, e) =>
            {
                if (e.Property == NumericUpDown.TextProperty) Validate();
            };
        LogLevelInput.SelectionChanged += (_, _) => Validate();
        DailyKeywordInput.TextChanged += (_, _) => Validate();
        MonthlyKeywordInput.TextChanged += (_, _) => Validate();
        foreach (var toggle in new[] { BlindBoxInput, AnnounceInput, WelcomeInput, AnimationInput })
            toggle.PropertyChanged += (_, e) =>
            {
                if (e.Property == ToggleButton.IsCheckedProperty) Validate();
            };
        AddAnimationButton.Click += async (_, _) => await ChooseAnimationAsync();
        CopyAnimationAddressButton.Click += async (_, _) => await CopyAnimationAddressAsync();
        DetachedFromVisualTree += (_, _) => { copyFeedback?.Dispose(); ShowCopied(false); };
        LoadSettings();
    }

    private void ArrangePendingActivity()
    {
        // The record being sent and the next one stay whole; past them the queue takes at most half, and history keeps its heading.
        // With more rows below, the second row's separator ends the area rather than a sliver of the third.
        // A scrolled-away second row keeps the last measured height.
        var count = PendingSendsList.ItemCount;
        if (count > 0
            && PendingSendsList.ContainerFromIndex(Math.Min(1, count - 1)) is { } row
            && PendingSendsList.TranslatePoint(default, PendingActivity) is { } list)
            firstPendingRowsHeight = list.Y + PendingSendsList.Padding.Top + row.Bounds.Bottom + (count > 2 ? 0 : PendingSendsList.Padding.Bottom);
        var room = SendActivity.Bounds.Height - RecentSendsActivity.Margin.Top - RecentSendsHeader.DesiredSize.Height;
        var height = Math.Max(0, Math.Min(room, Math.Max(SendActivity.Bounds.Height / 2, firstPendingRowsHeight)));
        if (Math.Abs(PendingActivity.MaxHeight - height) > 0.5) PendingActivity.MaxHeight = height;
    }

    private void ArrangeActivity()
    {
        if (!RecentActivity.IsVisible) return;
        var stacked = Bounds.Width < 680;
        Grid.SetColumn(RecentActivity, stacked ? 0 : 1);
        Grid.SetRow(RecentActivity, stacked ? 1 : 0);
        RecentActivity.Width = stacked ? double.NaN : 300;
        RecentActivity.BorderThickness = stacked ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        PageLayout.RowDefinitions[1].Height = stacked ? GridLength.Star : new GridLength(0);
    }

    /// <summary>Only log settings require restarting the app.</summary>
    private bool AppliesOnRestart => Section == SettingsSection.General;

    private void LoadSettings(Settings? current = null)
    {
        loading = true;
        settings = current ?? Settings.Load(dataDirectory);
        ShowFileStatus(settings);
        Show(RetentionInput, RetentionReadOnly, settings.LogRetentionDays);
        Show(LogLevelInput, LogLevelReadOnly, settings.LogLevel);
        Show(BlindBoxInput, BlindBoxReadOnly, settings.BlindBoxEnabled);
        Show(DailyKeywordInput, DailyKeywordReadOnly, settings.BlindBoxDailyKeyword);
        Show(MonthlyKeywordInput, MonthlyKeywordReadOnly, settings.BlindBoxMonthlyKeyword);
        Show(AnnounceInput, AnnounceReadOnly, settings.BlindBoxAnnounce);
        Show(SendIntervalInput, SendIntervalReadOnly, settings.SendIntervalSeconds);
        Show(WelcomeInput, WelcomeReadOnly, settings.WelcomeEnabled);
        LoadWelcomeTexts();
        Show(AnimationInput, AnimationReadOnly, settings.WelcomeAnimationEnabled);
        Show(AnimationPortInput, AnimationPortReadOnly, settings.WelcomeAnimationPort);
        LoadWelcomeConditions();
        if (Section == SettingsSection.WelcomeAnimation) LoadAnimations();
        original = Fields().ToDictionary(field => field.Name, field => field.Input);
        loading = false;
        Validate();
    }

    /// <summary>Rereads an untouched page; a draft keeps its inputs and only takes the file's status.</summary>
    public void RefreshFileStatus(Settings current)
    {
        if (saving) return;
        // A snapshot with an error filled the inputs with values that must not be used, so a draft over it is dropped.
        if (settings.Error is not null || !DirtyMessage.IsVisible)
        {
            LoadSettings(current);
            return;
        }
        ShowFileStatus(current);
        Validate();
    }

    private void ShowFileStatus(Settings current)
    {
        fileError = current.Error;
        UnknownKeysMessage.IsVisible = current.UnknownKeys.Count > 0;
        UnknownKeysMessage.Text = $"未知设置键：{string.Join("、", current.UnknownKeys)}。保存后会原样保留。";
    }

    // Each control shows its setting; a line that cannot be edited safely leaves it read-only beside its hint.
    private static void Show(NumericUpDown input, TextBlock readOnly, Setting<int> setting)
    {
        input.Value = setting.Value;
        input.Text = setting.Value.ToString(CultureInfo.InvariantCulture);
        input.IsReadOnly = !setting.CanEdit;
        input.AllowSpin = setting.CanEdit;
        readOnly.IsVisible = !setting.CanEdit;
    }

    private static void Show(TextBox input, TextBlock readOnly, Setting<string> setting)
    {
        input.Text = setting.Value;
        input.IsReadOnly = !setting.CanEdit;
        readOnly.IsVisible = !setting.CanEdit;
    }

    private static void Show<T>(ComboBox input, TextBlock readOnly, Setting<T> setting)
    {
        input.SelectedItem = setting.Value;
        input.IsEnabled = setting.CanEdit;
        readOnly.IsVisible = !setting.CanEdit;
    }

    private static void Show(ToggleSwitch input, TextBlock readOnly, Setting<bool> setting)
    {
        input.IsChecked = setting.Value;
        input.IsEnabled = setting.CanEdit;
        readOnly.IsVisible = !setting.CanEdit;
    }

    private readonly record struct Field(string Name, bool CanEdit, string? Input, TextBlock? Error = null);

    /// <summary>Each setting this page edits and the text its control now holds; validating, enabling Save
    /// and saving all read this one list.</summary>
    private Field[] Fields() =>
    new Field[]
    {
        new(settings.LogRetentionDays.Name, settings.LogRetentionDays.CanEdit, RetentionInput.Text, RetentionError),
        new(settings.LogLevel.Name, settings.LogLevel.CanEdit, LogLevelInput.SelectedItem?.ToString(), LogLevelError),
        new(settings.BlindBoxEnabled.Name, settings.BlindBoxEnabled.CanEdit, Settings.BooleanText(BlindBoxInput.IsChecked == true), BlindBoxError),
        new(settings.BlindBoxDailyKeyword.Name, settings.BlindBoxDailyKeyword.CanEdit, DailyKeywordInput.Text, DailyKeywordError),
        new(settings.BlindBoxMonthlyKeyword.Name, settings.BlindBoxMonthlyKeyword.CanEdit, MonthlyKeywordInput.Text, MonthlyKeywordError),
        new(settings.BlindBoxAnnounce.Name, settings.BlindBoxAnnounce.CanEdit, Settings.BooleanText(AnnounceInput.IsChecked == true), AnnounceError),
        new(settings.SendIntervalSeconds.Name, settings.SendIntervalSeconds.CanEdit, SendIntervalInput.Text, SendIntervalError),
        new(settings.WelcomeEnabled.Name, settings.WelcomeEnabled.CanEdit, Settings.BooleanText(WelcomeInput.IsChecked == true), WelcomeError),
        new(settings.WelcomeTexts.Name, settings.WelcomeTexts.CanEdit, Settings.TextListText(WelcomeTexts.Select(row => row.Value))),
        new(settings.WelcomeUids.Name, settings.WelcomeUids.CanEdit, WelcomeUidsText()),
        new(settings.WelcomeFanClubLevel.Name, settings.WelcomeFanClubLevel.CanEdit, ConditionValue(WelcomeCondition.FanClubLevel, "0")),
        new(settings.WelcomeGuard.Name, settings.WelcomeGuard.CanEdit, ConditionValue(WelcomeCondition.Guard, Settings.WelcomeGuards[0])),
        new(settings.WelcomeWealthLevel.Name, settings.WelcomeWealthLevel.CanEdit, ConditionValue(WelcomeCondition.WealthLevel, "0")),
        new(settings.WelcomeAnimationEnabled.Name, settings.WelcomeAnimationEnabled.CanEdit, Settings.BooleanText(AnimationInput.IsChecked == true)),
        new(settings.WelcomeAnimationPort.Name, settings.WelcomeAnimationPort.CanEdit, AnimationPortInput.Text, AnimationPortError),
    }.Where(field => field.Name.StartsWith(group, StringComparison.Ordinal)).ToArray();

    private void Validate()
    {
        if (loading || saving || settings is null) return;
        var fields = Fields();
        var error = fields.Select(InputError).FirstOrDefault(reason => reason is not null);
        FileMessage.Text = fileError?.Message ?? "";
        FileMessage.IsVisible = fileError is not null;
        foreach (var field in fields)
        {
            if (field.Error is not { } label) continue;
            label.Text = InputError(field) ?? "";
            label.IsVisible = label.Text.Length > 0;
        }
        var welcome = Section == SettingsSection.Welcome;
        var invalidCondition = welcome && ShowConditionErrors();
        var invalidWelcomeText = welcome && ShowWelcomeTextErrors();
        var animation = Section == SettingsSection.WelcomeAnimation;
        var invalidAnimation = animation && RefreshAnimations();
        DirtyMessage.IsVisible = welcome && ConditionsAddedOrRemoved
            || animation && AnimationDraft
            || fields.Any(field => field.CanEdit && field.Input != original.GetValueOrDefault(field.Name));
        SaveButton.IsEnabled = fileError is null && error is null && !invalidCondition && !invalidWelcomeText && !invalidAnimation && DirtyMessage.IsVisible;
        if (Section != SettingsSection.WelcomeAnimation || DirtyMessage.IsVisible) Message.Text = "";
    }

    /// <summary>Core's reason the input cannot be saved, labelled with the setting; read-only settings are never saved.</summary>
    private static string? InputError(Field field) =>
        field.CanEdit && Settings.Validate(field.Name, field.Input) is { } reason ? $"{field.Name}：{reason}" : null;

    /// <summary>The inputs Save would write; read-only settings keep the snapshot's values.</summary>
    private static Dictionary<string, string> Changes(IEnumerable<Field> fields) =>
        fields.Where(field => field.CanEdit).ToDictionary(field => field.Name, field => field.Input!);

    private async void Save(object? sender, RoutedEventArgs e)
    {
        Validate();
        if (!SaveButton.IsEnabled) return;
        // Core leaves alone what this form was filled with, so an untouched setting keeps its spelling and any edit made elsewhere.
        var changes = Changes(Fields());
        var result = "已保存。";
        long[] replaced = [];
        try
        {
            if (Section == SettingsSection.WelcomeAnimation)
            {
                var rows = Animations.ToDictionary(row => row.Uid!.Value, row => (row.FileName, row.SourcePath));
                replaced = rows.Where(pair => pair.Value.SourcePath is not null).Select(pair => pair.Key).ToArray();
                saveCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                saving = true;
                IsEnabled = false;
                var snapshot = settings;
                result = await Task.Run(() => SaveAnimationFiles(changes, rows, snapshot));
                saving = false;
            }
            else Settings.Save(dataDirectory, changes, settings);
            LoadSettings();
            if (settings.Error is null)
            {
                if (Section == SettingsSection.WelcomeAnimation && viewModel is not null)
                {
                    await viewModel.ReloadAnimationPageAsync(settings);
                    viewModel.ForgetAnimationFailures(replaced);
                }
                SaveCompleted?.Invoke();
                Message.Text = result;
                ReconnectNotice.IsVisible = !AppliesOnRestart && viewModel?.HasRoomConnection == true;
            }
        }
        catch (Exception error) when (error is SettingsException or IOException or UnauthorizedAccessException)
        {
            saving = false;
            // Keep selected source paths after a failed import, so the user can retry or replace the video.
            if (Section != SettingsSection.WelcomeAnimation) LoadSettings();
            else { ShowFileStatus(Settings.Load(dataDirectory)); Validate(); }
            SaveCompleted?.Invoke();
            Message.Text = error.Message;
        }
        finally { saving = false; IsEnabled = true; saveCompletion?.TrySetResult(); }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnRoomStateChanged;
            viewModel.AnimationRowsChanged -= OnAnimationRowsChanged;
        }
        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnRoomStateChanged;
            if (Section == SettingsSection.WelcomeAnimation) viewModel.AnimationRowsChanged += OnAnimationRowsChanged;
        }
        if (Section == SettingsSection.WelcomeAnimation && settings is not null) RefreshAnimations();
    }

    private void OnAnimationRowsChanged() => RefreshAnimations();

    // The room connection the notice warned about has ended (disconnected, reconnected or failed).
    private void OnRoomStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.HasRoomConnection) && viewModel?.HasRoomConnection != true)
            ReconnectNotice.IsVisible = false;
    }

    private void OpenFile(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Settings.FilePath(dataDirectory)) { UseShellExecute = true });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Message.Text = $"打开设置文件失败：{error.Message}";
        }
    }
}
