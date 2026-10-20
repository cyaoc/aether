using Aether.Core;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Aether.Gui;

public sealed class GradePicker : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    public GradePicker()
    {
        var grades = Enum.GetValues<Grade>();
        ItemsSource = grades;
        SelectionMode = SelectionMode.AlwaysSelected;
        Classes.Add("prizeGrade");
        ItemTemplate = new FuncDataTemplate<Grade>((grade, _) =>
            new TextBlock { Text = LotteryController.GradeNames[(int)grade] });
        PreparingContainer += (_, e) =>
        {
            foreach (var grade in grades)
                e.Container.Classes.Set(GradeStyle.Class(grade), Equals(Items[e.Index], grade));
        };
    }
}
