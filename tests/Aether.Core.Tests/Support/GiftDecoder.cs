using System.Text.Json;
using Aether.Core.Gifts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Core.Tests.Support;

/// <summary>Runs one gift message through <see cref="GiftMessages"/> the way the room connection does.</summary>
internal static class GiftDecoder
{
    public static Gift[] Decode(string message, ILogger? logger = null)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        var command = root.GetProperty("cmd").GetString();
        Assert.NotNull(command);
        return GiftMessages.Decode(command, root, logger ?? NullLogger.Instance);
    }
}
