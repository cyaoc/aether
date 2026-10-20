using Aether.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Aether.Gui;

public sealed class GradeBadge : Border
{
    public static readonly StyledProperty<Grade> GradeProperty =
        AvaloniaProperty.Register<GradeBadge, Grade>(nameof(Grade));
    private readonly TextBlock text = new() { VerticalAlignment = VerticalAlignment.Center };
    protected override Type StyleKeyOverride => typeof(Border);

    public GradeBadge()
    {
        Child = text;
        Classes.AddRange(["badge", GradeStyle.Class(Grade)]);
        text.Text = LotteryController.GradeNames[(int)Grade];
    }

    public Grade Grade
    {
        get => GetValue(GradeProperty);
        set => SetValue(GradeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != GradeProperty) return;
        Classes.Remove(GradeStyle.Class(change.GetOldValue<Grade>()));
        Classes.Add(GradeStyle.Class(Grade));
        text.Text = LotteryController.GradeNames[(int)Grade];
    }
}
