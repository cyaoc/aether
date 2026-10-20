using Aether.Core;

namespace Aether.Gui;

internal static class GradeStyle
{
    public static string Class(Grade grade) => grade switch
    {
        Grade.Grand => "grand",
        Grade.Second => "second",
        Grade.Third => "third",
        _ => throw new ArgumentOutOfRangeException(nameof(grade)),
    };
}
