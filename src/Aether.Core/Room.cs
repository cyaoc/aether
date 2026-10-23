namespace Aether.Core;

public static class Room
{
    public const string InvalidNumber = "请输入有效的房间号（正整数）。";

    public static bool TryParseNumber(string? text, out long number) => Ids.TryParsePositive(text, out number);
}
