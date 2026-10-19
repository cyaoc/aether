using Microsoft.Data.Sqlite;

namespace Aether.Core;

internal static class SqliteExtensions
{
    public static void Set(this SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value switch
        {
            null => DBNull.Value,
            DateTimeOffset time => time.ToString("O"),
            _ => value,
        });

    public static T? GetNullable<T>(this SqliteDataReader reader, int ordinal) where T : struct =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);
}
