using Aether.Core;
using Avalonia.Media;

namespace Aether.Gui;

/// <summary>What one page's status bar and its entry in the general settings' page list say: the web service's state in that page's words.</summary>
public sealed record WebPageStatus(WebServiceStatus Service, int Connections, WebPageTexts Texts)
{
    /// <summary>What the streamer gives the browser source; only the page's detail in the general settings shows it.</summary>
    public string Address { get; init; } = "";
    public bool Connected => Service.Running && Connections > 0;
    public bool Waiting => Service.Running && Connections == 0;
    public bool Failed => Service.Error is not null;
    public bool Off => Service is { Running: false, Error: null };
    public string Text => Service.Error is { } error ? $"网页服务没打开：{error}，{Texts.OffEffect}"
        : !Service.Running ? $"网页服务没打开，{Texts.OffEffect}"
        : Connected ? $"{Texts.Name}：已有 {Connections} 个推流软件连着"
        : Texts.WaitingText;
    public string? SetupSteps => Waiting ? Texts.SetupSteps : null;
    public string ConnectionsText => !Service.Running ? "服务没打开" : Connections > 0 ? $"{Connections} 个推流软件连着" : "还没有连着";
}

/// <param name="IconKey">The key of the page's icon in Theme.axaml.</param>
/// <param name="Purpose">What the page shows, under its name in the general settings.</param>
/// <param name="Size">The browser source size to give the page.</param>
/// <param name="OffEffect">What the streamer cannot do while the service is off.</param>
/// <param name="SetupSteps">Shown under the waiting text when the page needs more than its one line.</param>
public sealed record WebPageTexts(string Name, string IconKey, string Purpose, string Size, string WaitingText, string OffEffect, string? SetupSteps = null)
{
    public static readonly WebPageTexts Video = new("视频页", "Icon.Animation", "欢迎动画、进场动画和送礼动画在这里播放", "1920 × 1080",
        "视频页还没有推流软件连着，先在直播姬里加好再试播", "试播和进场都播不了");
    public static readonly WebPageTexts Lottery = new("抽奖页", "Icon.Lottery", "报名、开奖和中奖名单", "横屏 1920 × 1080 · 竖屏 1080 × 1920",
        "抽奖页：还没有推流软件连着", "抽奖页显示不了");
    public static readonly WebPageTexts GiftBoard = new("礼物榜页", "Icon.Trophy", "送礼观众的排行", "480 × 1000，可以等比放大",
        "礼物榜页：还没有推流软件连着", "礼物榜页显示不了", "在直播姬里添加浏览器素材，地址和宽高照通用设置里的“礼物榜页”填；已经加过的，右键素材 → 刷新。");
    public static readonly WebPageTexts DesktopPet = new("桌宠页", "Icon.Pet", "桌宠在直播画面一角表演", "480 × 640",
        "桌宠页：还没有推流软件连着", "桌宠页显示不了", "在直播姬里添加浏览器素材，地址和宽高照通用设置里的“桌宠页”填；已经加过的，右键素材 → 刷新。");

    public Geometry Icon => Icons.Find(IconKey);
}
