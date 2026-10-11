using System.Text.Json;
using Aether.Core.Entries;
using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.BlindGiftV2Fixture;

namespace Aether.Core.Tests;

public sealed class EntryDecodingTests
{
    [Theory]
    [InlineData(7734200, 47L)]
    [InlineData(999, null)]
    public void Only_the_current_rooms_fan_club_counts_and_wealth_is_decoded(long medalRoom, long? expectedLevel)
    {
        using var message = JsonDocument.Parse(EntryV2Fixture.Message(uid: 9876543210, guard: 2, medalRoom: medalRoom));
        var entry = Assert.IsType<Entry>(EntryMessages.Decode("INTERACT_WORD_V2", message.RootElement, 7734200));
        Assert.Equal(9876543210, entry.Uid);
        Assert.Equal("测试观众", entry.Nickname);
        Assert.Equal(1, entry.MessageType);
        Assert.Equal(2, entry.Guard); // The medal's guard field is 1; it belongs to another identity.
        Assert.Equal(expectedLevel, entry.FanClubLevel); // No lit flag: an unlit medal still counts.
        Assert.Equal(45, entry.WealthLevel);
    }

    [Fact]
    public void Omitted_proto3_fields_are_zero_or_empty()
    {
        using var message = JsonDocument.Parse(EntryV2Fixture.Wrap([.. Integer(1, 10001), .. Integer(5, 1)]));
        var entry = Assert.IsType<Entry>(EntryMessages.Decode("INTERACT_WORD_V2", message.RootElement, 7734200));
        Assert.Equal("", entry.Nickname);
        Assert.Equal(0, entry.Guard);
        Assert.Null(entry.FanClubLevel);
        Assert.Equal(0, entry.WealthLevel);
        Assert.Equal("欢迎测试观众", new WelcomeText("欢迎{大航海}{昵称}").Render("测试观众", entry.Guard, 20));
        using var empty = JsonDocument.Parse(EntryV2Fixture.Wrap([]));
        Assert.Null(EntryMessages.Decode("INTERACT_WORD_V2", empty.RootElement, 7734200));
    }
}
