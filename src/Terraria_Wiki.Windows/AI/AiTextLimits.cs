using System.Text;

namespace Terraria_Wiki.Windows.AI;

internal static class AiTextLimits
{
    internal const int MaximumCharacters = 32768;
    internal const int MaximumToolAttempts = 15;
    internal static int Count(string value) => value.EnumerateRunes().Count();
    internal static bool IsTooLong(string value) => Count(value) > MaximumCharacters;
}
