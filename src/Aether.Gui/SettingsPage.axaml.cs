using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Aether.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace Aether.Gui;

public enum SettingsSection { BlindBox, Welcome, Send, General }

public partial class SettingsPage : UserControl
{
    private readonly string dataDirectory;
    private readonly string group;
    private Settings settings = null!;

    private Dictionary<string, string?> original = [];
    private bool loading;
    private SettingsException? fileError;
    private List<long> welcomeUids = [];
    private MainWindowViewModel? viewModel;
    public SettingsSection Section { get; }
    /// <summary>Raised once Save has written the file or failed to, so other views can reread it.</summary>
    public event Action? SaveCompleted;

    public SettingsPage() : this(DataDirectory.Locate(), SettingsSection.General) { }

    public SettingsPage(string dataDirectory, SettingsSection section)
    {
        this.dataDirectory = dataDirectory;
        Section = section;
        InitializeComponent();
        (PageTitle.Text, group) = section switch
        {
            SettingsSection.BlindBox => ("盲盒统计", "blind_box."),
            SettingsSection.Welcome => ("进场欢迎", "welcome."),
            SettingsSection.Send => ("发送队列", "send."),
            _ => ("通用设置", "log.")
        };
        TimingHint.Text = AppliesOnRestart ? "重启后生效" : "下次观看直播间时生效";
        BlindBoxHeader.IsVisible = BlindBoxFields.IsVisible = section == SettingsSection.BlindBox;
        WelcomeHeader.IsVisible = WelcomeFields.IsVisible = section == SettingsSection.Welcome;
        SendFields.IsVisible = section == SettingsSection.Send;
        GeneralFields.IsVisible = section == SettingsSection.General;
        SendActivity.IsVisible = section == SettingsSection.Send;
        if (SendActivity.IsVisible)
        {
            FieldsAndSendActivity.RowDefinitions[0].Height = GridLength.Auto;
            FieldsAndSendActivity.RowDefinitions[1].Height = GridLength.Star;
        }
        RecentActivity.IsVisible = section is SettingsSection.Welcome or SettingsSection.BlindBox;
        RecentActivityTitle.Text = section == SettingsSection.Welcome ? "最近欢迎" : "最近回复和播报";
        RecentWelcomesList.IsVisible = WelcomeTotal.IsVisible = section == SettingsSection.Welcome;
        RecentQueriesAndAnnouncementsList.IsVisible = section == SettingsSection.BlindBox;
        SizeChanged += (_, _) => ArrangeActivity();
        LevelInput.ItemsSource = Settings.LogLevels;
        WelcomeGuardInput.ItemsSource = Settings.WelcomeGuards;
        foreach (var number in new[] { RetentionInput, SendIntervalInput, WelcomeFanClubInput, WelcomeWealthInput })
            number.PropertyChanged += (_, e) =>
            {
                if (e.Property == NumericUpDown.TextProperty) Validate();
            };
        LevelInput.SelectionChanged += (_, _) => Validate();
        WelcomeGuardInput.SelectionChanged += (_, _) => Validate();
        KeywordInput.TextChanged += (_, _) => Validate();
        WelcomeTextInput.TextChanged += (_, _) => Validate();
        WelcomeUidsInput.TextChanged += (_, _) => Validate();
        foreach (var toggle in new[] { BlindBoxInput, AnnounceInput, WelcomeInput })
            toggle.PropertyChanged += (_, e) =>
            {
                if (e.Property == ToggleButton.IsCheckedProperty) Validate();
            };
        LoadSettings();
    }

    private void ArrangeActivity()
    {
        if (!RecentActivity.IsVisible) return;
        var stacked = Bounds.Width < 680;
        Grid.SetColumn(RecentActivity, stacked ? 0 : 1);
        Grid.SetRow(RecentActivity, stacked ? 1 : 0);
        RecentActivity.Width = stacked ? double.NaN : 300;
        PageLayout.ColumnSpacing = stacked ? 0 : 24;
        PageLayout.RowSpacing = stacked ? 16 : 0;
        PageLayout.RowDefinitions[1].Height = stacked ? GridLength.Star : new GridLength(0);
    }

    /// <summary>Log settings are read at startup; every other page's settings when watching a room.</summary>
    private bool AppliesOnRestart => Section == SettingsSection.General;

