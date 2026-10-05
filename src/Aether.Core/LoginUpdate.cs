namespace Aether.Core;

public abstract record LoginUpdate;

public sealed record LoginQrCode(string Content) : LoginUpdate;

public sealed record LoggedIn : LoginUpdate;
