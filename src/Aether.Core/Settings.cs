using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Aether.Core;

public sealed record Setting<T>(string Name, T Value, bool CanEdit = true);

public sealed class SettingsException(string filePath, long lineNumber, string settingName, string reason)
    : Exception($"{filePath} 第 {lineNumber} 行（{settingName}）：{reason}")
{
    public string FilePath { get; } = filePath;
    public long LineNumber { get; } = lineNumber;
    public string SettingName { get; } = settingName;
}

/// <summary>A settings snapshot. Values must not be used when Error is present; CanEdit describes safe line editing.</summary>
public sealed partial class Settings
{
    private const string Template = """
        # 设置文件格式版本，由程序维护
        format_version: 1

        # 日志设置，修改后重启程序生效
        log:
          # 日志保留天数，必须为正整数
          retention_days: 30
          # 日志级别：Trace、Debug、Information、Warning、Error、Critical
          level: Information

        # 盲盒统计，下次观看直播间时生效
        blind_box:
          # true 或 false，false 时既不记录，也不回复和播报
          enabled: true
          # 今日查询关键字，非空文字
          daily_keyword: 今日盲盒
          # 本月查询关键字，非空文字；和今日关键字相同时只回复今日
          monthly_keyword: 本月盲盒
          # 今年查询关键字，非空文字；和今日或本月关键字相同时只回复今日或本月
          yearly_keyword: 今年盲盒
          # true 或 false，送完一轮盲盒后主动 @ 播报这一轮的投入和盈亏
          announce: true

        # 发送队列，下次观看直播间时生效
        send:
          # 两次发送之间的秒数，必须为正整数
          interval_seconds: 1

        # 进场欢迎，下次观看直播间时生效
        welcome_danmaku:
          # true 或 false，满足下面任意一个欢迎条件的观众进场时，发一条欢迎弹幕
          enabled: false
          # 欢迎语列表，可以写多条，每次随机发一条；列表和每一条都不能为空
          # 可用 {@}（@ 这位观众，只能放开头，不算字数）、{昵称}（太长时自动截短）、
          # {大航海}（舰长/提督/总督，不是大航海身份时为空）；要写花括号本身，用 {{ 和 }}
          # 用 ["欢迎{大航海}{昵称}", "{@}欢迎回家"] 的单行写法；每条加双引号，双引号和反斜杠写成 \" 和 \\
          # 英文、数字、半角符号算 1，其他字符算 2。上限自动读取账号的
          texts: ["欢迎{大航海}{昵称}"]
          # 指定观众的 UID，例如 [12345, 67890]；[] 表示不按 UID 欢迎
          uids: []
          # 本直播间粉丝团等级达到多少就欢迎，0 表示不按粉丝团等级欢迎
          fan_club_level: 0
          # 大航海身份门槛：0 关闭，1 舰长及以上，2 提督及以上，3 总督
          guard: 1
          # 荣耀等级达到多少就欢迎，0 表示不按荣耀等级欢迎
          wealth_level: 0

        # 进场欢迎：动画。进场动画和欢迎动画共用这个开关；只在 GUI 里生效，保存后下一次进场生效
        entry_animation:
          enabled: false
          # 粉丝团等级门槛：30、40、50 或 60，0 表示不按等级
          fan_club_level: 30
          # 音量，0 到 100，0 表示静音；进场动画、欢迎动画和送礼动画共用
          volume: 80

        # 动画名单：一行一位，值是 30、40、50、60，0 表示不播，或视频文件名；只在 GUI 里生效
        # UID 必须为正整数；视频放在 data/videos/，只能是 .mp4、.webm 文件名，不能带路径
        entry_animation_viewers:
          # 12345: 40

        # 送礼动画，只在 GUI 里生效，保存后下一次送礼生效
        gift_animation:
          enabled: false
          # 每一档的投入门槛（电池），必须是正整数，一档比一档高
          tier_30_spend: 100
          tier_40_spend: 500
          tier_50_spend: 2000
          tier_60_spend: 10000

        # 桌宠，只在 GUI 里生效，保存后立即生效；桌宠页 /desktop-pet 加成浏览器素材，放在直播画面一角
        # 改过的台词跟在分组最后，一类一行：lines.角色编号.台词类的英文 id: ["第一句", "第二句"]，没写的类用角色自带的台词
        # 写法和欢迎语一样，列表和每一句都不能为空；占位只能用这一类台词能用的，例如 {昵称}、{礼物}、{个数}
        desktop_pet:
          # true 或 false，桌宠按直播间里发生的事表演；false 时桌宠页全透明
          enabled: false
          # 不演的演出和日常小动作，写演出表里的英文 id，例如 ["entry", "hum"]；[] 表示全都演
          shows_off: []
          # 中礼物、大礼物门槛（电池），按一轮的投入比，盲盒也一样；必须为正整数，中礼物门槛要小于大礼物门槛
          mid_gift_spend: 1000
          big_gift_spend: 10000
          # 冷场几分钟进入无聊、沮丧（没收到礼物）和犯困（什么事都没有）；0 表示不进这种心情
          bored_minutes: 15
          gloomy_minutes: 40
          sleepy_minutes: 60
          # 平静时隔几秒做一个日常小动作，在最少和最多之间随机；必须为正整数，最少不能比最多大
          daily_min_seconds: 20
          daily_max_seconds: 40
          # 日常小动作出事的几率，0 到 100（%），0 表示不出事
          mishap_percent: 25

        # 网页服务，视频页 /video、抽奖页 /lottery、礼物榜页 /gift-board、桌宠页 /desktop-pet 都从这里打开；只在 GUI 里生效，先开 Aether 再开推流软件
        web_service:
          # 端口 1–65535；保存后立即生效，所有浏览器素材的地址都要跟着改
          port: 17890

        # 自动更新，只在 GUI 里生效，下次打开 GUI 时生效
        auto_update:
          # true 或 false，打开 GUI 时检查并自动更新
          enabled: true

        """;

