namespace Aether.Gui;

/// <summary>A date as the GUI writes it (canvases O4 and GS1): 月日, with the year only when it is not this year's, so older ones stay unambiguous.</summary>
internal static class LocalDateText
{
    public static string Date(DateTimeOffset timestamp, DateTimeOffset now) =>
        timestamp.ToLocalTime() is var at && at.Year == now.Year ? $"{at:M月d日}" : $"{at:yyyy年M月d日}";

    public static string DateAndTime(DateTimeOffset timestamp, DateTimeOffset now) => $"{Date(timestamp, now)} {timestamp.ToLocalTime():HH:mm}";
}
