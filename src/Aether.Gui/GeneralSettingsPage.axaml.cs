using System.Diagnostics;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Aether.Gui;

public partial class GeneralSettingsPage : SettingsPage
{
    private readonly Dictionary<Button, IDisposable> copyFeedback = [];
    private bool savedPortChanged;

    public GeneralSettingsPage() : this(DataDirectory.Locate()) { }

    public GeneralSettingsPage(string dataDirectory) : base(dataDirectory)
    {
        InitializeComponent();
        Title = "通用设置";
        LogLevelInput.ItemsSource = Settings.LogLevels;
        // Save appends missing groups in this order, so log stays ahead of web_service.
        Bind(RetentionInput, RetentionReadOnly, RetentionError, s => s.LogRetentionDays);
        Bind(LogLevelInput, LogLevelReadOnly, LogLevelError, s => s.LogLevel);
        Bind(WebPortInput, WebPortReadOnly, WebPortError, s => s.WebServicePort);
        Bind(AutoUpdateInput, AutoUpdateReadOnly, null, s => s.AutoUpdateEnabled);
        Tabs.SelectionChanged += (_, _) => ShowTab();
        ShowTab();
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

    protected override Task<string> WriteSettings(Dictionary<string, string> changes)
    {
        savedPortChanged = PortChanged;
        return base.WriteSettings(changes);
    }

    protected override async Task<string> AfterSave(Settings saved, string result)
    {
        result = await base.AfterSave(saved, result);
        return savedPortChanged && viewModel?.WebService.WebServiceStatus.Running == true ? "已保存，网页服务已换到新端口" : result;
    }

    /// <summary>Opens the 网页服务 tab with <paramref name="page"/> picked in the page list.</summary>
    public void ShowWebPage(WebPageTexts page)
    {
        Tabs.SelectedIndex = 0;
        PageList.SelectedItem = PageList.Items.OfType<ListBoxItem>().Single(item => page.Equals(item.Tag));
    }

    private void ShowTab()
    {
        var webService = Tabs.SelectedIndex == 0;
        WebServiceTab.IsVisible = webService;
        BelowBodyContent = webService ? PagesTab : null;
        UpdateTab.IsVisible = !webService;
        TimingHint = webService ? "端口保存后立即生效" : "日志设置重启后生效；自动更新下次打开时生效";
    }

    // Every page detail's copy button, heard here as the click reaches the details beside the page list.
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
