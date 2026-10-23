using System.Globalization;
using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class PoolPrizeRow(PoolPrize prize, bool isNew = false) : EditableRow(isNew)
{
    private string name = prize.Name;
    private string weight = prize.Weight.ToString(CultureInfo.InvariantCulture);
    private Grade grade = prize.Grade;
    public string Name { get => name; set => SetInput(ref name, value ?? ""); }
    public string Weight { get => weight; set => SetInput(ref weight, value ?? ""); }
    public Grade Grade { get => grade; set => SetInput(ref grade, value); }
    public PoolPrize Value => new(Name, int.TryParse(Weight, NumberStyles.None,
        CultureInfo.InvariantCulture, out var parsed) ? parsed : 0, Grade);
    [ObservableProperty] private bool nameInvalid;
    [ObservableProperty] private bool weightInvalid;
    [ObservableProperty] private string percentage = "—";
}
