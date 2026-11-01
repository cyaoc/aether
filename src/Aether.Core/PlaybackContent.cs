namespace Aether.Core;

/// <param name="Summary">What logs and the recent-playback list call it.</param>
public abstract record PlaybackContent(string Summary)
{
    public sealed record Video(string FileName) : PlaybackContent(FileName);
    /// <param name="Identifier">Core never reads it; the page looks it up in its registry.</param>
    public sealed record Animation(string Identifier, string Nickname, string Summary) : PlaybackContent(Summary);

    public static Animation General(EntryTier tier, string nickname) => new($"general-{tier}", nickname, $"{tier} 档");
}
