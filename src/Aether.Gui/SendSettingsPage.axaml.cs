using Aether.Core;
using Avalonia;

namespace Aether.Gui;

public partial class SendSettingsPage : SettingsPage
{
    private double firstPendingRowsHeight;

    public SendSettingsPage() : this(DataDirectory.Locate()) { }

    public SendSettingsPage(string dataDirectory) : base(dataDirectory)
    {
        InitializeComponent();
        Title = "发送队列";
        Bind(SendIntervalInput, SendIntervalReadOnly, SendIntervalError, s => s.SendIntervalSeconds);
        // Row heights are only known after layout, so the cap follows every layout pass.
        SendActivity.LayoutUpdated += (_, _) => ArrangePendingActivity();
        LoadSettings();
    }

    private void ArrangePendingActivity()
    {
        // The record being sent and the next one stay whole; past them the queue takes at most half, and history keeps its heading.
        // With more rows below, the second row's separator ends the area rather than a sliver of the third.
        // A scrolled-away second row keeps the last measured height.
        var count = PendingSendsList.ItemCount;
        if (count > 0
            && PendingSendsList.ContainerFromIndex(Math.Min(1, count - 1)) is { } row
            && PendingSendsList.TranslatePoint(default, PendingActivity) is { } list)
            firstPendingRowsHeight = list.Y + PendingSendsList.Padding.Top + row.Bounds.Bottom + (count > 2 ? 0 : PendingSendsList.Padding.Bottom);
        var room = SendActivity.Bounds.Height - RecentSendsActivity.Margin.Top - RecentSendsHeader.DesiredSize.Height;
        var height = Math.Max(0, Math.Min(room, Math.Max(SendActivity.Bounds.Height / 2, firstPendingRowsHeight)));
        if (Math.Abs(PendingActivity.MaxHeight - height) > 0.5) PendingActivity.MaxHeight = height;
    }
}
