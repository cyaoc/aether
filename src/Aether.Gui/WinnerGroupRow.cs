namespace Aether.Gui;

public sealed record WinnerGroupRow(string Award, string Prize, string Progress, IReadOnlyList<WinnerRow> Winners)
{
    public bool IsEmpty => Winners.Count == 0;
}

public sealed record WinnerRow(string Name, string? Hint = null, bool Fresh = false)
{
    public string ToolTip => Hint is null ? Name : $"{Name} · {Hint}";
}
