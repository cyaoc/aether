using System.Globalization;

namespace Aether.Core.Tests;

public sealed class RoomTests
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
    [InlineData("-5", false, 0L)]
    [InlineData("abc", false, 0L)]
    [InlineData("9223372036854775808", false, 0L)]
    [InlineData("１２３", false, 0L)]
    public void Number_accepts_only_positive_int64_digits_with_optional_surrounding_whitespace(string? text, bool valid, long expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.PositiveSign = "p";
            CultureInfo.CurrentCulture = culture;
            Assert.Equal(valid, Room.TryParseNumber(text, out var number));
            if (valid) Assert.Equal(expected, number);
            Assert.False(Room.TryParseNumber("p123", out _));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
