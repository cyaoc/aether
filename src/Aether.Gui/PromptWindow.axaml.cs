using Aether.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aether.Gui;

/// <summary>A short message with its buttons on the right, in one look for every prompt. Closes with true only when the user
/// confirms; a notice has nothing to confirm and keeps only the first button.</summary>
public partial class PromptWindow : Window
{
    public PromptWindow() => InitializeComponent();

    private PromptWindow(string title, string message, string cancel, string? confirm) : this()
    {
        Title = title;
        MessageText.Text = message;
        CancelButton.Content = cancel;
        ConfirmButton.Content = confirm;
        ConfirmButton.IsVisible = confirm is not null;
    }

    public static PromptWindow ConfirmLogout() => new("退出登录", "退出后需要重新扫码登录；正在连接的直播间会断开。", "取消", "退出登录");

    /// <summary>ADR 0018: another process still holds data/ after the wait.</summary>
    public static PromptWindow AlreadyRunning() => new("Aether", DataDirectory.AlreadyRunning, "知道了", confirm: null);

    private void Cancel(object? sender, RoutedEventArgs e) => Close(false);

    private void Confirm(object? sender, RoutedEventArgs e) => Close(true);
}
