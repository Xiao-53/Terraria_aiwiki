using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Hosting;

namespace Terraria_Wiki.Windows.Presentation.Chat;

internal enum ChatAvailabilityState
{
    Disabled,
    Starting,
    Ready,
    Answering,
    Failed,
    Stopped,
}

internal sealed record ChatAvailability(
    ChatAvailabilityState State,
    string Message,
    string? ErrorCode = null);

internal interface IChatRuntimeGateway
{
    event EventHandler<ChatAvailability>? StatusChanged;
    ChatAvailability Status { get; }
    bool TryReserveAnswer();
    void ReleaseAnswer();
    Task<AiAgentAnswer> AnswerAsync(
        string question,
        IReadOnlyList<AiConversationHistoryTurn> history,
        Func<AiRunEvent, ValueTask> onEvent,
        CancellationToken cancellationToken);
    Task CancelAsync();
    Task RetryAsync(CancellationToken cancellationToken);
}

internal sealed class WindowsChatRuntimeGateway : IChatRuntimeGateway, IDisposable
{
    private readonly WindowsClientRuntime _runtime;

    public WindowsChatRuntimeGateway(WindowsClientRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _runtime.AiStatusChanged += OnStatusChanged;
    }

    public event EventHandler<ChatAvailability>? StatusChanged;

    public ChatAvailability Status => Map(_runtime.AiStatus);

    public bool TryReserveAnswer() => _runtime.TryReserveAiAnswer();

    public void ReleaseAnswer() => _runtime.ReleaseAiAnswer();

    public Task<AiAgentAnswer> AnswerAsync(
        string question,
        IReadOnlyList<AiConversationHistoryTurn> history,
        Func<AiRunEvent, ValueTask> onEvent,
        CancellationToken cancellationToken)
    {
        var ai = _runtime.Ai;
        if (ai is null)
            throw new AiRuntimeException(_runtime.AiStatus.ErrorCode ?? "runtime_crashed");
        return ai.AnswerAsync(question, history, onEvent, cancellationToken, _runtime.Preferences.AiAnswerStyle);
    }

    public Task CancelAsync() => _runtime.Ai?.CancelAsync() ?? Task.CompletedTask;

    public Task RetryAsync(CancellationToken cancellationToken) => _runtime.RetryAiAsync(cancellationToken);

    public void Dispose() => _runtime.AiStatusChanged -= OnStatusChanged;

    private void OnStatusChanged(object? sender, WindowsAiRuntimeStatus status) =>
        StatusChanged?.Invoke(this, Map(status));

    private static ChatAvailability Map(WindowsAiRuntimeStatus status) => new(
        status.State switch
        {
            WindowsAiRuntimeState.Disabled => ChatAvailabilityState.Disabled,
            WindowsAiRuntimeState.Starting => ChatAvailabilityState.Starting,
            WindowsAiRuntimeState.Ready => ChatAvailabilityState.Ready,
            WindowsAiRuntimeState.Answering => ChatAvailabilityState.Answering,
            WindowsAiRuntimeState.Failed => ChatAvailabilityState.Failed,
            _ => ChatAvailabilityState.Stopped,
        },
        status.Message,
        status.ErrorCode);
}
