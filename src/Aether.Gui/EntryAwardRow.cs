using System.Globalization;
using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class EntryAwardRow(LotteryAward award, bool isNew = false) : EditableRow(isNew)
{
    private string name = award.Name;
    private string prize = award.Prize;
    private string capacity = award.Capacity.ToString(CultureInfo.InvariantCulture);
    private Grade grade = award.Grade;
    public string Name { get => name; set => SetInput(ref name, value ?? ""); }
    public string Prize { get => prize; set => SetInput(ref prize, value ?? ""); }
    public string Capacity { get => capacity; set => SetInput(ref capacity, value ?? ""); }
    public Grade Grade { get => grade; set => SetInput(ref grade, value); }
    public LotteryAward Value => new(Name, Prize, int.TryParse(Capacity, NumberStyles.None,
        CultureInfo.InvariantCulture, out var parsed) ? parsed : 0, Grade);
    [ObservableProperty] private bool preparing;
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private bool nameInvalid;
    [ObservableProperty] private bool prizeInvalid;
    [ObservableProperty] private bool capacityInvalid;
    [ObservableProperty] private bool canDraw;
    [ObservableProperty] private bool full;
    [ObservableProperty] private string drawCount = "";
    [ObservableProperty] private string drawText = "";
    [ObservableProperty] private string drawAccessibleName = "";
    [ObservableProperty] private string? drawHint;

    public new void Remove()
    {
        if (CanEdit) base.Remove();
    }
}
