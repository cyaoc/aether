namespace Aether.Gui;

/// <summary>The eye button's texts on 开奖记录 and 中奖名单 rows.</summary>
internal static class DrawVisibility
{
    public static string Name(string draw) => $"不挂出：{draw}";
    public static string Hint(bool hidden) => hidden ? "重新挂到直播画面" : "不挂到直播画面";
}
