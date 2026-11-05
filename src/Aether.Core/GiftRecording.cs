using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>One room connection's gifts for the 收礼记录 (ADR 0013): each paid gift once, marked by whether the 盲盒统计
/// wanted it and the 期 the 礼物榜 counted it in, and not at all when neither did. The 盲盒统计 only wants 盲盒, and only
/// when it is on for this room connection (<paramref name="blindBoxes"/>; its settings apply from the next one); the board
/// takes every gift while its 期 is running, before it is written.</summary>
internal sealed class GiftRecording(GiftStore store, GiftBoardController board, ILogger logger, long roomId, bool blindBoxes)
{
    /// <summary>Records one message's gifts. <paramref name="writeAll"/> runs the writes, gift by gift, through the function it
    /// is given, which returns whether that gift was newly recorded as a 盲盒 for the 盲盒统计.</summary>
    public void Record(IReadOnlyList<Gift> gifts, Action<Func<Gift, bool>> writeAll)
    {
        // On the board first: the database neither delays nor decides what the 礼物榜页 shows.
        var periodOf = board.Take(roomId, gifts);
        bool Write(Gift gift)
        {
            var forBlindBox = blindBoxes && gift.Blind is not null;
            var period = periodOf(gift);
            if (!forBlindBox && period is null) return false;
            try { return store.Save(roomId, gift, forBlindBox, period) && forBlindBox; }
            // A 盲盒 the 盲盒统计 wanted is never lost quietly: its failure ends the room connection, as it always has.
            catch (Exception error) when (!forBlindBox && IsDatabaseFailure(error))
            {
                logger.LogError(error, "礼物榜写数据库失败，榜上照常显示，没存下的礼物重新连接后不算：{Error}", error.Message);
                return false;
            }
        }
        writeAll(Write);
    }

    internal static bool IsDatabaseFailure(Exception error) => error is SqliteException or IOException;
}
