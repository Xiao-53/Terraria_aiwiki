namespace Terraria_Wiki.Windows.AI;

internal static class AiRuntimePaths
{
    internal static string BundledPythonExecutable() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "AgentRuntime",
        "python",
        "python.exe"));
}
