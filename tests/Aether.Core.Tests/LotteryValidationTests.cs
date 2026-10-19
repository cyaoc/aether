namespace Aether.Core.Tests;

public sealed class LotteryValidationTests
{
    [Theory]
    [InlineData(null, "口令不能为空")]
    [InlineData("", "口令不能为空")]
    [InlineData(" \t\r\n", "口令不能为空")]
    [InlineData("停", null)]
    [InlineData(" 停 ", null)]
    public void Stop_word_rejects_only_blank_text(string? text, string? expected) =>
        Assert.Equal(expected, LotteryController.ValidateStopWord(text));

    [Theory]
    [InlineData(null, "报名关键字不能为空")]
    [InlineData("", "报名关键字不能为空")]
    [InlineData(" \t\r\n", "报名关键字不能为空")]
    [InlineData("抽奖", null)]
    [InlineData(" 抽奖 ", null)]
    public void Entry_keyword_rejects_only_blank_text(string? text, string? expected) =>
        Assert.Equal(expected, LotteryController.ValidateKeyword(text));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \t\r\n", null)]
    [InlineData("123", null)]
    [InlineData(" 123 ", null)]
    [InlineData("9223372036854775807", null)]
    [InlineData("+123", "UID 必须是正整数或留空")]
    [InlineData("0", "UID 必须是正整数或留空")]
    [InlineData("-1", "UID 必须是正整数或留空")]
    [InlineData("abc", "UID 必须是正整数或留空")]
    [InlineData("9223372036854775808", "UID 必须是正整数或留空")]
    public void Uid_accepts_blank_text_or_a_positive_int64(string? text, string? expected) =>
        Assert.Equal(expected, LotteryController.ValidateUid(text));
}
