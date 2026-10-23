namespace Aether.Gui;

public sealed record WinnerGroupRow(string Title, string Prize, string Progress, IReadOnlyList<WinnerRow> Winners)
{
    public bool IsEmpty => Winners.Count == 0;
}

public sealed record WinnerRow(string Name, string? Hint = null, bool Fresh = false)
{
    public string? ToolTip => Hint is null ? null : $"{Name} · {Hint}";
}
