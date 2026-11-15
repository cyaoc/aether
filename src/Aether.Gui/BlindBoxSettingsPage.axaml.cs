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
        Bind(YearlyKeywordInput, YearlyKeywordReadOnly, YearlyKeywordError, s => s.BlindBoxYearlyKeyword);
        Bind(AnnounceInput, AnnounceReadOnly, AnnounceError, s => s.BlindBoxAnnounce);
        LoadSettings();
    }

    /// <summary>A query keyword that is the 桌宠's 帮它的关键字 shows Core's why on its own error line, as an invalid one does.</summary>
    protected override bool ShowRowErrors()
    {
        var invalid = false;
        foreach (var (input, error) in new[] { (DailyKeywordInput, DailyKeywordError), (MonthlyKeywordInput, MonthlyKeywordError), (YearlyKeywordInput, YearlyKeywordError) })
        {
            if (input.IsReadOnly || error.IsVisible || settings.ValidateBlindBoxKeyword(input.Text ?? "") is not { } why) continue;
            (error.Text, error.IsVisible, invalid) = (why, true, true);
        }
        return invalid;
    }
}
