using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>The account region's facade (ADR 0017): the saved credential is read and written only in its steps, while the requests
/// that use it, such as reading the account or refreshing the credential, run outside.</summary>
internal sealed class AccountFacade
{
    private readonly Region<Func<CredentialStore>> region;

    /// <param name="store">Opens the database on first use, so the first step can throw like any other store call.</param>
    public AccountFacade(Func<CredentialStore> store, PendingSteps steps, ILogger logger) => region = new(store, steps, logger);

    public Task<Credential?> LoadAsync() => region.Run(store => store().Load());

    /// <summary>With <paramref name="previousToken"/>, only over that credential: true when it was still the saved one.</summary>
    public Task<bool> SaveAsync(Credential credential, string? previousToken = null) =>
        region.Run(store => store().Save(credential, previousToken));

    public Task MarkCheckedAsync(string token, DateTimeOffset checkedAt) => region.Run(store => store().MarkChecked(token, checkedAt));

    /// <summary>With <paramref name="token"/>, only that credential: true when it was still the saved one.</summary>
    public Task<bool> DeleteAsync(string? token = null) => region.Run(store => store().Delete(token));

    public void Stop() => region.Stop();
}
