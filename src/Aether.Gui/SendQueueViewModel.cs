using System.Collections.ObjectModel;
using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class SendQueueViewModel : ObservableObject
{
    public ObservableCollection<SendQueueItemViewModel> PendingSends { get; } = [];
    public RecentItems<SendQueueItemViewModel> RecentSends { get; } = [];
    public RecentItems<SendQueueItemViewModel> RecentWelcomes { get; } = [];
    public RecentItems<SendQueueItemViewModel> RecentQueriesAndAnnouncements { get; } = [];
    [ObservableProperty] private long welcomeCount;

    internal void Apply(SendQueueUpdate update)
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

    internal void EndPending()
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
        RecentSends.Finish(item);
        var page = item.Kind == SendQueueItemKind.Welcome ? RecentWelcomes : RecentQueriesAndAnnouncements;
        page.Finish(item);
    }

    internal void Clear()
    {
        PendingSends.Clear();
        RecentSends.Clear();
        RecentWelcomes.Clear();
        RecentQueriesAndAnnouncements.Clear();
        WelcomeCount = 0;
    }
}
