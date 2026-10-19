using System.Globalization;
using System.Text.RegularExpressions;

namespace Aether.Core.Tests;

public sealed class LotteryPageTests
{
    [Fact]
    public void Script_grade_names_and_cell_count_match_the_controller()
    {
        using var resource = typeof(LotteryPage).Assembly.GetManifestResourceStream("Aether.Core.LotteryPage.js")!;
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
