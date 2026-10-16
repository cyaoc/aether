using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Media;

namespace Aether.Gui;

public partial class ListFoldButton : Button
{
    private bool isExpanded;
    public event Action? ExpandedChanged;
    protected override Type StyleKeyOverride => typeof(Button);

    public ListFoldButton()
    {
        InitializeComponent();
        Click += (_, _) => IsExpanded = !IsExpanded;
    }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value) return;
            isExpanded = value;
            ExpandedChanged?.Invoke();
            ControlAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                value ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded,
                value ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        }
    }

    public void Refresh(int count, int hiddenErrors, string unit)
    {
        IsVisible = count > 3;
        var error = !IsExpanded && hiddenErrors > 0;
        Label.Text = IsExpanded ? $"收起，只看前 3 {unit}"
            : $"还有 {count - 3} {unit}" + (error ? $"，其中 {hiddenErrors} {unit}有错" : "");
        AutomationProperties.SetName(this, Label.Text);
        Classes.Set("danger", error);
        Arrow.RenderTransform = new RotateTransform(IsExpanded ? 180 : 0);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ListFoldButtonPeer(this);

    private sealed class ListFoldButtonPeer(ListFoldButton owner) : ButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => owner.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public bool ShowsMenu => false;
        public void Expand() => owner.IsExpanded = true;
        public void Collapse() => owner.IsExpanded = false;
    }
}