    private void LoadSettings(Settings? current = null)
    {
        loading = true;
        settings = current ?? Settings.Load(dataDirectory);
        ShowFileStatus(settings);
        Show(RetentionInput, RetentionReadOnly, settings.LogRetentionDays);
        Show(LevelInput, LevelReadOnly, settings.LogLevel);
        Show(BlindBoxInput, BlindBoxReadOnly, settings.BlindBoxEnabled);
        Show(KeywordInput, KeywordReadOnly, settings.BlindBoxKeyword);
        Show(AnnounceInput, AnnounceReadOnly, settings.BlindBoxAnnounce);
        Show(SendIntervalInput, SendIntervalReadOnly, settings.SendIntervalSeconds);
        Show(WelcomeInput, WelcomeReadOnly, settings.WelcomeEnabled);
        Show(WelcomeTextInput, WelcomeTextReadOnly, settings.WelcomeText);
        welcomeUids = settings.WelcomeUids.Value.ToList();
        WelcomeUidsInput.Text = "";
        WelcomeUidsInput.IsReadOnly = !settings.WelcomeUids.CanEdit;
        WelcomeUidsReadOnly.IsVisible = !settings.WelcomeUids.CanEdit;
        RenderUidTags();
        foreach (var button in new[] { InsertAtButton, InsertNicknameButton, InsertGuardButton })
            button.IsEnabled = settings.WelcomeText.CanEdit;
        Show(WelcomeFanClubInput, WelcomeFanClubReadOnly, settings.WelcomeFanClubLevel);
        Show(WelcomeGuardInput, WelcomeGuardReadOnly, settings.WelcomeGuard);
        Show(WelcomeWealthInput, WelcomeWealthReadOnly, settings.WelcomeWealthLevel);
        original = Fields().ToDictionary(field => field.Name, field => field.Input);
        loading = false;
        Validate();
    }