    internal const string LineBreaks = "\r\n\u0085\u2028\u2029"; // YAML also accepts Unicode line breaks.

    // Static fields initialise in text order: this list must stay above every Register(...) below.
    private static readonly List<Definition> Definitions = [];
    /// <summary>Every setting this version knows; only tests read it.</summary>
    internal static IReadOnlyList<Definition> AllDefinitions => Definitions;
    private static readonly Definition<int> RetentionDays = Register(Definition.Number("log.retention_days", 30, "必须为正整数。", days => days > 0));
    /// <summary>The values log.level accepts, in order.</summary>
    public static IReadOnlyList<LogLevel> LogLevels { get; } =
    [
        Microsoft.Extensions.Logging.LogLevel.Trace, Microsoft.Extensions.Logging.LogLevel.Debug,
        Microsoft.Extensions.Logging.LogLevel.Information, Microsoft.Extensions.Logging.LogLevel.Warning,
        Microsoft.Extensions.Logging.LogLevel.Error, Microsoft.Extensions.Logging.LogLevel.Critical,
    ];
    private static readonly Definition<LogLevel> Level =
        Register(Definition.Choice("log.level", Microsoft.Extensions.Logging.LogLevel.Information, LogLevels));
    private static readonly Definition<bool> Enabled = Register(Definition.Boolean("blind_box.enabled", true));
    private static readonly Definition<string> DailyKeyword = Register(Definition.RequiredText("blind_box.daily_keyword", "今日盲盒"));
    private static readonly Definition<string> MonthlyKeyword = Register(Definition.RequiredText("blind_box.monthly_keyword", "本月盲盒"));
    private static readonly Definition<string> YearlyKeyword = Register(Definition.RequiredText("blind_box.yearly_keyword", "今年盲盒"));
    private static readonly Definition<bool> Announce = Register(Definition.Boolean("blind_box.announce", true));
    private static readonly Definition<int> SendInterval = Register(Definition.Number("send.interval_seconds", 1, "必须为正整数。", seconds => seconds > 0));
    private static readonly Definition<bool> WelcomeOn = Register(Definition.Boolean("welcome_danmaku.enabled", false));
    private static readonly Definition<IReadOnlyList<string>> WelcomeTextList = Register(Definition.TextList("welcome_danmaku.texts", ["欢迎{大航海}{昵称}"],
        texts => texts.Count == 0 ? "欢迎语列表不能为空。" : texts.Select(Core.WelcomeText.Validate).FirstOrDefault(error => error is not null)));
    private static readonly Definition<IReadOnlyList<long>> WelcomeUidList = Register(Definition.NumberList("welcome_danmaku.uids", []));
    public const int WelcomeLevelOff = 0;
    private static readonly Definition<int> WelcomeFanClub = Register(Definition.Number("welcome_danmaku.fan_club_level", WelcomeLevelOff, "必须为非负整数。", level => level >= 0));
    /// <summary>The values welcome_danmaku.guard accepts: 关闭, then 大航海身份 from low to high, so each one's index is its tier.</summary>
    public static IReadOnlyList<string> WelcomeGuards { get; } = ["关闭", "舰长", "提督", "总督"];
    private static readonly Definition<int> WelcomeGuardTier = Register(Definition.Choice("welcome_danmaku.guard", 1, new[] { 0, 1, 2, 3 }));
    private static readonly Definition<int> WelcomeWealth = Register(Definition.Number("welcome_danmaku.wealth_level", WelcomeLevelOff, "必须为非负整数。", level => level >= 0));
    private static readonly Definition<bool> AnimationOn = Register(Definition.Boolean("entry_animation.enabled", false));
    private static readonly Definition<int> AnimationLevel = Register(Definition.Number("entry_animation.fan_club_level", 30, "必须是 0、30、40、50、60。", value => value == 0 || EntryTier.Parse(value) is not null));
    private static readonly Definition<int> AnimationVolume = Register(Definition.Percent("entry_animation.volume", 80));
    private static readonly Definition<bool> GiftOn = Register(Definition.Boolean("gift_animation.enabled", false));
    private static readonly Definition<int>[] GiftThresholds = EntryTier.All.Select((tier, index) => Register(Definition.Number($"gift_animation.tier_{tier}_spend", new[] { 100, 500, 2000, 10000 }[index], "必须为正整数。", value => value > 0))).ToArray();
    public Setting<bool> EntryAnimationEnabled => Get(AnimationOn);
    public Setting<int> EntryAnimationFanClubLevel => Get(AnimationLevel);
    public Setting<int> EntryAnimationVolume => Get(AnimationVolume);
    public Setting<bool> GiftAnimationEnabled => Get(GiftOn);
    public IReadOnlyList<Setting<int>> GiftAnimationThresholds => GiftThresholds.Select(Get).ToArray();
    private static readonly Definition<bool> DesktopPetOn = Register(Definition.Boolean("desktop_pet.enabled", false));
    private static readonly Definition<IReadOnlyList<string>> PetShowsOff = Register(Definition.TextList("desktop_pet.shows_off", [],
        ids => ids.FirstOrDefault(id => !ShowTable.Current.Shows.Any(show => show.Id == id && show.CanTurnOff)) is { } wrong
            ? $"演出表里没有能关掉的 {wrong}。" : null));
    private static readonly Definition<int> PetMidGift = Register(Definition.Number("desktop_pet.mid_gift_spend", 1000, "必须为正整数。", value => value > 0));
    private static readonly Definition<int> PetBigGift = Register(Definition.Number("desktop_pet.big_gift_spend", 10000, "必须为正整数。", value => value > 0));
    private static readonly Definition<int> PetBoredMinutes = Register(Definition.Number("desktop_pet.bored_minutes", 15, "必须为非负整数。", minutes => minutes >= 0));
    private static readonly Definition<int> PetGloomyMinutes = Register(Definition.Number("desktop_pet.gloomy_minutes", 40, "必须为非负整数。", minutes => minutes >= 0));
    private static readonly Definition<int> PetSleepyMinutes = Register(Definition.Number("desktop_pet.sleepy_minutes", 60, "必须为非负整数。", minutes => minutes >= 0));
    private static readonly Definition<int> PetDailyMin = Register(Definition.Number("desktop_pet.daily_min_seconds", 20, "必须为正整数。", seconds => seconds > 0));
    private static readonly Definition<int> PetDailyMax = Register(Definition.Number("desktop_pet.daily_max_seconds", 40, "必须为正整数。", seconds => seconds > 0));
    private static readonly Definition<int> PetMishapPercent = Register(Definition.Percent("desktop_pet.mishap_percent", 25));
    /// <summary>The name the 桌宠's changed lines go under, a line for each kind, such as desktop_pet.lines.1.bigGift.</summary>
    public const string DesktopPetLinesName = "desktop_pet.lines";
    private static readonly LineDefinition PetLines = Register(new LineDefinition(DesktopPetLinesName));
    public Setting<bool> DesktopPetEnabled => Get(DesktopPetOn);
    /// <summary>The ids of the 演出表's rows turned off.</summary>
    public Setting<IReadOnlyList<string>> DesktopPetShowsOff => Get(PetShowsOff);
    /// <summary>The 中礼物 threshold, in 电池.</summary>
    public Setting<int> DesktopPetMidGift => Get(PetMidGift);
    /// <summary>The 大礼物 threshold, in 电池.</summary>
    public Setting<int> DesktopPetBigGift => Get(PetBigGift);
    /// <summary>Quiet minutes before 无聊.</summary>
    public Setting<int> DesktopPetBoredMinutes => Get(PetBoredMinutes);
    /// <summary>Minutes without a gift before 沮丧.</summary>
    public Setting<int> DesktopPetGloomyMinutes => Get(PetGloomyMinutes);
    /// <summary>Minutes with nothing at all before 犯困.</summary>
    public Setting<int> DesktopPetSleepyMinutes => Get(PetSleepyMinutes);
    /// <summary>The least seconds between 日常小动作.</summary>
    public Setting<int> DesktopPetDailyMinSeconds => Get(PetDailyMin);
    /// <summary>The most seconds between 日常小动作.</summary>
    public Setting<int> DesktopPetDailyMaxSeconds => Get(PetDailyMax);
    public Setting<int> DesktopPetMishapPercent => Get(PetMishapPercent);
    /// <summary>The current character's kinds of lines the streamer changed, by kind.</summary>
    public IReadOnlyDictionary<string, Setting<IReadOnlyList<string>>> DesktopPetChangedLines => PetLines.Items(this)
        .Where(item => item.Key.Character == Character.CurrentNumber).ToDictionary(item => item.Key.Kind, item => item.Value);
    /// <summary>The current character's lines, by kind: <paramref name="character"/>'s, with each kind the streamer changed
    /// replaced.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DesktopPetLines(Character character)
    {
        var changed = DesktopPetChangedLines;
        return character.Lines.ToDictionary(kind => kind.Key, kind => changed.TryGetValue(kind.Key, out var mine) ? mine.Value : kind.Value);
    }
    public bool CanEditDesktopPetLines => PetLines.CanEdit(this);
    /// <summary>Whether the 桌宠页 would be told the same settings and lines as under <paramref name="other"/>.</summary>
    public bool SameDesktopPet(Settings other)
    {
        var (mine, theirs) = (DesktopPetSettings.Of(this), DesktopPetSettings.Of(other));
        var lines = other.DesktopPetLines(Character.Current);
        // A record compares its list Off by reference, so Off is compared on its own.
        return DesktopPetEnabled.Value == other.DesktopPetEnabled.Value && mine.Off.SequenceEqual(theirs.Off) && mine with { Off = theirs.Off } == theirs
            && DesktopPetLines(Character.Current).All(kind => kind.Value.SequenceEqual(lines[kind.Key]));
    }
    internal static readonly Definition<int> WebPort = Register(Definition.Number("web_service.port", 17890, "必须是 1–65535 的端口。", port => port is >= 1 and <= 65535));
    private static readonly ViewerGroupDefinition AnimationViewers = Register(new ViewerGroupDefinition("entry_animation_viewers"));
    private static readonly Definition<bool> AutoUpdate = Register(Definition.Boolean("auto_update.enabled", true));
    /// <summary>Adding a setting means registering its definition here, adding its property and a line in Template,
    /// then a Bind call in its GUI settings page (composite settings need their row code instead).
    /// The GUI coverage test catches settings omitted from the pages.</summary>
    private static T Register<T>(T definition) where T : Definition
    {
        Definitions.Add(definition);
        return definition;
    }

