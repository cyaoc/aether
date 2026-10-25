using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests.Support;

/// <summary>Answers the online rank request and drives an <see cref="OnlineRankPicker"/> the way a click and Enter do.</summary>
internal static class OnlineRankList
{
    public static readonly (long Uid, string Name)[] TwoViewers = [(42, "柚子"), (99, "小鹿")];

    /// <summary>The top 100 the design package shows: a long UID second and a long nickname sixth.</summary>
    public static (long Uid, string Name)[] Top100(long first = 12345) => Enumerable.Range(1, 100).Select(index => (
        index == 1 ? first : index == 2 ? 3494000012300417L : index,
        index == 1 ? "柚子不酸" : index == 2 ? "一只小鹿" : index == 6 ? "今天也要努力学习不然就要挂科了超长昵称" : $"观众 {index}")).ToArray();

    public static HttpResponseMessage Response(int count, params (long Uid, string Name)[] viewers) => GuiHarness.Json(JsonSerializer.Serialize(
        new { code = 0, data = new { count, item = viewers.Select(viewer => new { uid = viewer.Uid, name = viewer.Name }) } }));

    public static Func<Uri, CancellationToken, Task<HttpResponseMessage>> Answer(int count, params (long Uid, string Name)[] viewers) =>
        (_, _) => Task.FromResult(Response(count, viewers));

    public static Button Button(OnlineRankPicker picker) => picker.FindControl<Button>("PickerButton")!;

    public static ListBox List(OnlineRankPicker picker) => picker.FindControl<ListBox>("RankList")!;

    /// <summary>Opens the picker and waits for <paramref name="rows"/> rows.</summary>
    public static async Task<ListBox> OpenAsync(OnlineRankPicker picker, int rows)
    {
        Button(picker).Flyout!.ShowAt(Button(picker));
        await GuiHarness.WaitForAsync(() => List(picker).ItemCount == rows);
        return List(picker);
    }

    /// <summary>Opens the picker, chooses the row at <paramref name="index"/> with Enter, and checks the popup closed.</summary>
    public static async Task ChooseAsync(OnlineRankPicker picker, int index, int rows = 2)
    {
        var list = await OpenAsync(picker, rows);
        list.SelectedIndex = index;
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        GuiHarness.Flush();
        Assert.False(Button(picker).Flyout!.IsOpen);
    }

    /// <summary>What a row shows: rank, nickname and UID.</summary>
    public static string[] RowText(ListBox list, int index)
    {
        list.ScrollIntoView(index);
        GuiHarness.Flush();
        return list.ContainerFromIndex(index)!.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? "").ToArray();
    }
}