    /// <summary>Rereads an untouched page; a draft keeps its inputs and only takes the file's status.</summary>
    public void RefreshFileStatus(Settings current)
    {
        // A snapshot with an error filled the inputs with values that must not be used, so a draft over it is dropped.
        if (settings.Error is not null || !DirtyMessage.IsVisible && string.IsNullOrEmpty(WelcomeUidsInput.Text))
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

    private static void Show(CheckBox input, TextBlock readOnly, Setting<bool> setting)
    {
        input.IsChecked = setting.Value;
        input.IsEnabled = setting.CanEdit;
        readOnly.IsVisible = !setting.CanEdit;
    }

    private readonly record struct Field(string Name, bool CanEdit, string? Input, TextBlock Error);

    /// <summary>Each setting this page edits and the text its control now holds; validating, enabling Save
    /// and saving all read this one list.</summary>
    private Field[] Fields() =>
    new Field[]
    {
        new(settings.LogRetentionDays.Name, settings.LogRetentionDays.CanEdit, RetentionInput.Text, RetentionError),
        new(settings.LogLevel.Name, settings.LogLevel.CanEdit, LevelInput.SelectedItem?.ToString(), LevelError),
        new(settings.BlindBoxEnabled.Name, settings.BlindBoxEnabled.CanEdit, Settings.BooleanText(BlindBoxInput.IsChecked == true), BlindBoxError),
        new(settings.BlindBoxKeyword.Name, settings.BlindBoxKeyword.CanEdit, KeywordInput.Text, KeywordError),
        new(settings.BlindBoxAnnounce.Name, settings.BlindBoxAnnounce.CanEdit, Settings.BooleanText(AnnounceInput.IsChecked == true), AnnounceError),
        new(settings.SendIntervalSeconds.Name, settings.SendIntervalSeconds.CanEdit, SendIntervalInput.Text, SendIntervalError),
        new(settings.WelcomeEnabled.Name, settings.WelcomeEnabled.CanEdit, Settings.BooleanText(WelcomeInput.IsChecked == true), WelcomeError),
        new(settings.WelcomeText.Name, settings.WelcomeText.CanEdit, WelcomeTextInput.Text, WelcomeTextError),
        new(settings.WelcomeUids.Name, settings.WelcomeUids.CanEdit, Settings.NumberListText(welcomeUids), WelcomeUidsError),
        new(settings.WelcomeFanClubLevel.Name, settings.WelcomeFanClubLevel.CanEdit, WelcomeFanClubInput.Text, WelcomeFanClubError),
        new(settings.WelcomeGuard.Name, settings.WelcomeGuard.CanEdit, WelcomeGuardInput.SelectedItem?.ToString(), WelcomeGuardError),
        new(settings.WelcomeWealthLevel.Name, settings.WelcomeWealthLevel.CanEdit, WelcomeWealthInput.Text, WelcomeWealthError),
    }.Where(field => field.Name.StartsWith(group, StringComparison.Ordinal)).ToArray();

    private void Validate()
    {
        if (loading || settings is null) return;
        var fields = Fields();
        var error = fields.Select(InputError).FirstOrDefault(reason => reason is not null);
        FileMessage.Text = fileError?.Message ?? "";
        FileMessage.IsVisible = fileError is not null;
        foreach (var field in fields)
        {
            var label = field.Error;
            label.Text = InputError(field) ?? "";
            label.IsVisible = label.Text.Length > 0;
        }
        if (Section == SettingsSection.Welcome && settings.WelcomeUids.CanEdit
            && !string.IsNullOrWhiteSpace(WelcomeUidsInput.Text) && !TryUid(out _))
        {
            error = $"{settings.WelcomeUids.Name}：UID 必须是正整数。";
            WelcomeUidsError.Text = error;
            WelcomeUidsError.IsVisible = true;
        }
        DirtyMessage.IsVisible = fields.Any(field => field.CanEdit && field.Input != original.GetValueOrDefault(field.Name));
        SaveButton.IsEnabled = fileError is null && error is null && DirtyMessage.IsVisible;
        Message.Text = "";
    }

    /// <summary>Core's reason the input cannot be saved, labelled with the setting; read-only settings are never saved.</summary>
    private static string? InputError(Field field) =>
        field.CanEdit && Settings.Validate(field.Name, field.Input) is { } reason ? $"{field.Name}：{reason}" : null;

    /// <summary>The inputs Save would write; read-only settings keep the snapshot's values.</summary>
    private static Dictionary<string, string> Changes(IEnumerable<Field> fields) =>
        fields.Where(field => field.CanEdit).ToDictionary(field => field.Name, field => field.Input!);

    private void Save(object? sender, RoutedEventArgs e)
    {
        Validate();
        if (!SaveButton.IsEnabled) return;
        // Core leaves alone what this form was filled with, so an untouched setting keeps its spelling and any edit made elsewhere.
        var changes = Changes(Fields());
        try
        {
            Settings.Save(dataDirectory, changes, settings);
            LoadSettings();
            if (settings.Error is null)
            {
                SaveCompleted?.Invoke();
                Message.Text = "已保存。";
                ReconnectNotice.IsVisible = !AppliesOnRestart && viewModel?.HasRoomConnection == true;
            }
        }
        catch (Exception error) when (error is SettingsException or IOException or UnauthorizedAccessException)
        {
            LoadSettings();
            SaveCompleted?.Invoke();
            Message.Text = error.Message;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.PropertyChanged -= OnRoomStateChanged;
        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null) viewModel.PropertyChanged += OnRoomStateChanged;
    }

    // The room connection the notice warned about has ended (disconnected, reconnected or failed).
    private void OnRoomStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.HasRoomConnection) && viewModel?.HasRoomConnection != true)
            ReconnectNotice.IsVisible = false;
    }

    private bool TryUid(out long uid) =>
        long.TryParse(WelcomeUidsInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uid) && uid > 0;

    private void AddUid(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (!settings.WelcomeUids.CanEdit || !TryUid(out var uid)) { Validate(); return; }
        if (!welcomeUids.Contains(uid)) welcomeUids.Add(uid);
        WelcomeUidsInput.Text = "";
        RenderUidTags();
        Validate();
    }

    private void RenderUidTags()
    {
        UidTags.Children.Clear();
        foreach (var uid in welcomeUids)
        {
            var remove = new Button { Content = "×", Padding = new Thickness(6, 0), IsEnabled = settings.WelcomeUids.CanEdit };
            Avalonia.Automation.AutomationProperties.SetName(remove, $"删除 UID {uid}");
            remove.Click += (_, _) =>
            {
                welcomeUids.Remove(uid);
                RenderUidTags();
                Validate();
            };
            var label = new TextBlock { Text = uid.ToString(CultureInfo.InvariantCulture), VerticalAlignment = VerticalAlignment.Center };
            UidTags.Children.Add(new Border
            {
                Classes = { "uid" }, Padding = new Thickness(8, 4), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(6),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { label, remove } }
            });
        }
    }

    private void InsertPlaceholder(object? sender, RoutedEventArgs e)
    {
        if (!settings.WelcomeText.CanEdit || sender is not Button { Tag: string token }) return;
        var text = WelcomeTextInput.Text ?? "";
        if (token == "{@}")
            for (var at = 0; at < text.Length; at++)
            {
                if (text.AsSpan(at).StartsWith("{{", StringComparison.Ordinal)) at++;
                else if (text.AsSpan(at).StartsWith(token, StringComparison.Ordinal)) return;
            }
        var index = token == "{@}" ? 0 : WelcomeTextInput.CaretIndex;
        WelcomeTextInput.Text = text.Insert(index, token);
        WelcomeTextInput.CaretIndex = WelcomeTextInput.SelectionStart = WelcomeTextInput.SelectionEnd = index + token.Length;
        WelcomeTextInput.Focus();
        Validate();
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
