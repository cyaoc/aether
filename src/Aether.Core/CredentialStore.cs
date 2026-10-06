using System.Globalization;
using System.Text.Json;

namespace Aether.Core;

internal sealed record Credential(IReadOnlyDictionary<string, string> Cookies, string RefreshToken,
    DateTimeOffset SavedAt, DateTimeOffset? CheckedAt);

internal sealed class CredentialStore(Database database)
{
    public bool Save(Credential credential, string? previousToken = null)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        // ponytail: 登录凭据暂存明文；要分发给别人用时，改用系统钥匙串。
        command.CommandText = previousToken is null ? """
            INSERT INTO credential (id, cookies, refresh_token, saved_at, checked_at)
            VALUES (1, $cookies, $token, $savedAt, $savedAt)
            ON CONFLICT (id) DO UPDATE SET
                cookies = excluded.cookies, refresh_token = excluded.refresh_token,
                saved_at = excluded.saved_at, checked_at = excluded.checked_at;
            """ : """
            UPDATE credential SET cookies = $cookies, refresh_token = $token, saved_at = $savedAt, checked_at = $savedAt
            WHERE refresh_token = $previousToken;
            """;
        if (previousToken is not null) command.Parameters.AddWithValue("$previousToken", previousToken);
        command.Parameters.AddWithValue("$cookies", JsonSerializer.Serialize(credential.Cookies));
        command.Parameters.AddWithValue("$token", credential.RefreshToken);
        command.Parameters.AddWithValue("$savedAt", credential.SavedAt.ToString("O"));
        return command.ExecuteNonQuery() != 0;
    }

    public Credential? Load()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT cookies, refresh_token, saved_at, checked_at FROM credential";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new Credential(JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(0))!,
            reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
            reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture));
    }

    public void MarkChecked(string token, DateTimeOffset checkedAt)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE credential SET checked_at = $checkedAt WHERE refresh_token = $token";
        command.Parameters.AddWithValue("$checkedAt", checkedAt.ToString("O"));
        command.Parameters.AddWithValue("$token", token);
        command.ExecuteNonQuery();
    }

    public bool Delete(string? token = null)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = token is null ? "DELETE FROM credential" : "DELETE FROM credential WHERE refresh_token = $token";
        if (token is not null) command.Parameters.AddWithValue("$token", token);
        return command.ExecuteNonQuery() != 0;
    }
}
