using System.Collections.ObjectModel;
using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class MainWindowViewModel
{
    // ponytail: each page keeps 100 finished items; add history storage only if older records are needed.
    private const int FinishedSendLimit = 100;
    // Each feature page's finished items, earliest-finished first, so trimming drops the oldest by when they finished.
    private readonly Queue<SendQueueItemViewModel> finishedWelcomes = new();
    private readonly Queue<SendQueueItemViewModel> finishedQueriesAndAnnouncements = new();
    public ObservableCollection<SendQueueItemViewModel> PendingSends { get; } = [];
    public ObservableCollection<SendQueueItemViewModel> RecentSends { get; } = [];
    public ObservableCollection<SendQueueItemViewModel> RecentWelcomes { get; } = [];
    public ObservableCollection<SendQueueItemViewModel> RecentQueriesAndAnnouncements { get; } = [];
    [ObservableProperty] private long welcomeCount;

    private void ApplySendQueue(SendQueueUpdate update)
    {
        // Core reports nothing after an item's outcome, so only pending items can be updated.
        var item = PendingSends.FirstOrDefault(pending => pending.Id == update.Id);
        if (item is null)
        {
            item = new(update);
            if (item.Kind == SendQueueItemKind.Welcome)
            {
                RecentWelcomes.Insert(0, item);
                WelcomeCount++;
            }
            else RecentQueriesAndAnnouncements.Insert(0, item);
            if (!item.IsFinished) PendingSends.Add(item);
        }
        else item.Apply(update);

        if (item.IsFinished) Finish(item);
        for (var index = 0; index < PendingSends.Count; index++) PendingSends[index].Position = index + 1;
    }

    private void EndPendingSends()
    {
        foreach (var item in PendingSends.ToArray())
        {
            item.EndConnection();
            Finish(item);
        }
    }

    private void Finish(SendQueueItemViewModel item)
    {
        PendingSends.Remove(item);
        RecentSends.Insert(0, item);
        if (RecentSends.Count > FinishedSendLimit) RecentSends.RemoveAt(FinishedSendLimit);
        var (page, finished) = item.Kind == SendQueueItemKind.Welcome
            ? (RecentWelcomes, finishedWelcomes)
            : (RecentQueriesAndAnnouncements, finishedQueriesAndAnnouncements);
        finished.Enqueue(item);
        if (finished.Count > FinishedSendLimit) page.Remove(finished.Dequeue());
    }

    private void ClearSendHistory()
    {
        PendingSends.Clear();
        RecentSends.Clear();
        RecentWelcomes.Clear();
        RecentQueriesAndAnnouncements.Clear();
        finishedWelcomes.Clear();
        finishedQueriesAndAnnouncements.Clear();
        WelcomeCount = 0;
    }
}
