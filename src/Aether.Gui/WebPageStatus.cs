using Aether.Core;

namespace Aether.Gui;

/// <summary>What one page's status bar says: the web service's state in that page's words.</summary>
public sealed record WebPageStatus(WebServiceStatus Service, int Connections, WebPageTexts Texts)
{
    public bool Connected => Service.Running && Connections > 0;
    public bool Waiting => Service.Running && Connections == 0;
    public bool Failed => Service.Error is not null;
    public bool Off => Service is { Running: false, Error: null };
    public string Text => Service.Error is { } error ? $"网页服务没打开：{error}，{Texts.OffEffect}"
        : !Service.Running ? $"网页服务没打开，{Texts.OffEffect}"
        : Connected ? $"{Texts.Name}：已有 {Connections} 个推流软件连着"
        : Texts.WaitingText;
    public string? SetupSteps => Waiting ? Texts.SetupSteps : null;
}

/// <param name="OffEffect">What the streamer cannot do while the service is off.</param>
/// <param name="SetupSteps">Shown under the waiting text when the page needs more than its one line.</param>
public sealed record WebPageTexts(string Name, string WaitingText, string OffEffect, string? SetupSteps = null)
{
    public static readonly WebPageTexts Video = new("视频页", "视频页还没有推流软件连着，先在直播姬里加好再试播", "试播和进场都播不了");
}
