using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Terraria_Wiki.Agent;

// The observer is inside the framework function loop: one invocation is one
// provider response. Candidate text is visible immediately and can be reclassified.
internal sealed class WikiAgentStreamObserver(
    IChatClient inner,
    Func<WikiAgentEvent, ValueTask> emit,
    WikiAgentToolState? tools = null) : DelegatingChatClient(inner)
{
    internal string FinalMarkdown { get; private set; } = "";
    internal int Requests { get; private set; }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options = tools?.BeginResponse(options) ?? options;
        var sanitizer = new IncrementalMarkdownSanitizer();
        var hasTools = false;
        FinalMarkdown = "";
        if (tools is { Finalizing: true, HasRead: false })
        {
            const string insufficient = "本地 Wiki 证据不足，无法可靠回答这个问题。";
            await emit(new(WikiAgentEventKind.AnswerDelta, Delta: insufficient)).ConfigureAwait(false);
            FinalMarkdown = insufficient;
            yield return new ChatResponseUpdate(ChatRole.Assistant, insufficient);
            yield break;
        }
        Requests++;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
            {
                if (content is FunctionCallContent call)
                {
                    tools?.Reserve(call);
                    if (!hasTools)
                    {
                        hasTools = true;
                        await emit(new(WikiAgentEventKind.AnswerReclassified)).ConfigureAwait(false);
                    }
                }
                if (content is TextContent text)
                {
                    var delta = sanitizer.Feed(text.Text);
                    if (delta.Length != 0)
                        await emit(new(hasTools ? WikiAgentEventKind.ProcessDelta : WikiAgentEventKind.AnswerDelta,
                            Delta: delta)).ConfigureAwait(false);
                }
            }
            yield return update;
        }
        var tail = sanitizer.Finish();
        if (tail.Length != 0)
            await emit(new(hasTools ? WikiAgentEventKind.ProcessDelta : WikiAgentEventKind.AnswerDelta, Delta: tail))
                .ConfigureAwait(false);
        if (!hasTools) FinalMarkdown = sanitizer.Output;
    }
}
