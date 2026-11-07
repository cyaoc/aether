using System.Globalization;
using System.Text.RegularExpressions;
using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.ServerSentEvents;

namespace Aether.Core.Tests;

public sealed class LotteryPageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task State_push_includes_each_draw_visibility(bool hidden)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var watch = await h.WatchConnectedAsync();
        await h.Client.Lottery.StartAsync([new("贴纸", 1, Grade.Third)], "停");
        await h.Client.Lottery.StopAsync();
        if (hidden) await h.Client.Lottery.HideAllDrawsFromStreamAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
        using var response = await http.GetAsync("lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(h.Stop.Token));
        var (_, json) = await Event(reader);
        Assert.Equal(hidden, json.GetProperty("lottery").GetProperty("draws")[0].GetProperty("hiddenFromStream").GetBoolean());
    }

    [Fact]
    public void Script_grade_names_and_cell_count_match_the_controller()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Pages", "lottery", "page.js"));
        var grades = Regex.Match(script, @"const grades = \{([^}]+)\};").Groups[1].Value;
        var names = Regex.Matches(grades, @"(\w+):'([^']+)'");
        Assert.Equal(Enum.GetNames<Grade>(), names.Select(match => match.Groups[1].Value));
        Assert.Equal(LotteryController.GradeNames, names.Select(match => match.Groups[2].Value));
        var cells = Regex.Match(script, @"const cellCount = (\d+);").Groups[1].Value;
        Assert.Equal(LotteryController.DrawCells, int.Parse(cells, CultureInfo.InvariantCulture));
    }
}
