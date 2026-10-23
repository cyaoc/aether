using System.Globalization;
using Aether.Core;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class WelcomeConditionRow : EditableRow
{
    private string uid = "";
    private string level = "";
    private string guard = "";
    public WelcomeCondition Kind { get; }
    public string Name { get; }
    public Geometry Icon { get; }
    public bool IsUid => Kind == WelcomeCondition.Uid;
    public bool IsGuard => Kind == WelcomeCondition.Guard;
    public bool IsLevel => Kind is WelcomeCondition.FanClubLevel or WelcomeCondition.WealthLevel;
    public decimal? InitialLevel { get; }
    public string LevelName => Name + "门槛";
    public string RemoveName => IsUid ? "删除指定观众 UID" : $"删除{Name}条件";
    public static IReadOnlyList<string> Guards { get; } = Settings.WelcomeGuards.Skip(1).ToArray();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasReadOnlyHint))] private bool hasHeading;
    [ObservableProperty] private bool hasSeparator;
    public bool HasReadOnlyHint => HasHeading && !CanEdit;
    public string Uid { get => uid; set => SetInput(ref uid, value ?? ""); }
    public string Level { get => level; set => SetInput(ref level, value ?? ""); }
    public string Guard { get => guard; set => SetInput(ref guard, value ?? ""); }
    private string Input => IsUid ? Uid : IsGuard ? Guard : Level;

    public WelcomeConditionRow(WelcomeCondition kind, string value, bool canEdit, bool isNew = false)
        : base(isNew && kind == WelcomeCondition.Uid, canEdit)
    {
        Kind = kind;
        if (IsUid) uid = value;
        else if (IsGuard) guard = value;
        else level = value;
        InitialLevel = IsLevel ? int.Parse(value, CultureInfo.InvariantCulture) : null;
        (Name, var icon) = Appearance(kind);
        Icon = Geometry.Parse(icon);
    }

    public string Value => Kind switch
    {
        WelcomeCondition.Uid when Viewer.TryParseUid(Input, out var uid) => uid.ToString(CultureInfo.InvariantCulture),
        WelcomeCondition.FanClubLevel or WelcomeCondition.WealthLevel
            when long.TryParse(Input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) => level.ToString(CultureInfo.InvariantCulture),
        _ => Input
    };

    public string? Validate(ISet<long> uidsAbove)
    {
        if (CanEdit && IsUid)
        {
            if (string.IsNullOrWhiteSpace(Value)) return "填一个 UID，或删掉这条";
            if (!Viewer.TryParseUid(Value, out var uid)) return "UID 必须是正整数";
            if (!uidsAbove.Add(uid)) return "和上面那条重复了";
        }
        // A row means the condition is on; saving 0 would turn it off and drop the row next time settings open.
        else if (CanEdit && IsLevel && (!int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)
            || level <= Settings.WelcomeLevelOff))
            return "至少 1 级";
        return null;
    }

    /// <summary>The name a kind goes by in its row and in hints about it.</summary>
    public static string KindName(WelcomeCondition kind) => Appearance(kind).Name;

    private static (string Name, string Icon) Appearance(WelcomeCondition kind) => kind switch
    {
        WelcomeCondition.Uid => ("指定观众", "M20 21v-2a7 7 0 0 0-14 0v2 M17 7a4 4 0 1 1-8 0a4 4 0 1 1 8 0"),
        WelcomeCondition.FanClubLevel => ("粉丝团等级", "M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1.1-1.1a5.5 5.5 0 0 0-7.8 7.8L12 21l8.8-8.6a5.5 5.5 0 0 0 0-7.8z"),
        WelcomeCondition.Guard => ("大航海身份", "M2 8l5 4 5-9 5 9 5-4-3 12H5z"),
        _ => ("荣耀等级", "M12 3l2.8 5.7 6.2.9-4.5 4.4 1.1 6.2L12 17.3l-5.6 2.9 1.1-6.2L3 9.6l6.2-.9z")
    };
}
