using Aether.Core;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class WelcomeAnimationRow(string uid, string fileName, bool canEdit, bool isNew = false) : EditableRow(isNew)
{
    private string uidText = uid;
    private string fileName = fileName;
    private string? sourcePath;
    public string UidText { get => uidText; set => SetInput(ref uidText, value ?? ""); }
    public long? Uid => Viewer.TryParseUid(UidText, out var uid) ? uid : null;
    public string FileName { get => fileName; private set => SetProperty(ref fileName, value); }
    public string? SourcePath => sourcePath;
    public bool CanEdit { get; } = canEdit;
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

    public new void Remove()
    {
        if (CanEdit) base.Remove();
    }
}
