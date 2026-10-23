using System.Globalization;
using Aether.Core;

namespace Aether.Gui;

public partial class WelcomeSettingsPage : SettingsPage
{
    public WelcomeSettingsPage() : this(DataDirectory.Locate()) { }

    public WelcomeSettingsPage(string dataDirectory) : base(dataDirectory)
    {
        InitializeComponent();
        Title = "进场欢迎";
        Bind(WelcomeInput, WelcomeReadOnly, WelcomeError, s => s.WelcomeEnabled);
        WelcomeTextRows.ItemsSource = WelcomeTexts;
        WelcomeTextRows.GotFocus += WelcomeTextGotFocus;
        WelcomeTexts.CollectionChanged += WelcomeTextsChanged;
        WelcomeTexts.RowChanged += Validate;
        WelcomeTextFold.Toggled += RefreshWelcomeTextFold;
        UidRows.ItemsSource = WelcomeUids;
        ConditionRows.ItemsSource = WelcomeThresholds;
        WelcomeUids.CollectionChanged += (_, _) => RefreshConditionList();
        WelcomeThresholds.CollectionChanged += (_, _) => RefreshConditionList();
        WelcomeUids.RowChanged += Validate;
        WelcomeThresholds.RowChanged += Validate;
        UidFold.Toggled += RefreshUidFold;
        LoadSettings();
    }

    protected override void LoadRows()
    {
        LoadWelcomeTexts();
        LoadWelcomeConditions();
    }

    protected override IEnumerable<Field> RowFields() =>
    [
        new(settings.WelcomeTexts.Name, settings.WelcomeTexts.CanEdit, Settings.TextListText(WelcomeTexts.Select(row => row.Value))),
        new(settings.WelcomeUids.Name, settings.WelcomeUids.CanEdit, WelcomeUidsText()),
        new(settings.WelcomeFanClubLevel.Name, settings.WelcomeFanClubLevel.CanEdit, ConditionValue(WelcomeCondition.FanClubLevel, Settings.WelcomeLevelOff.ToString(CultureInfo.InvariantCulture))),
        new(settings.WelcomeGuard.Name, settings.WelcomeGuard.CanEdit, ConditionValue(WelcomeCondition.Guard, Settings.WelcomeGuards[0])),
        new(settings.WelcomeWealthLevel.Name, settings.WelcomeWealthLevel.CanEdit, ConditionValue(WelcomeCondition.WealthLevel, Settings.WelcomeLevelOff.ToString(CultureInfo.InvariantCulture)))
    ];

    protected override bool RowsChanged => ConditionsAddedOrRemoved;

    protected override bool ShowRowErrors()
    {
        var invalidCondition = ShowConditionErrors();
        var invalidWelcomeText = ShowWelcomeTextErrors();
        return invalidCondition || invalidWelcomeText;
    }
}
