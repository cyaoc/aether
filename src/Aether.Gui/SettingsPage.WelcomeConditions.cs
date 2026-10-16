using System.Globalization;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Aether.Gui;

public partial class SettingsPage
{
    private WelcomeCondition[] loadedConditionKinds = [];

    private IEnumerable<WelcomeConditionRow> Conditions => UidRows.Children.Concat(ConditionRows.Children).Cast<WelcomeConditionRow>();

    private void LoadWelcomeConditions()
    {
        ConditionRows.Children.Clear();
        UidRows.Children.Clear();
        UidFold.IsExpanded = false;
        foreach (var uid in settings.WelcomeUids.Value)
            InsertCondition(WelcomeCondition.Uid, uid.ToString(CultureInfo.InvariantCulture), settings.WelcomeUids.CanEdit);
        if (settings.WelcomeFanClubLevel.Value > 0)
            InsertCondition(WelcomeCondition.FanClubLevel, settings.WelcomeFanClubLevel.Value.ToString(CultureInfo.InvariantCulture), settings.WelcomeFanClubLevel.CanEdit);
        if (settings.WelcomeGuard.Value != Settings.WelcomeGuards[0])
            InsertCondition(WelcomeCondition.Guard, settings.WelcomeGuard.Value, settings.WelcomeGuard.CanEdit);
        if (settings.WelcomeWealthLevel.Value > 0)
            InsertCondition(WelcomeCondition.WealthLevel, settings.WelcomeWealthLevel.Value.ToString(CultureInfo.InvariantCulture), settings.WelcomeWealthLevel.CanEdit);
        loadedConditionKinds = Conditions.Select(row => row.Kind).ToArray();
        RefreshConditionList();
    }

    private WelcomeConditionRow InsertCondition(WelcomeCondition kind, string value, bool canEdit, bool isNew = false)
    {
        var row = new WelcomeConditionRow(kind, value, canEdit, isNew);
        var rows = kind == WelcomeCondition.Uid ? UidRows : ConditionRows;
        var index = rows.Children.Cast<WelcomeConditionRow>().TakeWhile(existing => existing.Kind <= kind).Count();
        rows.Children.Insert(index, row);
        row.Changed += Validate;
        row.RemoveRequested += () =>
        {
            rows.Children.Remove(row);
            RefreshConditionList();
            Validate();
        };
        return row;
    }

    /// <summary>Updates what depends on which rows exist: the count, the empty state, the menu and the "或" separators.</summary>
    private void RefreshConditionList()
    {
        var count = Conditions.Select(row => row.Kind).Distinct().Count();
        UidGroup.IsVisible = UidRows.Children.Count > 0;
        ConditionCount.Text = $"{count} 条";
        ConditionCount.IsVisible = count > 0;
        EmptyConditions.IsVisible = count == 0;
        // A read-only row carries its own hint; a read-only kind with no row is named here, since its menu item is disabled.
        var readOnly = Enum.GetValues<WelcomeCondition>()
            .Where(kind => !CanEditCondition(kind) && !Conditions.Any(row => row.Kind == kind))
            .Select(WelcomeConditionRow.KindName).ToArray();
        ConditionsReadOnly.Text = $"{string.Join("、", readOnly)}是多行写法，请在设置文件里改";
        ConditionsReadOnly.IsVisible = readOnly.Length > 0;
        FanClubAdded.IsVisible = Conditions.Any(row => row.Kind == WelcomeCondition.FanClubLevel);
        GuardAdded.IsVisible = Conditions.Any(row => row.Kind == WelcomeCondition.Guard);
        WealthAdded.IsVisible = Conditions.Any(row => row.Kind == WelcomeCondition.WealthLevel);
        foreach (var item in ((MenuFlyout)AddConditionButton.Flyout!).Items.Cast<MenuItem>())
        {
            var kind = (WelcomeCondition)item.Tag!;
            item.IsEnabled = CanEditCondition(kind) && (kind == WelcomeCondition.Uid || !Conditions.Any(row => row.Kind == kind));
        }
        WelcomeCondition? previous = null;
        var uidIndex = 0;
        foreach (var row in Conditions)
        {
            row.ShowSeparator(previous is not null && previous != row.Kind);
            row.ShowHeading(previous != row.Kind);
            if (row.Kind == WelcomeCondition.Uid)
                row.IsVisible = uidIndex++ < 3 || UidFold.IsExpanded;
            previous = row.Kind;
        }
        RefreshUidFold();
    }

    private bool CanEditCondition(WelcomeCondition kind) => kind switch
    {
        WelcomeCondition.Uid => settings.WelcomeUids.CanEdit,
        WelcomeCondition.FanClubLevel => settings.WelcomeFanClubLevel.CanEdit,
        WelcomeCondition.Guard => settings.WelcomeGuard.CanEdit,
        _ => settings.WelcomeWealthLevel.CanEdit
    };

    private void AddConditionFromMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { IsEnabled: true, Tag: WelcomeCondition kind }) return;
        var value = kind switch
        {
            WelcomeCondition.Uid => "",
            WelcomeCondition.FanClubLevel => "1",
            WelcomeCondition.Guard => "舰长",
            _ => "10"
        };
        var row = InsertCondition(kind, value, true, isNew: true);
        if (kind == WelcomeCondition.Uid) UidFold.IsExpanded = true;
        RefreshConditionList();
        Validate();
        AddConditionButton.Flyout!.Hide();
        // Menu dismissal restores focus to its button; focus the new input after that finishes.
        Dispatcher.UIThread.Post(row.FocusInput, DispatcherPriority.Loaded);
    }

    private string ConditionValue(WelcomeCondition kind, string absent) =>
        Conditions.FirstOrDefault(row => row.Kind == kind)?.Value ?? absent;

    /// <summary>The UID rows as welcome.uids text. Their order means nothing, so the file's UIDs in another order
    /// (one removed and added back) keep the file's text.</summary>
    private string WelcomeUidsText()
    {
        var uids = Conditions.Where(row => row.Kind == WelcomeCondition.Uid).Select(row => row.Value).ToArray();
        var loaded = settings.WelcomeUids.Value.Select(uid => uid.ToString(CultureInfo.InvariantCulture));
        return uids.Order(StringComparer.Ordinal).SequenceEqual(loaded.Order(StringComparer.Ordinal))
            ? Settings.NumberListText(settings.WelcomeUids.Value) : $"[{string.Join(", ", uids)}]";
    }

    /// <summary>A row added or removed is a draft even when it maps to the file's values, such as a new level left at 0.</summary>
    private bool ConditionsAddedOrRemoved => !Conditions.Select(row => row.Kind).SequenceEqual(loadedConditionKinds);

    /// <summary>Shows each row's error; true when any row cannot be saved.</summary>
    private bool ShowConditionErrors()
    {
        var invalid = false;
        var uidsAbove = new HashSet<long>();
        foreach (var row in Conditions) invalid |= row.ShowError(uidsAbove);
        RefreshUidFold();
        return invalid;
    }

    private void RefreshUidFold() => UidFold.Refresh(UidRows.Children.Count,
        UidRows.Children.Cast<WelcomeConditionRow>().Skip(3).Count(row => row.HasVisibleError), "位");
}
