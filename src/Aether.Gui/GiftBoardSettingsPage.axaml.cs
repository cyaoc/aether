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
        Bind(ThresholdInput, ThresholdReadOnly, ThresholdError, settings => settings.GiftBoardThreshold);
        Bind([(Settings.GiftBoardByUnitPrice, ByUnitPriceInput), (Settings.GiftBoardByTotalPrice, ByTotalPriceInput)], ThresholdByReadOnly,
            settings => settings.GiftBoardThresholdBy);
        Bind(ResetOnLiveInput, ResetOnLiveReadOnly, null, settings => settings.GiftBoardResetOnLive);
        Bind(MascotInput, MascotReadOnly, null, settings => settings.GiftBoardMascot);
        Bind(VolumeInput, VolumeReadOnly, settings => settings.GiftBoardVolume);
        LoadSettings();
    }

    protected override bool ReconnectAfterSave => false;

    protected override async Task<string> AfterSave(Settings saved, string result)
    {
        if (viewModel is not null) await viewModel.WebService.ApplyWebSettingsAsync(saved);
        return result;
    }
}
