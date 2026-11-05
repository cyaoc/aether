using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>One room connection's writes to the 收礼记录 (ADR 0013): each paid gift once, flagged by whether the 盲盒统计 and
/// the 礼物榜 want it, and not at all when neither does. The 盲盒统计 only wants 盲盒, and only when it is on for this room
/// connection (<paramref name="blindBoxes"/>; its settings apply from the next one); the board wants every gift while its 期 is
/// running.</summary>
internal sealed class GiftRecording(GiftStore store, GiftBoardController board, ILogger logger, long roomId, bool blindBoxes)
{
    /// <summary>Writes one message's gifts. <paramref name="writeAll"/> runs the writes, gift by gift, through the function it is
    /// given, which returns whether that gift was newly recorded as a 盲盒 for the 盲盒统计; without it they run in order.</summary>
    public void Record(IEnumerable<Gift> gifts, Action<Func<Gift, bool>>? writeAll = null)
    {
        var forBoard = board.Wants(roomId);
        List<(Gift, long)> onBoard = [];
        bool Write(Gift gift)
        {
            var forBlindBox = blindBoxes && gift.Blind is not null;
            if (!forBlindBox && !forBoard) return false;
            try
            {
                if (store.Save(roomId, gift, forBlindBox, forBoard) is not { } id) return false;
                if (forBoard) onBoard.Add((gift, id));
                return forBlindBox;
            }
            // A 盲盒 the 盲盒统计 wanted is never lost quietly: its failure ends the room connection, as it always has.
            catch (Exception error) when (!forBlindBox && IsDatabaseFailure(error))
            {
                logger.LogError(error, "礼物榜读写数据库失败，弹幕照常接收：{Error}", error.Message);
                return false;
            }
        }
        if (writeAll is null)
            foreach (var gift in gifts) Write(gift);
        else writeAll(Write);
        if (onBoard.Count > 0) board.ShowGifts(roomId, onBoard);
    }

    internal static bool IsDatabaseFailure(Exception error) => error is SqliteException or IOException;
}
