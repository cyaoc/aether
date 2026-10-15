using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;

namespace Aether.Gui;

public partial class WelcomeAnimationRow : UserControl
{
    public bool CanEdit { get; }
    public string FileName { get; private set; }
    public string? SourcePath { get; private set; }
    public long? Uid => long.TryParse(UidInput.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var uid) && uid > 0 ? uid : null;
    public event Action? Changed;
    public event Action? RemoveRequested;
    public event Action? ChooseRequested;
    public event Action? TestRequested;

    public WelcomeAnimationRow() : this("", "", true) { }

    public WelcomeAnimationRow(string uid, string fileName, bool canEdit)
    {
        InitializeComponent();
        CanEdit = canEdit;
        FileName = fileName;
        UidInput.Text = uid;
        UidInput.IsReadOnly = !canEdit;
        FileNameText.Text = fileName;
        FileButton.IsEnabled = canEdit;
        RemoveButton.IsVisible = canEdit;
        UidInput.TextChanged += (_, _) => Changed?.Invoke();
        RemoveButton.Click += (_, _) => { if (CanEdit) RemoveRequested?.Invoke(); };
        FileButton.Click += (_, _) => { if (CanEdit) ChooseRequested?.Invoke(); };
        TestButton.Click += (_, _) => { if (TestButton.IsEnabled) TestRequested?.Invoke(); };
    }

    public void SelectVideo(string path)
    {
        SourcePath = path;
        FileName = Path.GetFileName(path);
        FileNameText.Text = FileName;
        Changed?.Invoke();
    }

    public bool Refresh(ISet<long> uids, string directory, bool saved, bool hasPages, bool busy, bool playing, bool failed)
    {
        var error = string.IsNullOrWhiteSpace(UidInput.Text) ? "UID 没填，填一个 UID 或删掉这条"
            : Uid is not { } uid ? "UID 不合法，必须是正整数"
            : !uids.Add(uid) ? "UID 重复，和上面那条重复了" : null;
        var exists = File.Exists(SourcePath ?? Path.Combine(directory, FileName));
        FileNameText.TextDecorations = exists ? null : TextDecorations.Strikethrough;
        ToolTip.SetTip(FileButton, FileName);
        var message = error is not null ? "⚠ " + error
            : !exists ? "⚠ 找不到视频文件，请重新选择"
            : playing ? "▶ 正在播放"
            : !saved ? "⚠ 保存后才能试播"
            : failed ? "✕ 上次播放失败，请换成 H.264 编码的 MP4" : "";
        if (!CanEdit) message += (message.Length > 0 ? "\n" : "") + "这项不能逐行编辑，请在设置文件里改";
        RowMessage.Text = message;
        RowMessage.IsVisible = message.Length > 0;
        RowMessage.Classes.Set("error", error is not null || !exists || failed && saved);
        TestButton.Content = playing ? "播放中" : "试播";
        TestButton.IsEnabled = error is null && saved && hasPages && exists && !busy;
        return error is not null;
    }
}
