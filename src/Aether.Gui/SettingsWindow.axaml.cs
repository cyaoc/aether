using System.Diagnostics;
using System.Globalization;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aether.Gui;

public partial class SettingsWindow : Window
{
    private readonly string dataDirectory;
    private Settings settings = null!;

    public SettingsWindow() : this(DataDirectory.Locate()) { }

    public SettingsWindow(string dataDirectory)
    {
        this.dataDirectory = dataDirectory;
        InitializeComponent();
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
        LoadSettings();
    }

    private void LoadSettings()
    {
        settings = Settings.Load(dataDirectory);
        Show(RetentionInput, RetentionReadOnly, settings.LogRetentionDays);
        Show(LevelInput, LevelReadOnly, settings.LogLevel);
        Show(BlindBoxInput, BlindBoxReadOnly, settings.BlindBoxEnabled);
        Show(KeywordInput, KeywordReadOnly, settings.BlindBoxKeyword);
        Show(AnnounceInput, AnnounceReadOnly, settings.BlindBoxAnnounce);
        Show(SendIntervalInput, SendIntervalReadOnly, settings.SendIntervalSeconds);
        Show(WelcomeInput, WelcomeReadOnly, settings.WelcomeEnabled);
        Show(WelcomeTextInput, WelcomeTextReadOnly, settings.WelcomeText);
        Show(WelcomeUidsInput, WelcomeUidsReadOnly, settings.WelcomeUids);
        Show(WelcomeFanClubInput, WelcomeFanClubReadOnly, settings.WelcomeFanClubLevel);
        Show(WelcomeGuardInput, WelcomeGuardReadOnly, settings.WelcomeGuard);
        Show(WelcomeWealthInput, WelcomeWealthReadOnly, settings.WelcomeWealthLevel);
        UnknownKeysMessage.IsVisible = settings.UnknownKeys.Count > 0;
        UnknownKeysMessage.Text = $"未知设置键：{string.Join("、", settings.UnknownKeys)}。保存后会原样保留。";
        Validate();
    }

    // Each control shows its setting; a line that cannot be edited safely leaves it read-only beside its hint.
    private static void Show(NumericUpDown input, TextBlock readOnly, Setting<int> setting)
    {
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

    private static void Show(TextBox input, TextBlock readOnly, Setting<IReadOnlyList<long>> setting)
    {
        input.Text = Settings.NumberListText(setting.Value);
        input.IsReadOnly = !setting.CanEdit;
        readOnly.IsVisible = !setting.CanEdit;
    }

    private static void Show(CheckBox input, TextBlock readOnly, Setting<bool> setting)
    {
        input.IsChecked = setting.Value;
        input.IsEnabled = setting.CanEdit;
        readOnly.IsVisible = !setting.CanEdit;
    }

    /// <summary>Each setting this form edits and the text its control now holds; validating, enabling Save
    /// and saving all read this one list.</summary>
    private (string Name, bool CanEdit, string? Input)[] Fields() =>
    [
        (settings.LogRetentionDays.Name, settings.LogRetentionDays.CanEdit, RetentionInput.Text),
        (settings.LogLevel.Name, settings.LogLevel.CanEdit, LevelInput.SelectedItem?.ToString()),
        (settings.BlindBoxEnabled.Name, settings.BlindBoxEnabled.CanEdit, Settings.BooleanText(BlindBoxInput.IsChecked == true)),
        (settings.BlindBoxKeyword.Name, settings.BlindBoxKeyword.CanEdit, KeywordInput.Text),
        (settings.BlindBoxAnnounce.Name, settings.BlindBoxAnnounce.CanEdit, Settings.BooleanText(AnnounceInput.IsChecked == true)),
        (settings.SendIntervalSeconds.Name, settings.SendIntervalSeconds.CanEdit, SendIntervalInput.Text),
        (settings.WelcomeEnabled.Name, settings.WelcomeEnabled.CanEdit, Settings.BooleanText(WelcomeInput.IsChecked == true)),
        (settings.WelcomeText.Name, settings.WelcomeText.CanEdit, WelcomeTextInput.Text),
        (settings.WelcomeUids.Name, settings.WelcomeUids.CanEdit, WelcomeUidsInput.Text),
        (settings.WelcomeFanClubLevel.Name, settings.WelcomeFanClubLevel.CanEdit, WelcomeFanClubInput.Text),
        (settings.WelcomeGuard.Name, settings.WelcomeGuard.CanEdit, WelcomeGuardInput.SelectedItem?.ToString()),
        (settings.WelcomeWealthLevel.Name, settings.WelcomeWealthLevel.CanEdit, WelcomeWealthInput.Text),
    ];

    private void Validate()
    {
        var fields = Fields();
        var error = settings.Error?.Message ?? fields.Select(InputError).FirstOrDefault(reason => reason is not null);
        if (error is null && settings.ValidateWelcomeText(Changes(fields)) is { } reason) error = $"{settings.WelcomeText.Name}：{reason}";
        Message.Text = error ?? "";
        SaveButton.IsEnabled = error is null && fields.Any(field => field.CanEdit);
    }

    /// <summary>Core's reason the input cannot be saved, labelled with the setting; read-only settings are never saved.</summary>
    private static string? InputError((string Name, bool CanEdit, string? Input) field) =>
        field.CanEdit && Settings.Validate(field.Name, field.Input) is { } reason ? $"{field.Name}：{reason}" : null;

    /// <summary>The inputs Save would write; read-only settings keep the snapshot's values.</summary>
    private static Dictionary<string, string> Changes(IEnumerable<(string Name, bool CanEdit, string? Input)> fields) =>
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
            if (settings.Error is null) Message.Text = "已保存，各项按标注的时机生效。";
        }
        catch (Exception error) when (error is SettingsException or IOException or UnauthorizedAccessException)
        {
            LoadSettings();
            Message.Text = error.Message;
        }
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
