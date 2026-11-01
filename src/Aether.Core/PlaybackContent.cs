namespace Aether.Core;

public abstract record PlaybackContent
{
    public sealed record Video(string FileName) : PlaybackContent;
    public sealed record Animation(string Identifier, string Nickname) : PlaybackContent;
    public string Summary => this switch { Video video => video.FileName, Animation animation => animation.Identifier, _ => "" };
}

public static class AnimationId
{
    public static string General(EntryTier tier) => $"general-{tier}";
}
