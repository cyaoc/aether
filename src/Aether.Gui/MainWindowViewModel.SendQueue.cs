using System.Collections.ObjectModel;
using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class MainWindowViewModel
{
    // ponytail: each page keeps 100 finished items; add history storage only if older records are needed.
    private const int FinishedSendLimit = 100;
    private readonly Dictionary<long, SendQueueItemViewModel> sendQueueItems = [];
    public ObservableCollection<SendQueueItemViewModel> PendingSends { get; } = [];
    public ObservableCollection<SendQueueItemViewModel> RecentSends { get; } = [];
    public ObservableCollection<SendQueueItemViewModel> RecentWelcomes { get; } = [];
    public ObservableCollection<SendQueueItemViewModel> RecentBlindBoxes { get; } = [];
    [ObservableProperty] private long welcomeCount;

    private void ApplySendQueue(SendQueueUpdate update)
    {
        if (!sendQueueItems.TryGetValue(update.Id, out var item))
        {
            item = new(update);
            sendQueueItems.Add(item.Id, item);
            if (item.Kind == SendQueueItemKind.Welcome)
            {
                RecentWelcomes.Insert(0, item);
                WelcomeCount++;
            }
            else RecentBlindBoxes.Insert(0, item);
            if (!item.IsFinished) PendingSends.Add(item);
        }
        else item.Apply(update);

        if (item.IsFinished)
        {
            PendingSends.Remove(item);
            if (!RecentSends.Contains(item)) RecentSends.Insert(0, item);
            TrimFinished(RecentSends);
            TrimFinished(item.Kind == SendQueueItemKind.Welcome ? RecentWelcomes : RecentBlindBoxes);
        }
        for (var index = 0; index < PendingSends.Count; index++) PendingSends[index].Position = index + 1;
    }

    private void EndPendingSends()
    {
        foreach (var item in PendingSends)
        {
            item.EndConnection();
            RecentSends.Insert(0, item);
        }
        PendingSends.Clear();
        TrimFinished(RecentSends);
        TrimFinished(RecentWelcomes);
        TrimFinished(RecentBlindBoxes);
    }

    private void ClearSendHistory()
    {
        sendQueueItems.Clear();
        PendingSends.Clear();
        RecentSends.Clear();
        RecentWelcomes.Clear();
        RecentBlindBoxes.Clear();
        WelcomeCount = 0;
    }

    private void TrimFinished(ObservableCollection<SendQueueItemViewModel> items)
    {
        var finished = 0;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!item.IsFinished || ++finished <= FinishedSendLimit) continue;
            items.RemoveAt(index--);
            if (!RecentSends.Contains(item) && !RecentWelcomes.Contains(item) && !RecentBlindBoxes.Contains(item))
                sendQueueItems.Remove(item.Id);
        }
    }
}
