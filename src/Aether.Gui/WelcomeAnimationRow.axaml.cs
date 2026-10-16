using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace Aether.Gui;

public partial class WelcomeAnimationRow : UserControl
{
    private bool errorsRevealed;
    public bool CanEdit { get; }
    public string FileName { get; private set; }
    public string? SourcePath { get; private set; }
    public long? Uid => long.TryParse(UidInput.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var uid) && uid > 0 ? uid : null;
    public event Action? Changed;
    public event Action? RemoveRequested;
    public event Action? ChooseRequested;
    public event Action? TestRequested;

    public WelcomeAnimationRow() : this("", "", true) { }

    public WelcomeAnimationRow(string uid, string fileName, bool canEdit, bool isNew = false)
    {
        errorsRevealed = !isNew;
        InitializeComponent();
        CanEdit = canEdit;
        FileName = fileName;
        UidInput.Text = uid;
        UidInput.IsReadOnly = !canEdit;
        FileNameText.Text = fileName;
        FileButton.IsEnabled = canEdit;
        RemoveButton.IsVisible = canEdit;
        UidInput.TextChanged += (_, _) => Changed?.Invoke();
        UidInput.LostFocus += (_, _) => { errorsRevealed = true; Changed?.Invoke(); };
        RemoveButton.Click += (_, _) => { if (CanEdit) RemoveRequested?.Invoke(); };
        FileButton.Click += (_, _) => { if (CanEdit) ChooseRequested?.Invoke(); };
        TestButton.Click += (_, _) => { if (TestButton.IsEnabled) TestRequested?.Invoke(); };
    }

    public bool CanTest => TestButton.IsEnabled;

    /// <summary>Rows share one bordered list; every row but the first draws the line above it.</summary>
    public void ShowDivider(bool visible) => RowBorder.BorderThickness = new Thickness(0, visible ? 1 : 0, 0, 0);

    public void FocusInput()
    {
        UidInput.Focus();
        this.BringIntoView();
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
        var error = string.IsNullOrWhiteSpace(UidInput.Text) ? "填一个 UID，或删掉这条"
            : Uid is not { } uid ? "UID 必须是正整数"
            : !uids.Add(uid) ? "和上面那条重复了" : null;
        var exists = File.Exists(SourcePath ?? Path.Combine(directory, FileName));
        var visibleError = errorsRevealed ? error : null;
        UidInput.Classes.Set("invalid", visibleError is not null);
        FileButton.Classes.Set("missing", !exists);
        FileIcon.Data = Icon(exists ? "Icon.Animation" : "Icon.Alert");
        FileNameText.TextDecorations = exists ? null : TextDecorations.Strikethrough;
        ToolTip.SetTip(FileButton, "点击更换视频");
        // The canvas's order: what blocks saving or playing first, then why the last playback failed, then progress.
        var (note, tone, icon) = visibleError is not null ? (visibleError, "error", "Icon.Alert")
            : !exists ? ("找不到视频文件，点文件名重新选择", "error", "Icon.Alert")
            : failed ? ("上次播放失败：格式不支持，换成 H.264 编码的 MP4", "error", "Icon.Fail")
            : !saved ? ("保存后才能试播", null, "Icon.Info")
            : playing ? ("正在直播姬里播放", "playing", "Icon.Wave") : ("", null, "Icon.Info");
        if (!CanEdit) note += (note.Length > 0 ? "\n" : "") + "这项不能逐行编辑，请在设置文件里改";
        RowMessage.Text = note;
        NoteIcon.Data = Icon(icon);
        RowNote.IsVisible = note.Length > 0;
        RowNote.Classes.Set("error", tone == "error");
        RowNote.Classes.Set("playing", tone == "playing");
        var who = Uid is { } owner ? $"{owner} 的" : "";
        AutomationProperties.SetName(FileButton, $"更换{who}视频：{FileName}");
        AutomationProperties.SetName(TestButton, (playing ? "正在试播" : "试播") + who + "欢迎动画");
        AutomationProperties.SetName(RemoveButton, $"删除{who}欢迎动画");
        TestButton.Classes.Set("playing", playing);
        TestIcon.Data = Icon(playing ? "Icon.Wave" : "Icon.Play");
        TestText.Text = playing ? "播放中" : "试播";
        TestButton.IsEnabled = error is null && saved && hasPages && exists && !busy;
        return error is not null;
    }

    private static Geometry Icon(string key) => (Geometry)Application.Current!.FindResource(key)!;
}
