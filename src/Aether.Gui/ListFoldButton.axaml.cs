using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Media;

namespace Aether.Gui;

public partial class ListFoldButton : Button
{
    private const int Shown = 3;
    private bool isExpanded;
    /// <summary>The viewer expanded or collapsed the list. Setting <see cref="IsExpanded"/> does not raise it,
    /// since the page refreshes the rows itself after that.</summary>
    public event Action? Toggled;
    protected override Type StyleKeyOverride => typeof(Button);

    public ListFoldButton()
    {
        InitializeComponent();
        Click += (_, _) => Toggle(!IsExpanded);
    }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value) return;
            isExpanded = value;
            ControlAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                value ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded,
                value ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        }
    }

    private void Toggle(bool expanded)
    {
        IsExpanded = expanded;
        Toggled?.Invoke();
    }

    /// <summary>Shows the first 3 rows, or all of them when expanded. Past 3 rows the button appears and says how many
    /// rows are hidden and how many of those show an error.</summary>
    public void ApplyTo<T>(IEnumerable<T> rows, string unit) where T : EditableRow
    {
        var all = rows.ToArray();
        for (var index = 0; index < all.Length; index++) all[index].IsShown = IsExpanded || index < Shown;
        Refresh(all.Length, IsExpanded ? 0 : all.Skip(Shown).Count(row => row.VisibleError is not null), unit);
    }

    private void Refresh(int count, int hiddenErrors, string unit)
    {
        IsVisible = count > Shown;
        Label.Text = IsExpanded ? $"收起，只看前 {Shown} {unit}"
            : $"还有 {count - Shown} {unit}" + (hiddenErrors > 0 ? $"，其中 {hiddenErrors} {unit}有错" : "");
        AutomationProperties.SetName(this, Label.Text);
        Classes.Set("danger", hiddenErrors > 0);
        Arrow.RenderTransform = new RotateTransform(IsExpanded ? 180 : 0);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ListFoldButtonPeer(this);

    private sealed class ListFoldButtonPeer(ListFoldButton owner) : ButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => owner.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public bool ShowsMenu => false;
        public void Expand() => owner.Toggle(true);
        public void Collapse() => owner.Toggle(false);
    }
}
