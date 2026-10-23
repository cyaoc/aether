namespace Aether.Gui;

public sealed record LotteryHistoryRow(string Id, string Date, bool Active, string Status, string Mode,
    string Count, string Summary, string AccessibleName);
