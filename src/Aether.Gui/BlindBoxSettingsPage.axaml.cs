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
        Bind(DailyKeywordInput, DailyKeywordReadOnly, null, s => s.BlindBoxDailyKeyword);
        Bind(MonthlyKeywordInput, MonthlyKeywordReadOnly, null, s => s.BlindBoxMonthlyKeyword);
        Bind(YearlyKeywordInput, YearlyKeywordReadOnly, null, s => s.BlindBoxYearlyKeyword);
        Bind(AnnounceInput, AnnounceReadOnly, AnnounceError, s => s.BlindBoxAnnounce);
        LoadSettings();
    }

    /// <summary>A query keyword left empty, or that is the 1 or 2 a viewer takes a 站队 with in the 桌宠's 宝箱小游戏, shows Core's why
    /// on its own error line.</summary>
    protected override bool ShowRowErrors()
    {
        var invalid = false;
        foreach (var (input, error) in new[] { (DailyKeywordInput, DailyKeywordError), (MonthlyKeywordInput, MonthlyKeywordError), (YearlyKeywordInput, YearlyKeywordError) })
        {
            var why = input.IsReadOnly ? null : Settings.ValidateBlindBoxKeyword(input.Text ?? "");
            (error.Text, error.IsVisible) = (why ?? "", why is not null);
            invalid |= why is not null;
        }
        return invalid;
    }
}
