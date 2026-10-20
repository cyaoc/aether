using System.Collections.ObjectModel;

namespace Aether.Gui;

public sealed class EditableRows<T>(int minimumCount) : ObservableCollection<T> where T : EditableRow
{
    public event Action? RowChanged;

    public void AddNew(T row)
    {
        Add(row);
        row.RequestFocus();
    }

    protected override void InsertItem(int index, T item)
    {
        item.RemoveRow = () => Remove(item);
        item.Changed += OnRowChanged;
        base.InsertItem(index, item);
        RefreshRows();
    }

    protected override void RemoveItem(int index)
    {
        Detach(this[index]);
        base.RemoveItem(index);
        RefreshRows();
    }

    protected override void ClearItems()
    {
        foreach (var row in this) Detach(row);
        base.ClearItems();
        RefreshRows();
    }

    protected override void SetItem(int index, T item)
    {
        Detach(this[index]);
        item.RemoveRow = () => Remove(item);
        item.Changed += OnRowChanged;
        base.SetItem(index, item);
        RefreshRows();
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        base.MoveItem(oldIndex, newIndex);
        RefreshRows();
    }

    private void Detach(T row)
    {
        row.Changed -= OnRowChanged;
        row.RemoveRow = null;
    }

    private void RefreshRows()
    {
        for (var index = 0; index < Count; index++)
        {
            this[index].Number = index + 1;
            this[index].CanRemove = Count > minimumCount;
        }
        OnRowChanged();
    }

    private void OnRowChanged() => RowChanged?.Invoke();
}
