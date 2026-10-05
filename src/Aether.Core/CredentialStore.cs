using System.Globalization;
using System.Text.Json;

namespace Aether.Core;

internal sealed record Credential(IReadOnlyDictionary<string, string> Cookies, string RefreshToken, DateTimeOffset SavedAt);

internal sealed class CredentialStore(Database database)
{
    public void Save(Credential credential)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        // ponytail: 登录凭据暂存明文；要分发给别人用时，改用系统钥匙串。
        command.CommandText = """
            INSERT INTO credential (id, cookies, refresh_token, saved_at) VALUES (1, $cookies, $token, $savedAt)
            ON CONFLICT (id) DO UPDATE SET
                cookies = excluded.cookies, refresh_token = excluded.refresh_token, saved_at = excluded.saved_at;
            """;
        command.Parameters.AddWithValue("$cookies", JsonSerializer.Serialize(credential.Cookies));
        command.Parameters.AddWithValue("$token", credential.RefreshToken);
        command.Parameters.AddWithValue("$savedAt", credential.SavedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    public Credential? Load()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT cookies, refresh_token, saved_at FROM credential";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new Credential(JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(0))!,
            reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture));
    }

    public void Delete()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM credential";
        command.ExecuteNonQuery();
    }
}
