using Aether.Core;

namespace Aether.Gui;

/// <summary>What one page's status bar and its row in the page list say: the web service's state in that page's words.</summary>
public sealed record WebPageStatus(WebServiceStatus Service, int Connections, WebPageTexts Texts)
{
    /// <summary>What the streamer gives the browser source; only the page list shows it.</summary>
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

/// <param name="OffEffect">What the streamer cannot do while the service is off.</param>
/// <param name="SetupSteps">Shown under the waiting text when the page needs more than its one line.</param>
public sealed record WebPageTexts(string Name, string WaitingText, string OffEffect, string? SetupSteps = null)
{
    public static readonly WebPageTexts Video = new("视频页", "视频页还没有推流软件连着，先在直播姬里加好再试播", "试播和进场都播不了");
    public static readonly WebPageTexts Lottery = new("抽奖页", "抽奖页：还没有推流软件连着", "抽奖页显示不了");
    public static readonly WebPageTexts GiftBoard = new("礼物榜页", "礼物榜页：还没有推流软件连着", "礼物榜页显示不了",
        "在直播姬里添加浏览器素材，地址和宽高照通用设置里“礼物榜页”那一行填；已经加过的，右键素材 → 刷新。");
    public static readonly WebPageTexts DesktopPet = new("桌宠页", "桌宠页：还没有推流软件连着", "桌宠页显示不了",
        "在直播姬里添加浏览器素材，地址和宽高照通用设置里“桌宠页”那一行填；已经加过的，右键素材 → 刷新。");
}
