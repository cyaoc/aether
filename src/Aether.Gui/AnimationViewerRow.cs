using Aether.Core;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class AnimationViewerRow(string uid, string fileName, bool canEdit, bool isNew = false) : EditableRow(isNew, canEdit)
{
    public static IReadOnlyList<string> Choices { get; } = EntryTier.All.Select(tier => tier.ToString()).Concat(["欢迎动画", "不播"]).ToArray();
    public static Avalonia.Controls.Templates.IDataTemplate ChoiceTemplate { get; } = new Avalonia.Controls.Templates.FuncDataTemplate<string>((value, _) =>
        new Avalonia.Controls.TextBlock { Text = int.TryParse(value, out var number) ? $"{value} 档" : value });
    private string choice = "欢迎动画";
    public string Choice { get => choice; set { SetInput(ref choice, value); OnPropertyChanged(nameof(IsVideo)); } }
    public bool IsVideo => Choice == "欢迎动画";
    public string SettingValue => IsVideo ? FileName : Choice == "不播" ? "0" : Choice;
    private string uidText = uid;
    private string fileName = fileName;
    private string? sourcePath;
    public string UidText { get => uidText; set => SetInput(ref uidText, value ?? ""); }
    public long? Uid => Viewer.TryParseUid(UidText, out var uid) ? uid : null;
    public string FileName { get => fileName; private set { SetProperty(ref fileName, value); OnPropertyChanged(nameof(HasFile)); OnPropertyChanged(nameof(FileText)); } }
    public bool HasFile => FileName.Length > 0;
    public string FileText => HasFile ? FileName : "选一个视频";
    public string? SourcePath => sourcePath;
    [ObservableProperty] private bool canTest;
    [ObservableProperty] private bool missing;
    [ObservableProperty] private bool playing;
    [ObservableProperty] private bool stoppable;
    [ObservableProperty] private bool noteError;
    [ObservableProperty] private bool notePlaying;
    [ObservableProperty] private string rowMessage = "";
    [ObservableProperty] private string testText = "";
    [ObservableProperty] private string fileAccessibleName = "";
    [ObservableProperty] private string testAccessibleName = "";
    [ObservableProperty] private string removeAccessibleName = "";
    [ObservableProperty] private Geometry? fileIcon;
    [ObservableProperty] private Geometry? noteIcon;
    [ObservableProperty] private Geometry? testIcon;
    [ObservableProperty] private TextDecorationCollection? fileNameDecorations;

    public void SelectVideo(string path)
    {
        FileName = Path.GetFileName(path);
        SetInput(ref sourcePath, path, nameof(SourcePath));
    }
}
