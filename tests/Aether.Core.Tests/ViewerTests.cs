namespace Aether.Core.Tests;

public sealed class ViewerTests
{
    [Theory]
    [InlineData(" 123 ", true, 123L)]
    [InlineData("00123", true, 123L)]
    [InlineData("9223372036854775807", true, long.MaxValue)]
    [InlineData(null, false, 0L)]
    [InlineData("", false, 0L)]
    [InlineData(" ", false, 0L)]
    [InlineData("+123", false, 0L)]
    [InlineData("0", false, 0L)]
    [InlineData("-1", false, 0L)]
    [InlineData("abc", false, 0L)]
    [InlineData("9223372036854775808", false, 0L)]
    public void Uid_accepts_only_positive_int64_digits_with_optional_surrounding_whitespace(string? text, bool valid, long expected)
    {
        Assert.Equal(valid, Viewer.TryParseUid(text, out var uid));
        if (valid) Assert.Equal(expected, uid);
    }
}
