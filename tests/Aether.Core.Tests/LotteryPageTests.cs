using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class LotteryPageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task State_push_includes_each_draw_visibility(bool hidden)
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var watch = await h.WatchConnectedAsync();
        h.Client.Lottery.Start([new("贴纸", 1, Grade.Third)], "停");
        h.Client.Lottery.Stop();
        if (hidden) h.Client.Lottery.HideAllDrawsFromStream();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
        using var response = await http.GetAsync("lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(h.Stop.Token));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string? line;
        do { line = await reader.ReadLineAsync(timeout.Token); } while (line is not null && !line.StartsWith("data: "));
        Assert.NotNull(line);
        using var json = JsonDocument.Parse(line[6..]);
        Assert.Equal(hidden, json.RootElement.GetProperty("lottery").GetProperty("draws")[0].GetProperty("hiddenFromStream").GetBoolean());
    }

    [Fact]
    public void Script_grade_names_and_cell_count_match_the_controller()
    {
        using var resource = typeof(LotteryPage).Assembly.GetManifestResourceStream("Aether.Core.Pages.lottery.page.js")!;
        using var reader = new StreamReader(resource);
        var script = reader.ReadToEnd();
        var grades = Regex.Match(script, @"const grades = \{([^}]+)\};").Groups[1].Value;
        var names = Regex.Matches(grades, @"(\w+):'([^']+)'");
        Assert.Equal(Enum.GetNames<Grade>(), names.Select(match => match.Groups[1].Value));
        Assert.Equal(LotteryController.GradeNames, names.Select(match => match.Groups[2].Value));
        var cells = Regex.Match(script, @"const cellCount = (\d+);").Groups[1].Value;
        Assert.Equal(LotteryController.DrawCells, int.Parse(cells, CultureInfo.InvariantCulture));
    }
}
