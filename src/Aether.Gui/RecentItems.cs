using System.Collections.ObjectModel;

namespace Aether.Gui;

public sealed class RecentItems<T> : ObservableCollection<T>
{
    // shortcut: each page keeps 100 finished items; add history storage only if older records are needed.
    private const int FinishedItemLimit = 100;
    private readonly List<T> finished = [];

    public void Finish(T item)
    {
        finished.Add(item);
        if (finished.Count <= FinishedItemLimit) return;
        Remove(finished[0]);
        finished.RemoveAt(0);
    }

    protected override void ClearItems()
    {
        finished.Clear();
        base.ClearItems();
    }

    protected override void SetItem(int index, T item)
    {
        var finishedIndex = finished.IndexOf(this[index]);
        if (finishedIndex >= 0) finished[finishedIndex] = item;
        base.SetItem(index, item);
    }
}