    public Setting<int> LogRetentionDays => Get(RetentionDays);
    public Setting<LogLevel> LogLevel => Get(Level);
    public Setting<bool> BlindBoxEnabled => Get(Enabled);
    public Setting<string> BlindBoxDailyKeyword => Get(DailyKeyword);
    public Setting<string> BlindBoxMonthlyKeyword => Get(MonthlyKeyword);
    public Setting<string> BlindBoxYearlyKeyword => Get(YearlyKeyword);
    public Setting<bool> BlindBoxAnnounce => Get(Announce);
    public Setting<int> SendIntervalSeconds => Get(SendInterval);
    public Setting<bool> WelcomeEnabled => Get(WelcomeOn);
    public Setting<IReadOnlyList<string>> WelcomeTexts => Get(WelcomeTextList);
    public Setting<IReadOnlyList<long>> WelcomeUids => Get(WelcomeUidList);
    public Setting<int> WelcomeFanClubLevel => Get(WelcomeFanClub);
    public Setting<int> WelcomeGuard => Get(WelcomeGuardTier);
    public Setting<int> WelcomeWealthLevel => Get(WelcomeWealth);
    public Setting<int> WebServicePort => Get(WebPort);
    public Setting<bool> AutoUpdateEnabled => Get(AutoUpdate);
    public IReadOnlyDictionary<long, Setting<AnimationChoice>> EntryAnimationViewers => AnimationViewers.Items(this);
    public IReadOnlyDictionary<long, Setting<string>> WelcomeAnimationVideos => EntryAnimationViewers.Where(pair => pair.Value.Value.Video is not null).ToDictionary(pair => pair.Key, pair => new Setting<string>(pair.Value.Name, pair.Value.Value.Video!, pair.Value.CanEdit));
    public bool CanEditEntryAnimationViewers => AnimationViewers.CanEdit(this);
    public IReadOnlyList<string> UnknownKeys { get; private set; } = [];
    public SettingsException? Error { get; private set; }
    public int? UpgradedFromVersion { get; private set; }
    private readonly Dictionary<string, object> values = [];
    private string source = "";
    private Encoding encoding = Encoding.UTF8;
    private YamlMappingNode? root;

