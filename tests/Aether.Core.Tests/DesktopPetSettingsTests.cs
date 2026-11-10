using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

/// <summary>The 桌宠's settings (#359): the desktop_pet group of the settings file, read, checked and written back a line at a time,
/// with the streamer's lines kept only for the kinds changed.</summary>
public sealed class DesktopPetSettingsTests : IAsyncDisposable
{
    private readonly WatchHarness h = new();
    private string SettingsPath => Settings.FilePath(h.DataDirectory);

    [Fact]
    public void The_design_is_the_default_and_each_value_reads_back_from_its_line()
    {
        Assert.Equal(("", "1000 10000", "15 40 60", "20 40", 25), Values(Settings.Load(h.DataDirectory)));

        Write("desktop_pet:\n  shows_off: [entry, hum]\n  mid_gift_spend: 500\n  big_gift_spend: 5000\n  bored_minutes: 0\n  gloomy_minutes: 30\n"
            + "  sleepy_minutes: 90\n  daily_min_seconds: 10\n  daily_max_seconds: 15\n  mishap_percent: 0\n");
        Assert.Equal(("entry hum", "500 5000", "0 30 90", "10 15", 0), Values(Settings.Load(h.DataDirectory)));
    }

    [Theory]
    [InlineData("shows_off: [entry, nope]", "desktop_pet.shows_off", "演出表里没有能关掉的 nope。")]
    [InlineData("shows_off: [bored]", "desktop_pet.shows_off", "演出表里没有能关掉的 bored。")]
    [InlineData("mid_gift_spend: 0", "desktop_pet.mid_gift_spend", "必须为正整数。")]
    [InlineData("bored_minutes: -1", "desktop_pet.bored_minutes", "必须为非负整数。")]
    [InlineData("daily_max_seconds: 0", "desktop_pet.daily_max_seconds", "必须为正整数。")]
    [InlineData("mishap_percent: 101", "desktop_pet.mishap_percent", "必须是 0–100。")]
    [InlineData("mid_gift_spend: 10000", "desktop_pet.big_gift_spend", "中礼物门槛要小于大礼物门槛。")]
    [InlineData("mid_gift_spend: 2147483647", "desktop_pet.big_gift_spend", "中礼物门槛要小于大礼物门槛。")]
    [InlineData("lines.1.bigGift: [\"开出{奖品}了\"]", "desktop_pet.lines.1.bigGift", "这一类台词不能用 {奖品}，只能用 {昵称}、{礼物}、{个数}。")]
    [InlineData("lines.1.bigGift: []", "desktop_pet.lines.1.bigGift", "台词列表不能为空。")]
    [InlineData("lines.1.bigGift: [\"好\", \"\"]", "desktop_pet.lines.1.bigGift", "每一句都不能为空。")]
    [InlineData("lines.1.nope: [\"好\"]", "desktop_pet.lines.1.nope", "必须是 lines.角色编号.台词类，例如 lines.1.bigGift；台词类见演出表。")]
    [InlineData("lines.0.bigGift: [\"好\"]", "desktop_pet.lines.0.bigGift", "必须是 lines.角色编号.台词类，例如 lines.1.bigGift；台词类见演出表。")]
    public void A_wrong_value_is_reported_on_its_line_with_why_and_the_file_is_left_alone(string line, string name, string reason)
    {
        var yaml = $"desktop_pet:\n  enabled: true\n  big_gift_spend: 10000\n  {line}\n";
        Write(yaml);

        var error = Assert.IsType<SettingsException>(Settings.Load(h.DataDirectory).Error);

        Assert.Equal(name, error.SettingName);
        Assert.EndsWith("）：" + reason, error.Message);
        Assert.Equal(name.EndsWith("big_gift_spend") ? 3 : 4, error.LineNumber);
        Assert.Equal(yaml, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void The_daily_actions_least_seconds_may_equal_the_most_but_not_exceed_them()
    {
        Write("desktop_pet:\n  daily_min_seconds: 30\n  daily_max_seconds: 30\n");
        Assert.Equal(("", "1000 10000", "15 40 60", "30 30", 25), Values(Settings.Load(h.DataDirectory)));

        Write("desktop_pet:\n  daily_min_seconds: 31\n  daily_max_seconds: 30\n");
        var error = Assert.IsType<SettingsException>(Settings.Load(h.DataDirectory).Error);

        Assert.Equal(("desktop_pet.daily_max_seconds", 3), (error.SettingName, error.LineNumber));
        Assert.EndsWith("）：日常小动作的最短间隔不能比最长间隔长。", error.Message);
    }

    [Fact]
    public void Saving_a_threshold_that_is_not_below_the_big_one_is_refused_without_writing()
    {
        Settings.Load(h.DataDirectory);
        var before = File.ReadAllText(SettingsPath);

        var error = Assert.Throws<SettingsException>(() => Settings.Save(h.DataDirectory,
            new Dictionary<string, string> { ["desktop_pet.mid_gift_spend"] = "20000" }));

        Assert.Equal("desktop_pet.big_gift_spend", error.SettingName);
        Assert.Equal(before, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void An_old_file_without_the_group_gets_all_of_it_and_keeps_every_other_byte()
    {
        Settings.Load(h.DataDirectory);
        var template = File.ReadAllText(SettingsPath);
        var group = template[template.IndexOf("# 桌宠，", StringComparison.Ordinal)..template.IndexOf("# 网页服务，", StringComparison.Ordinal)];
        var old = template.Replace(group, "") + "future: 保留\n";
        Write(old);

        var settings = Settings.Load(h.DataDirectory);

        Assert.Null(settings.Error);
        Assert.Null(settings.UpgradedFromVersion);
        Assert.Equal(old + "\n" + group.TrimEnd('\n') + "\n", File.ReadAllText(SettingsPath));
        Assert.Equal(("", "1000 10000", "15 40 60", "20 40", 25), Values(settings));
    }

    [Fact]
    public void Each_setting_saves_on_its_own_line()
    {
        Settings.Load(h.DataDirectory);
        var before = File.ReadAllText(SettingsPath);

        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["desktop_pet.shows_off"] = Settings.TextListText(["entry", "hum"]),
            ["desktop_pet.big_gift_spend"] = "20000",
            ["desktop_pet.mishap_percent"] = "0",
        });

        Assert.Equal(before.Replace("  shows_off: []\n", "  shows_off: [\"entry\", \"hum\"]\n").Replace("  big_gift_spend: 10000\n", "  big_gift_spend: 20000\n")
            .Replace("  mishap_percent: 25\n", "  mishap_percent: 0\n"), File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Only_the_changed_kinds_of_lines_are_kept_and_one_set_back_to_hers_is_deleted()
    {
        var snapshot = Settings.Load(h.DataDirectory);
        var before = File.ReadAllText(SettingsPath);
        IReadOnlyList<string> mine = ["哇！{昵称}送了{礼物}！", "{昵称}，谢谢你～"];

        Settings.SaveDesktopPet(h.DataDirectory, new Dictionary<string, string> { ["desktop_pet.enabled"] = "true" },
            new Dictionary<string, IReadOnlyList<string>> { ["bigGift"] = mine, ["gift"] = Character.Current.Lines["gift"] }, snapshot);

        Assert.Equal(before.Replace("false 时桌宠页全透明\n  enabled: false", "false 时桌宠页全透明\n  enabled: true")
            .Replace("  mishap_percent: 25\n", "  mishap_percent: 25\n  lines.1.bigGift: [\"哇！{昵称}送了{礼物}！\", \"{昵称}，谢谢你～\"]\n"),
            File.ReadAllText(SettingsPath));
        var saved = Settings.Load(h.DataDirectory);
        Assert.Equal(["bigGift"], saved.DesktopPetChangedLines.Keys);
        var lines = saved.DesktopPetLines(Character.Current);
        Assert.Equal(Character.Current.Lines.Keys, lines.Keys);
        Assert.Equal(mine, lines["bigGift"]);
        Assert.Equal(Character.Current.Lines["gift"], lines["gift"]);

        Settings.SaveDesktopPet(h.DataDirectory, new Dictionary<string, string> { ["desktop_pet.enabled"] = "false" },
            new Dictionary<string, IReadOnlyList<string>> { ["bigGift"] = Character.Current.Lines["bigGift"] }, saved);

        Assert.Equal(before, File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("  shows_off: [entry]\n  gloomy_minutes: 30\n")]
    [InlineData("  shows_off: [entry, hum]\n")]
    [InlineData("  shows_off: []\n")]
    [InlineData("  shows_off: [entry]\n  lines.1.bigGift: [\"谢谢{昵称}\"]\n")]
    public void The_desktop_pet_page_is_told_the_same_only_while_her_settings_and_lines_are(string changed)
    {
        Write("desktop_pet:\n  shows_off: [entry]\n");
        var first = Settings.Load(h.DataDirectory);
        Assert.Null(first.Error);
        Assert.True(first.SameDesktopPet(Settings.Load(h.DataDirectory)));

        Write("desktop_pet:\n" + changed);
        var second = Settings.Load(h.DataDirectory);
        Assert.Null(second.Error);
        Assert.False(first.SameDesktopPet(second));
    }

    [Fact]
    public void Saving_wrong_lines_is_refused_with_why_without_writing()
    {
        var snapshot = Settings.Load(h.DataDirectory);
        var before = File.ReadAllText(SettingsPath);

        var error = Assert.Throws<SettingsException>(() => Settings.SaveDesktopPet(h.DataDirectory, new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<string>> { ["entry"] = ["{礼物}来啦"] }, snapshot));

        Assert.Equal("desktop_pet.lines.1.entry", error.SettingName);
        Assert.EndsWith("这一类台词不能用 {礼物}，只能用 {昵称}。", error.Message);
        Assert.Equal(before, File.ReadAllText(SettingsPath));
    }

    private static (string, string, string, string, int) Values(Settings settings)
    {
        Assert.Null(settings.Error);
        return (string.Join(' ', settings.DesktopPetShowsOff.Value), Text([settings.DesktopPetMidGift, settings.DesktopPetBigGift]),
            Text([settings.DesktopPetBoredMinutes, settings.DesktopPetGloomyMinutes, settings.DesktopPetSleepyMinutes]),
            Text([settings.DesktopPetDailyMinSeconds, settings.DesktopPetDailyMaxSeconds]), settings.DesktopPetMishapPercent.Value);

        static string Text(IEnumerable<Setting<int>> numbers) => string.Join(' ', numbers.Select(number => number.Value));
    }

    private void Write(string yaml)
    {
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(SettingsPath, yaml);
    }

    public ValueTask DisposeAsync() => h.DisposeAsync();
}
