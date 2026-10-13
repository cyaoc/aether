using System.Globalization;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Aether.Gui;

public partial class SettingsPage
{
    private IEnumerable<WelcomeConditionRow> Conditions => ConditionRows.Children.Cast<WelcomeConditionRow>();

    private void LoadWelcomeConditions()
    {
        ConditionRows.Children.Clear();
        foreach (var uid in settings.WelcomeUids.Value)
            AddCondition(WelcomeCondition.Uid, uid.ToString(CultureInfo.InvariantCulture), settings.WelcomeUids.CanEdit);
        if (settings.WelcomeFanClubLevel.Value > 0)
            AddCondition(WelcomeCondition.FanClubLevel, settings.WelcomeFanClubLevel.Value.ToString(CultureInfo.InvariantCulture), settings.WelcomeFanClubLevel.CanEdit);
        if (settings.WelcomeGuard.Value != "关闭")
            AddCondition(WelcomeCondition.Guard, settings.WelcomeGuard.Value, settings.WelcomeGuard.CanEdit);
        if (settings.WelcomeWealthLevel.Value > 0)
            AddCondition(WelcomeCondition.WealthLevel, settings.WelcomeWealthLevel.Value.ToString(CultureInfo.InvariantCulture), settings.WelcomeWealthLevel.CanEdit);
        UpdateConditions();
    }

    private WelcomeConditionRow AddCondition(WelcomeCondition kind, string value, bool canEdit)
    {
        var row = new WelcomeConditionRow(kind, value, canEdit);
        var index = Conditions.TakeWhile(existing => existing.Kind <= kind).Count();
        ConditionRows.Children.Insert(index, row);
        row.Changed += Validate;
        row.RemoveRequested += () =>
        {
            ConditionRows.Children.Remove(row);
            UpdateConditions();
            Validate();
        };
        return row;
    }

    private void UpdateConditions()
    {
        var count = ConditionRows.Children.Count;
        ConditionCount.Text = $"{count} 条";
        ConditionCount.IsVisible = count > 0;
        EmptyConditions.IsVisible = count == 0;
        ConditionsReadOnly.IsVisible = Enum.GetValues<WelcomeCondition>().Any(kind => !CanEditCondition(kind) && !Conditions.Any(row => row.Kind == kind));
        FanClubAdded.IsVisible = Conditions.Any(row => row.Kind == WelcomeCondition.FanClubLevel);
        GuardAdded.IsVisible = Conditions.Any(row => row.Kind == WelcomeCondition.Guard);
        WealthAdded.IsVisible = Conditions.Any(row => row.Kind == WelcomeCondition.WealthLevel);
        foreach (var item in ((MenuFlyout)AddConditionButton.Flyout!).Items.Cast<MenuItem>())
        {
            var kind = (WelcomeCondition)item.Tag!;
            item.IsEnabled = CanEditCondition(kind) && (kind == WelcomeCondition.Uid || !Conditions.Any(row => row.Kind == kind));
        }
        var first = true;
        foreach (var row in Conditions)
        {
            row.ShowSeparator(!first);
            first = false;
        }
    }

    private bool CanEditCondition(WelcomeCondition kind) => kind switch
    {
        WelcomeCondition.Uid => settings.WelcomeUids.CanEdit,
        WelcomeCondition.FanClubLevel => settings.WelcomeFanClubLevel.CanEdit,
        WelcomeCondition.Guard => settings.WelcomeGuard.CanEdit,
        _ => settings.WelcomeWealthLevel.CanEdit
    };

    private void AddWelcomeCondition(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { IsEnabled: true, Tag: WelcomeCondition kind }) return;
        var value = kind switch
        {
            WelcomeCondition.Uid => "",
            WelcomeCondition.FanClubLevel => "1",
            WelcomeCondition.Guard => "舰长",
            _ => "10"
        };
        var row = AddCondition(kind, value, true);
        UpdateConditions();
        Validate();
        AddConditionButton.Flyout!.Hide();
        // Menu dismissal restores focus to its button; focus the new input after that finishes.
        Dispatcher.UIThread.Post(row.FocusInput, DispatcherPriority.Loaded);
    }

    private string ConditionValue(WelcomeCondition kind, string absent) =>
        Conditions.FirstOrDefault(row => row.Kind == kind)?.Value ?? absent;

    private bool ValidateWelcomeConditions()
    {
        var invalid = false;
        var uids = new HashSet<long>();
        foreach (var row in Conditions)
        {
            string? error = null;
            if (row.CanEdit && row.Kind == WelcomeCondition.Uid)
            {
                if (string.IsNullOrWhiteSpace(row.Value)) error = "填一个 UID，或删掉这条";
                else if (!long.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid) || uid <= 0)
                    error = "UID 必须是正整数";
                else if (!uids.Add(uid)) error = "和上面那条重复了";
            }
            else if (row.CanEdit && row.Kind is WelcomeCondition.FanClubLevel or WelcomeCondition.WealthLevel
                && (!int.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) || level < 1))
                error = "至少 1 级";
            row.SetError(error);
            invalid |= error is not null;
        }
        return invalid;
    }
}
