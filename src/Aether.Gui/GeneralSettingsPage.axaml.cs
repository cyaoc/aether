using System.Diagnostics;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Gui;

public partial class GeneralSettingsPage : SettingsPage
{
    private readonly Dictionary<Button, IDisposable> copyFeedback = [];
    private bool savedPortChanged;
    private bool savedAutoUpdate;

    public GeneralSettingsPage() : this(DataDirectory.Locate()) { }

    public GeneralSettingsPage(string dataDirectory) : base(dataDirectory)
    {
        InitializeComponent();
        Title = "通用设置";
        TimingHint = "端口保存后立即生效；日志设置重启后生效；自动更新下次打开时生效";
        LogLevelInput.ItemsSource = Settings.LogLevels;
        // Save appends missing groups in this order, so log stays ahead of web_service.
        Bind(RetentionInput, RetentionReadOnly, RetentionError, s => s.LogRetentionDays);
        Bind(LogLevelInput, LogLevelReadOnly, LogLevelError, s => s.LogLevel);
        Bind(WebPortInput, WebPortReadOnly, WebPortError, s => s.WebServicePort);
        AutoUpdateInput.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) Validate(); };
        DetachedFromVisualTree += (_, _) =>
        {
            foreach (var (button, timer) in copyFeedback) { timer.Dispose(); button.Classes.Set("copied", false); }
            copyFeedback.Clear();
        };
        LoadSettings();
    }

    protected override bool AppliesToWebService => true;
    private bool PortChanged => settings.WebServicePort.CanEdit && WebPortInput.Text != original.GetValueOrDefault(settings.WebServicePort.Name);
    protected override string DirtyDescription => PortChanged ? "保存后网页服务换到新端口，直播姬里的地址要跟着改" : base.DirtyDescription;

    // The switch lives in the GUI settings (ADR 0015) but saves with the page. The window's own copy is the one changed,
    // or its next save of gui.yml would write the old value back.
    private GuiSettings Gui => viewModel?.GuiSettings ?? GuiSettings.Load(dataDirectory, NullLogger<GuiSettings>.Instance);
    protected override void LoadRows() => AutoUpdateInput.IsChecked = savedAutoUpdate = Gui.AutoUpdate;
    protected override bool RowsChanged => AutoUpdateInput.IsChecked != savedAutoUpdate;

    protected override async Task<string> WriteSettings(Dictionary<string, string> changes)
    {
        savedPortChanged = PortChanged;
        var result = await base.WriteSettings(changes);
        var gui = Gui;
        gui.AutoUpdate = AutoUpdateInput.IsChecked == true;
        gui.Save();
        return result;
    }

    protected override async Task<string> AfterSave(Settings saved, string result)
    {
        result = await base.AfterSave(saved, result);
        return savedPortChanged && viewModel?.WebService.WebServiceStatus.Running == true ? "已保存，网页服务已换到新端口" : result;
    }

    // Every page row's copy button, heard here as the click reaches the page list.
    private async void CopyPageAddress(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(button.Tag as string ?? "");
            if (copyFeedback.Remove(button, out var pending)) pending.Dispose();
            button.Classes.Set("copied", true);
            copyFeedback[button] = DispatcherTimer.RunOnce(() => { button.Classes.Set("copied", false); copyFeedback.Remove(button); }, TimeSpan.FromSeconds(2));
        }
        catch (Exception error) { Message = $"复制地址失败：{error.Message}"; }
    }

    private void OpenFile(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Settings.FilePath(dataDirectory)) { UseShellExecute = true });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Message = $"打开设置文件失败：{error.Message}";
        }
    }
}
