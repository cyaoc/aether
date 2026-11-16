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

    /// <summary>A query keyword left empty or that is the 桌宠's 帮它的关键字 shows Core's why on its own error line, as the
    /// 帮它的关键字 does.</summary>
    protected override bool ShowRowErrors()
    {
        var invalid = false;
        foreach (var (input, error) in new[] { (DailyKeywordInput, DailyKeywordError), (MonthlyKeywordInput, MonthlyKeywordError), (YearlyKeywordInput, YearlyKeywordError) })
        {
            var why = input.IsReadOnly ? null : settings.ValidateBlindBoxKeyword(input.Text ?? "");
            (error.Text, error.IsVisible) = (why ?? "", why is not null);
            invalid |= why is not null;
        }
        return invalid;
    }
}
