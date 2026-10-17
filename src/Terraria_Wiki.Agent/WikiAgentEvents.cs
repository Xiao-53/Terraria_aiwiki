namespace Terraria_Wiki.Agent;

public enum WikiAgentEventKind
{
    RunStarted,
    ToolStarted,
    ToolFinished,
    AnswerDelta,
    AnswerReclassified,
    ProcessDelta,
}

public sealed record WikiAgentEvent(
    WikiAgentEventKind Kind,
    string? Name = null,
    string? Status = null,
    string? Delta = null);
