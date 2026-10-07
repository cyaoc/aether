using System.Diagnostics;
using System.Globalization;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.Logging;

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
        LevelInput.ItemsSource = Enum.GetValues<LogLevel>().Where(level => level != LogLevel.None).ToArray();
        RetentionInput.PropertyChanged += (_, e) =>
        {
            if (e.Property == NumericUpDown.TextProperty) Validate();
        };
        LevelInput.SelectionChanged += (_, _) => Validate();
        LoadSettings();
    }

    private void LoadSettings()
    {
        settings = Settings.Load(dataDirectory);
        RetentionInput.Text = settings.LogRetentionDays.Value.ToString(CultureInfo.InvariantCulture);
        RetentionInput.IsReadOnly = !settings.LogRetentionDays.CanEdit;
        RetentionInput.AllowSpin = settings.LogRetentionDays.CanEdit;
        RetentionReadOnly.IsVisible = !settings.LogRetentionDays.CanEdit;
        LevelInput.SelectedItem = settings.LogLevel.Value;
        LevelInput.IsEnabled = settings.LogLevel.CanEdit;
        LevelReadOnly.IsVisible = !settings.LogLevel.CanEdit;
        UnknownKeysMessage.IsVisible = settings.UnknownKeys.Count > 0;
        UnknownKeysMessage.Text = $"未知设置键：{string.Join("、", settings.UnknownKeys)}。保存后会原样保留。";
        Validate();
    }

    private void Validate()
    {
        var error = settings.Error?.Message;
        if (error is null && settings.LogRetentionDays.CanEdit
            && Settings.Validate("log.retention_days", RetentionInput.Text) is { } daysError)
            error = $"log.retention_days：{daysError}";
        if (error is null && settings.LogLevel.CanEdit
            && Settings.Validate("log.level", LevelInput.SelectedItem?.ToString()) is { } levelError)
            error = $"log.level：{levelError}";
        Message.Text = error ?? "";
        SaveButton.IsEnabled = error is null && (settings.LogRetentionDays.CanEdit || settings.LogLevel.CanEdit);
    }

    private void Save(object? sender, RoutedEventArgs e)
    {
        Validate();
        if (!SaveButton.IsEnabled) return;
        var changes = new Dictionary<string, string>();
        if (settings.LogRetentionDays.CanEdit && int.Parse(RetentionInput.Text!, CultureInfo.InvariantCulture) != settings.LogRetentionDays.Value)
            changes["log.retention_days"] = RetentionInput.Text!;
        if (settings.LogLevel.CanEdit && (LogLevel)LevelInput.SelectedItem! != settings.LogLevel.Value)
            changes["log.level"] = LevelInput.SelectedItem.ToString()!;
        try
        {
            Settings.Save(dataDirectory, changes);
            LoadSettings();
            if (settings.Error is null) Message.Text = "已保存，日志设置重启后生效。";
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
