using Aether.Core;

namespace Aether.Gui;

public partial class GiftBoardSettingsPage : SettingsPage
{
    public GiftBoardSettingsPage() : this(DataDirectory.Locate()) { }

    public GiftBoardSettingsPage(string dataDirectory) : base(dataDirectory)
    {
        InitializeComponent();
        Title = "礼物榜";
        TimingHint = "保存后立即生效";
        Bind(EnabledInput, EnabledReadOnly, null, settings => settings.GiftBoardEnabled);
        LoadSettings();
    }

    protected override bool ReconnectAfterSave => false;

    protected override async Task<string> AfterSave(Settings saved, string result)
    {
        if (viewModel is not null) await viewModel.WebService.ApplyWebSettingsAsync(saved);
        return result;
    }
}
