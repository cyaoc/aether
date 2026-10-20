using Aether.Core;

namespace Aether.Gui;

public partial class BlindBoxSettingsPage : SettingsPage
{
    public BlindBoxSettingsPage() : this(DataDirectory.Locate()) { }

    public BlindBoxSettingsPage(string dataDirectory) : base(dataDirectory)
    {
        InitializeComponent();
        Title = "盲盒统计";
        Bind(BlindBoxInput, BlindBoxReadOnly, BlindBoxError, s => s.BlindBoxEnabled);
        Bind(DailyKeywordInput, DailyKeywordReadOnly, DailyKeywordError, s => s.BlindBoxDailyKeyword);
        Bind(MonthlyKeywordInput, MonthlyKeywordReadOnly, MonthlyKeywordError, s => s.BlindBoxMonthlyKeyword);
        Bind(AnnounceInput, AnnounceReadOnly, AnnounceError, s => s.BlindBoxAnnounce);
        LoadSettings();
    }
}