    public static string FilePath(string dataDirectory) => Path.Combine(dataDirectory, "aether.yml");

    /// <summary>Where shared videos live; the mapping holds bare file names inside it.</summary>
    public static string VideoDirectory(string dataDirectory) => Path.Combine(dataDirectory, "videos");

    internal static string VideoFile(string dataDirectory, string fileName) =>
        Path.Combine(VideoDirectory(dataDirectory), fileName);

    /// <summary>How a switch is written in the file, for forms that hold it as a bool.</summary>
    public static string BooleanText(bool value) => value ? "true" : "false";

    /// <summary>How a number list is written in the file, which is also the text forms take for it.</summary>
    public static string NumberListText(IEnumerable<long> numbers) =>
        Definition.Emit(numbers.Select(number => number.ToString(CultureInfo.InvariantCulture)));

    /// <summary>A single-line text list, quoting every item and escaping quotes and backslashes.</summary>
    public static string TextListText(IEnumerable<string> texts) => Definition.Emit(texts, ScalarStyle.DoubleQuoted);

    /// <summary>The same validation used when loading and saving; null means the input is valid.</summary>
    public static string? Validate(string name, string? value) => Validate(Definitions, name, value);

    private static string? Validate(IReadOnlyList<Definition> definitions, string name, string? value) =>
        Find(definitions, name) is { } definition ? definition.Validate(name, value) : "未知设置，不能修改。";

    private static Definition? Find(IReadOnlyList<Definition> definitions, string name) =>
        definitions.FirstOrDefault(definition => definition.Resolve(name) is not null);

    public static string? ValidateVideoFileName(string name) =>
        name is "." or ".." || name.IndexOfAny(['/', '\\', ':', '\0']) >= 0
            || name.Any(char.IsControl) || !new[] { ".mp4", ".webm" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
            ? "必须是纯文件名，扩展名为 .mp4 或 .webm，不能带路径。" : null;

    /// <summary>Whether a welcome text has an unescaped {@} anywhere; where it may go is left to validation.</summary>
    public static bool HasWelcomeMention(string text) => Core.WelcomeText.HasMention(text);

    internal Setting<T> Get<T>(Definition<T> definition) =>
        values.TryGetValue(definition.Name, out var setting) ? (Setting<T>)setting : definition.Initial;
}
