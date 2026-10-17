using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Conversations;
using Terraria_Wiki.Windows.Presentation.Infrastructure;
using Terraria_Wiki.Windows.Presentation.Markdown;

namespace Terraria_Wiki.Windows.Presentation.Chat;

internal enum ChatRunState
{
    Empty,
    Ready,
    Answering,
    Completed,
    Cancelled,
    Failed,
}

internal sealed class ChatToolProgressViewModel : ObservableObject
{
    private string _status;

    public ChatToolProgressViewModel(string name, string displayName, string status)
    {
        Name = name;
        _displayName = displayName;
        _status = status;
    }

    public string Name { get; }
    private readonly string _displayName;
    public string DisplayName => Text.Render(_displayName);
    public bool IsRunning => _status == "running";
    public bool IsSucceeded => _status == "succeeded";
    public bool IsNotFound => _status == "not_found";
    public bool IsCancelled => _status == "cancelled";
    public bool IsFailed => !IsRunning && !IsSucceeded && !IsNotFound && !IsCancelled;
    public string Status => Text.Render(UiTextCatalog.Message(_status switch
    {
        "running" => "Running",
        "succeeded" => "Complete",
        "not_found" => "NotFound",
        "cancelled" => "ToolCancelled",
        _ => "Failed",
    }));

    public void SetStatus(string status)
    {
        if (!SetProperty(ref _status, status, nameof(Status))) return;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsSucceeded));
        OnPropertyChanged(nameof(IsNotFound));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(IsFailed));
    }
}

internal sealed class ChatSourceViewModel : ObservableObject
{
    private readonly Func<AiAgentSource, Task> _open;

    public ChatSourceViewModel(AiAgentSource source, Func<AiAgentSource, Task> open)
    {
        Source = source;
        _open = open;
        Title = source.ResolvedTitle;
        Detail = BuildDetail(source);
        OpenCommand = new AsyncCommand(_ => _open(Source));
    }

    public AiAgentSource Source { get; }
    public string Title { get; }
    private string _localizedDetail = string.Empty;
    public string Detail { get => Text.Render(_localizedDetail); private set => _localizedDetail = value; }
    public AsyncCommand OpenCommand { get; }
    internal Task OpenAsync() => _open(Source);

    private static string BuildDetail(AiAgentSource source)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(source.Anchor))
            details.Add("§ " + source.Anchor);
        if (!string.Equals(source.RequestedTitle, source.ResolvedTitle, StringComparison.Ordinal))
            details.Add(UiTextCatalog.Message("SourceRequestedTitle", source.RequestedTitle));
        if (source.LastModified is not null)
            details.Add(source.LastModified.Value.ToString("yyyy-MM-dd"));
        return UiTextCatalog.Join(" · ", details.ToArray());
    }
}

internal sealed class ChatTurnViewModel : ObservableObject
{
    protected override void OnTextCatalogChanged()
    {
        foreach (var item in Sources) item.UseTextCatalog(Text);
        foreach (var item in Tools) item.UseTextCatalog(Text);
    }

    private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(75);
    private readonly SafeMarkdownParser _parser;
    private readonly Func<AiAgentSource, Task> _openSource;
    private readonly StringBuilder _streamingMarkdown = new();
    private readonly StringBuilder _processMarkdown = new();
    private SafeMarkdownDocument _processDocument = SafeMarkdownDocument.Empty;
    private bool _isProcessExpanded;
    private CancellationTokenSource? _scheduledRender;
    private DateTimeOffset _lastRender = DateTimeOffset.MinValue;
    private SafeMarkdownDocument _answerDocument;
    private string _statusMessage;
    private string _usageText;
    private ChatRunState _runState;

    public ChatTurnViewModel(
        ChatConversationTurn model,
        SafeMarkdownParser parser,
        Func<AiAgentSource, Task> openSource)
    {
        Model = model;
        _parser = parser;
        _openSource = openSource;
        _answerDocument = string.IsNullOrEmpty(model.Markdown) ? SafeMarkdownDocument.Empty : parser.Parse(model.Markdown);
        _streamingMarkdown.Append(model.Markdown);
        _usageText = model.Usage is null ? string.Empty : ChatViewModel.FormatTokenSpend(model.Usage);
        _runState = MapRunState(model.Status);
        _statusMessage = StatusFor(model.Status);
        _isProcessExpanded = model.Status == ChatConversationTurnStatus.Pending;
        foreach (var source in model.Sources)
            Sources.Add(new ChatSourceViewModel(source, openSource).WithText(Text));
        foreach (var call in model.ToolTrace)
            Tools.Add(new ChatToolProgressViewModel(call.Name, DisplayToolName(call.Name), call.Status).WithText(Text));
    }

    public ChatConversationTurn Model { get; private set; }
    public string Question => Model.Question;
    public ObservableCollection<ChatToolProgressViewModel> Tools { get; } = [];
    public ObservableCollection<ChatSourceViewModel> Sources { get; } = [];
    public SafeMarkdownDocument ProcessDocument
    {
        get => _processDocument;
        private set
        {
            if (SetProperty(ref _processDocument, value))
            {
                OnPropertyChanged(nameof(HasProcessText));
                OnPropertyChanged(nameof(HasProcess));
                OnPropertyChanged(nameof(IsProcessVisible));
            }
        }
    }
    public bool HasProcessText => ProcessDocument.Source.Length != 0;
    public bool HasProcess => HasTools || HasProcessText;
    public bool IsProcessVisible => IsAnswering || HasProcess;
    public bool IsProcessExpanded { get => _isProcessExpanded; set => SetProperty(ref _isProcessExpanded, value); }
    public string ProcessSummary => RunState == ChatRunState.Completed
        ? Text.Render(UiTextCatalog.Message("CompletedToolCalls", Model.Usage?.ToolCalls ?? Tools.Count))
        : IsAnswering ? Text.Render(UiTextCatalog.Message("SearchingTheLocalWiki")) : StatusMessage;
    public SafeMarkdownDocument AnswerDocument
    {
        get => _answerDocument;
        private set
        {
            if (!SetProperty(ref _answerDocument, value))
                return;
            OnPropertyChanged(nameof(HasAnswer));
            OnPropertyChanged(nameof(IsThinking));
            OnPropertyChanged(nameof(IsStreamingAnswer));
            OnPropertyChanged(nameof(ActivityTitle));
        }
    }
    public string StatusMessage
    {
        get => Text.Render(_statusMessage);
        private set
        {
            if (SetProperty(ref _statusMessage, UiTextCatalog.FromSafeMessage(value)))
                OnPropertyChanged(nameof(ProcessSummary));
        }
    }
    public string UsageText
    {
        get => Text.Render(_usageText);
        private set
        {
            if (SetProperty(ref _usageText, value))
                OnPropertyChanged(nameof(HasUsage));
        }
    }
    public ChatRunState RunState
    {
        get => _runState;
        private set
        {
            if (!SetProperty(ref _runState, value))
                return;
            OnPropertyChanged(nameof(IsAnswering));
            OnPropertyChanged(nameof(IsThinking));
            OnPropertyChanged(nameof(IsStreamingAnswer));
            OnPropertyChanged(nameof(IsToolTraceVisible));
            OnPropertyChanged(nameof(IsTerminalStatusVisible));
            OnPropertyChanged(nameof(IsAttentionStatusVisible));
            OnPropertyChanged(nameof(ProcessSummary));
            OnPropertyChanged(nameof(IsProcessVisible));
        }
    }
    public bool IsAnswering => RunState == ChatRunState.Answering;
    public bool HasAnswer => AnswerDocument.Source.Length > 0;
    public bool HasUsage => UsageText.Length > 0;
    public bool HasSources => Sources.Count > 0;
    public bool HasTools => Tools.Count > 0;
    public bool IsThinking => IsAnswering && !HasAnswer;
    public bool IsStreamingAnswer => IsAnswering && HasAnswer;
    public bool IsToolTraceVisible => HasTools && !IsAnswering;
    public bool IsTerminalStatusVisible => !IsAnswering;
    public bool IsAttentionStatusVisible => !IsAnswering && Model.Status != ChatConversationTurnStatus.Completed;
    public string ActivityTitle => Text.Render(HasAnswer
        ? UiTextCatalog.Message("GeneratingAnswer")
        : HasTools ? UiTextCatalog.Message("ReadingTheLocalWiki") : UiTextCatalog.Message("AnalyzingYourQuestion"));

    public void ApplyEvent(AiRunEvent value)
    {
        switch (value.Kind)
        {
            case AiRunEventKind.RunStarted:
                StatusMessage = UiTextCatalog.Message("TheModelHasStartedAnalyzing");
                break;
            case AiRunEventKind.ToolStarted:
                Tools.Add(new ChatToolProgressViewModel(
                    value.Name ?? "wiki_tool", DisplayToolName(value.Name), "running").WithText(Text));
                RaiseToolProperties();
                break;
            case AiRunEventKind.ToolFinished:
                var item = Tools.LastOrDefault(tool => tool.Name == value.Name && tool.IsRunning);
                if (item is null)
                    Tools.Add(new ChatToolProgressViewModel(
                        value.Name ?? "wiki_tool", DisplayToolName(value.Name), value.Status ?? "failed").WithText(Text));
                else
                    item.SetStatus(value.Status ?? "failed");
                RaiseToolProperties();
                break;
            case AiRunEventKind.AnswerDelta:
                if (!string.IsNullOrEmpty(value.Delta))
                    AppendMarkdown(value.Delta);
                break;
            case AiRunEventKind.AnswerReclassified:
                CancelScheduledRender();
                if (_processMarkdown.Length > 0) _processMarkdown.AppendLine().AppendLine();
                _processMarkdown.Append(_streamingMarkdown);
                _streamingMarkdown.Clear();
                _lastRender = DateTimeOffset.MinValue;
                AnswerDocument = SafeMarkdownDocument.Empty;
                RenderProcessMarkdown();
                break;
            case AiRunEventKind.ProcessDelta:
                _processMarkdown.Append(value.Delta);
                RenderProcessMarkdown();
                break;
        }
    }

    public void Complete(AiAgentAnswer answer, DateTimeOffset now)
    {
        CancelScheduledRender();
        _streamingMarkdown.Clear();
        _streamingMarkdown.Append(answer.Markdown);
        AnswerDocument = _parser.Parse(answer.Markdown);
        Sources.Clear();
        foreach (var source in answer.Sources)
            Sources.Add(new ChatSourceViewModel(source, _openSource).WithText(Text));
        Tools.Clear();
        foreach (var call in answer.ToolTrace)
            Tools.Add(new ChatToolProgressViewModel(call.Name, DisplayToolName(call.Name), call.Status).WithText(Text));
        RaiseToolProperties();
        OnPropertyChanged(nameof(HasSources));
        UsageText = ChatViewModel.FormatTokenSpend(answer.Usage);
        var status = answer.Status == "answered"
            ? ChatConversationTurnStatus.Completed
            : ChatConversationTurnStatus.InsufficientEvidence;
        Model = Model with
        {
            Markdown = answer.Markdown,
            Sources = answer.Sources,
            ToolTrace = answer.ToolTrace,
            Usage = answer.Usage,
            Status = status,
            UpdatedAt = now,
        };
        RunState = ChatRunState.Completed;
        IsProcessExpanded = false;
        OnPropertyChanged(nameof(ProcessSummary));
        StatusMessage = status == ChatConversationTurnStatus.Completed
            ? UiTextCatalog.Message("AnswerComplete")
            : UiTextCatalog.Message("NotEnoughLocalEvidenceTryAMoreSpecific");
    }

    public void FinishNonSuccess(ChatConversationTurnStatus status, string message, DateTimeOffset now)
    {
        CancelScheduledRender();
        foreach (var tool in Tools.Where(tool => tool.IsRunning))
            tool.SetStatus(status == ChatConversationTurnStatus.Cancelled ? "cancelled" : "failed");
        var markdown = _streamingMarkdown.ToString();
        if (markdown.Length > 0)
            AnswerDocument = _parser.Parse(markdown);
        // Keep partial text visible, but only typed completion can persist a final body.
        Model = Model with { Markdown = string.Empty, Status = status, UpdatedAt = now };
        RunState = status == ChatConversationTurnStatus.Cancelled ? ChatRunState.Cancelled : ChatRunState.Failed;
        StatusMessage = message;
    }

    private void AppendMarkdown(string delta)
    {
        _streamingMarkdown.Append(delta);
        var now = DateTimeOffset.UtcNow;
        var elapsed = now - _lastRender;
        if (elapsed >= RenderInterval)
        {
            RenderStreamingMarkdown(now);
            return;
        }
        if (_scheduledRender is not null)
            return;
        _scheduledRender = new CancellationTokenSource();
        _ = ScheduleRenderAsync(RenderInterval - elapsed, _scheduledRender);
    }

    private void RenderProcessMarkdown() => ProcessDocument = _parser.Parse(_processMarkdown.ToString(),
        AiTextLimits.MaximumCharacters * (AiTextLimits.MaximumToolAttempts + 1) + 2 * AiTextLimits.MaximumToolAttempts);

    private async Task ScheduleRenderAsync(TimeSpan delay, CancellationTokenSource scheduled)
    {
        try
        {
            await Task.Delay(delay, scheduled.Token);
            void Render()
            {
                if (!ReferenceEquals(scheduled, _scheduledRender))
                    return;
                _scheduledRender = null;
                RenderStreamingMarkdown(DateTimeOffset.UtcNow);
            }
            if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
                Render();
            else
                await Dispatcher.UIThread.InvokeAsync(Render);
        }
        catch (OperationCanceledException) { }
        finally { scheduled.Dispose(); }
    }

    private void RenderStreamingMarkdown(DateTimeOffset now)
    {
        _lastRender = now;
        AnswerDocument = _parser.Parse(_streamingMarkdown.ToString());
    }

    private void CancelScheduledRender()
    {
        _scheduledRender?.Cancel();
        _scheduledRender = null;
    }

    private void RaiseToolProperties()
    {
        OnPropertyChanged(nameof(HasTools));
        OnPropertyChanged(nameof(HasProcess));
        OnPropertyChanged(nameof(IsProcessVisible));
        OnPropertyChanged(nameof(ProcessSummary));
        OnPropertyChanged(nameof(IsToolTraceVisible));
        OnPropertyChanged(nameof(ActivityTitle));
    }

    private static ChatRunState MapRunState(ChatConversationTurnStatus status) => status switch
    {
        ChatConversationTurnStatus.Pending => ChatRunState.Answering,
        ChatConversationTurnStatus.Completed or ChatConversationTurnStatus.InsufficientEvidence => ChatRunState.Completed,
        ChatConversationTurnStatus.Cancelled => ChatRunState.Cancelled,
        _ => ChatRunState.Failed,
    };

    private static string StatusFor(ChatConversationTurnStatus status) => status switch
    {
        ChatConversationTurnStatus.Completed => UiTextCatalog.Message("AnswerComplete"),
        ChatConversationTurnStatus.InsufficientEvidence => UiTextCatalog.Message("NotEnoughLocalEvidence"),
        ChatConversationTurnStatus.Cancelled => UiTextCatalog.Message("AIAnswerCancelled"),
        ChatConversationTurnStatus.Interrupted => UiTextCatalog.Message("ThePreviousAnswerWasInterrupted"),
        ChatConversationTurnStatus.Failed => UiTextCatalog.Message("AIAnswerFailed"),
        _ => UiTextCatalog.Message("SearchingTheLocalWiki"),
    };

    private static string DisplayToolName(string? name) => name switch
    {
        "search_wiki" => UiTextCatalog.Message("SearchWiki"),
        "read_wiki" => UiTextCatalog.Message("ReadPage"),
        _ => UiTextCatalog.Message("WikiTool"),
    };

}

internal sealed class ChatViewModel : ObservableObject, IDisposable
{
    protected override void OnTextCatalogChanged()
    {
        foreach (var item in Turns) item.UseTextCatalog(Text);
    }

    internal const int HistoryCharacterBudget = 24_000;
    private const string HistoryTruncationMarker = "\n\n[较早回答已在本地截断]";
    private readonly SafeMarkdownParser _markdownParser;
    private readonly Func<AiAgentSource, Task> _openSource;
    private readonly Action _openWiki;
    private readonly IChatConversationStore _store;
    private IChatRuntimeGateway? _runtime;
    private IDisposable? _runtimeOwner;
    private CancellationTokenSource? _answerCancellation;
    private ChatConversation? _conversation;
    private string _question = string.Empty;
    private string _statusMessage = UiTextCatalog.Message("AIIsStarting");
    private ChatAvailabilityState _availability = ChatAvailabilityState.Starting;
    private bool _disposed;

    public ChatViewModel(
        SafeMarkdownParser markdownParser,
        Func<AiAgentSource, Task> openSource,
        Action openWiki,
        IChatConversationStore? store = null)
    {
        _markdownParser = markdownParser ?? throw new ArgumentNullException(nameof(markdownParser));
        _openSource = openSource ?? throw new ArgumentNullException(nameof(openSource));
        _openWiki = openWiki ?? throw new ArgumentNullException(nameof(openWiki));
        _store = store ?? new MemoryChatConversationStore();
        SendCommand = new AsyncCommand(_ => SendAsync(), _ => CanSend);
        StopCommand = new AsyncCommand(_ => StopAsync(), _ => IsAnswering);
        RetryCommand = new AsyncCommand(_ => RetryAsync(), _ => CanRetry);
        OpenWikiCommand = new RelayCommand(_ => _openWiki());
        ToggleFavoriteCommand = new AsyncCommand(_ => ToggleFavoriteAsync(), _ => CanFavorite);
        NewConversationCommand = new RelayCommand(_ => StartNewConversation(), _ => CanStartNewConversation);
    }

    public ObservableCollection<ChatTurnViewModel> Turns { get; } = [];
    public event Action<Guid?>? ConversationChanged;
    public string Question
    {
        get => _question;
        set { if (SetProperty(ref _question, value)) RefreshCommands(); }
    }
    public string StatusMessage { get => Text.Render(_statusMessage); private set => SetProperty(ref _statusMessage, UiTextCatalog.FromSafeMessage(value)); }
    public bool IsEmpty => Turns.Count == 0;
    public bool IsAnswering => Turns.LastOrDefault()?.IsAnswering == true;
    public bool IsSendVisible => !IsAnswering;
    public bool IsStopVisible => IsAnswering;
    public string ComposerPlaceholder => Text.Render(IsAnswering ? UiTextCatalog.Message("AIIsAnswering") : UiTextCatalog.Message("AskAI"));
    public bool CanEditQuestion => !IsAnswering;
    public bool IsQuestionReadOnly => !CanEditQuestion;
    public bool CanSend => _availability == ChatAvailabilityState.Ready
                           && !IsAnswering && !string.IsNullOrWhiteSpace(Question)
                           && (_conversation?.Turns.Count ?? 0) < 100;
    public bool CanRetry => !IsAnswering
                            && _availability is ChatAvailabilityState.Disabled or ChatAvailabilityState.Failed;
    public bool IsAiUnavailable => _availability is ChatAvailabilityState.Disabled or ChatAvailabilityState.Failed;
    public bool IsAvailabilityStatusVisible => _availability != ChatAvailabilityState.Ready && !IsAnswering;
    public bool IsFavorite => _conversation?.IsFavorite == true;
    public string FavoriteActionText => Text.Render(IsFavorite ? UiTextCatalog.Message("UnfavoriteThisConversation") : UiTextCatalog.Message("FavoriteThisConversation"));
    public bool CanFavorite => _conversation is not null && !IsAnswering;
    public bool CanStartNewConversation => !IsAnswering;
    public Guid? CurrentConversationId => _conversation?.Id;

    // Compatibility projections retained for focused behavior tests and automation.
    public string SubmittedQuestion => Turns.LastOrDefault()?.Question ?? string.Empty;
    public SafeMarkdownDocument AnswerDocument => Turns.LastOrDefault()?.AnswerDocument ?? SafeMarkdownDocument.Empty;
    public ObservableCollection<ChatToolProgressViewModel> Tools => Turns.LastOrDefault()?.Tools ?? [];
    public ObservableCollection<ChatSourceViewModel> Sources => Turns.LastOrDefault()?.Sources ?? [];
    public string UsageText => Text.Render(Turns.LastOrDefault()?.UsageText ?? string.Empty);
    public ChatRunState RunState => Turns.LastOrDefault()?.RunState ?? (IsEmpty ? ChatRunState.Empty : ChatRunState.Ready);
    public bool HasAnswer => Turns.LastOrDefault()?.HasAnswer == true;
    public bool IsThinking => Turns.LastOrDefault()?.IsThinking == true;
    public bool IsStreamingAnswer => Turns.LastOrDefault()?.IsStreamingAnswer == true;
    public bool IsToolTraceVisible => Turns.LastOrDefault()?.IsToolTraceVisible == true;
    public bool IsTerminalStatusVisible => Turns.LastOrDefault()?.IsTerminalStatusVisible == true;

    public AsyncCommand SendCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand RetryCommand { get; }
    public RelayCommand OpenWikiCommand { get; }
    public AsyncCommand ToggleFavoriteCommand { get; }
    public RelayCommand NewConversationCommand { get; }

    public void AttachRuntime(IChatRuntimeGateway runtime, IDisposable? owner = null)
    {
        DetachRuntime();
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _runtimeOwner = owner;
        _runtime.StatusChanged += OnRuntimeStatusChanged;
        ApplyAvailability(_runtime.Status);
    }

    public void DetachRuntime()
    {
        if (_runtime is not null)
            _runtime.StatusChanged -= OnRuntimeStatusChanged;
        _runtime = null;
        _runtimeOwner?.Dispose();
        _runtimeOwner = null;
        _availability = ChatAvailabilityState.Stopped;
        StatusMessage = UiTextCatalog.Message("AIIsUnavailableTheOfflineWikiIsStill");
        RefreshProperties();
    }

    public bool LoadConversation(ChatConversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (IsAnswering)
            return false;
        _conversation = conversation;
        Turns.Clear();
        foreach (var turn in conversation.Turns)
            Turns.Add(new ChatTurnViewModel(turn, _markdownParser, _openSource).WithText(Text));
        Question = string.Empty;
        StatusMessage = UiTextCatalog.Message("LocalConversationOpened");
        RaiseConversationProperties();
        ConversationChanged?.Invoke(conversation.Id);
        return true;
    }

    public void StartNewConversation()
    {
        if (IsAnswering)
            return;
        _conversation = null;
        Turns.Clear();
        StatusMessage = UiTextCatalog.Message("NewConversationStarted");
        RaiseConversationProperties();
        ConversationChanged?.Invoke(null);
    }

    public void MarkConversationDeleted(Guid id)
    {
        if (_conversation?.Id == id)
            StartNewConversation();
    }

    public void SetFavoriteState(Guid id, bool isFavorite)
    {
        if (_conversation?.Id != id)
            return;
        _conversation = _conversation with { IsFavorite = isFavorite };
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteActionText));
    }

    public async Task SendAsync()
    {
        var runtime = _runtime;
        var question = Question.Trim();
        if (runtime is null || !CanSend || question.Length == 0)
            return;
        if (!runtime.TryReserveAnswer())
        {
            StatusMessage = UiTextCatalog.Message("AIIsAnsweringInAnotherWindowYourDraft");
            return;
        }

        var now = DateTimeOffset.Now;
        var conversation = _conversation ?? new ChatConversation
        {
            Id = Guid.NewGuid(),
            Title = ChatConversation.CreateTitle(question),
            CreatedAt = now,
            UpdatedAt = now,
        };
        var history = BuildHistory(conversation.Turns);
        var pending = new ChatConversationTurn
        {
            Id = Guid.NewGuid(),
            Question = question,
            Status = ChatConversationTurnStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        conversation = conversation with
        {
            UpdatedAt = now,
            Turns = conversation.Turns.Append(pending).ToArray(),
        };
        _conversation = conversation;
        var turnViewModel = new ChatTurnViewModel(pending, _markdownParser, _openSource).WithText(Text);
        Turns.Add(turnViewModel);
        Question = string.Empty;
        RaiseConversationProperties();
        await TrySaveAsync();

        _answerCancellation = new CancellationTokenSource();
        var current = _answerCancellation;
        StatusMessage = UiTextCatalog.Message("SearchingTheLocalWiki");
        try
        {
            var answer = await runtime.AnswerAsync(
                question,
                history,
                value => ApplyRunEventAsync(turnViewModel, value, current.Token),
                current.Token);
            turnViewModel.Complete(answer, DateTimeOffset.Now);
            StatusMessage = turnViewModel.StatusMessage;
        }
        catch (OperationCanceledException)
        {
            turnViewModel.FinishNonSuccess(
                ChatConversationTurnStatus.Cancelled, UiTextCatalog.Message("AIAnswerCancelled"), DateTimeOffset.Now);
            StatusMessage = turnViewModel.StatusMessage;
        }
        catch (AiRuntimeException exception)
        {
            turnViewModel.FinishNonSuccess(
                ChatConversationTurnStatus.Failed,
                UiTextCatalog.Join("", UiTextCatalog.FromSafeMessage(exception.Message), UiTextCatalog.Message("TheOfflineWikiIsStillAvailable")),
                DateTimeOffset.Now);
            StatusMessage = turnViewModel.StatusMessage;
        }
        catch
        {
            turnViewModel.FinishNonSuccess(
                ChatConversationTurnStatus.Failed,
                UiTextCatalog.Message("AIAnswerFailedTheOfflineWikiIsStill"),
                DateTimeOffset.Now);
            StatusMessage = turnViewModel.StatusMessage;
        }
        finally
        {
            if (ReferenceEquals(current, _answerCancellation))
            {
                _answerCancellation.Dispose();
                _answerCancellation = null;
            }
            SyncConversationFromTurns();
            await TrySaveAsync();
            runtime.ReleaseAnswer();
            RaiseConversationProperties();
            ConversationChanged?.Invoke(_conversation?.Id);
        }
    }

    public async Task StopAsync()
    {
        if (!IsAnswering)
            return;
        _answerCancellation?.Cancel();
        try { if (_runtime is not null) await _runtime.CancelAsync(); }
        catch (AiRuntimeException exception) { StatusMessage = exception.Message; }
    }

    public async Task RetryAsync()
    {
        if (_runtime is null || !CanRetry)
            return;
        StatusMessage = UiTextCatalog.Message("RestartingAI");
        try { await _runtime.RetryAsync(CancellationToken.None); }
        catch (OperationCanceledException) { StatusMessage = UiTextCatalog.Message("AIRetryCancelled"); }
        catch { StatusMessage = UiTextCatalog.Message("AIFailedToStartTheOfflineWikiIs"); }
    }

    internal async Task ToggleFavoriteAsync()
    {
        if (_conversation is null || !CanFavorite)
            return;
        _conversation = _conversation with
        {
            IsFavorite = !_conversation.IsFavorite,
            UpdatedAt = DateTimeOffset.Now,
        };
        try
        {
            await _store.SaveAsync(_conversation);
            StatusMessage = _conversation.IsFavorite ? UiTextCatalog.Message("ConversationFavorited") : UiTextCatalog.Message("RemovedFromFavoritesTheConversationRemainsInHistory");
            ConversationChanged?.Invoke(_conversation.Id);
        }
        catch (ChatConversationStoreException exception)
        {
            _conversation = _conversation with { IsFavorite = !_conversation.IsFavorite };
            StatusMessage = exception.Message;
        }
        catch
        {
            _conversation = _conversation with { IsFavorite = !_conversation.IsFavorite };
            StatusMessage = UiTextCatalog.Message("CouldNotUpdateTheConversationFavorite");
        }
        RaiseConversationProperties();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _answerCancellation?.Cancel();
        _answerCancellation?.Dispose();
        DetachRuntime();
    }

    internal static IReadOnlyList<AiConversationHistoryTurn> BuildHistory(
        IReadOnlyList<ChatConversationTurn> turns)
    {
        var selected = new List<AiConversationHistoryTurn>();
        var remaining = HistoryCharacterBudget;
        foreach (var turn in turns.Where(turn => turn.IsModelHistoryEligible).Reverse().Take(4))
        {
            var required = turn.Question.Length + turn.Markdown.Length;
            if (required <= remaining)
            {
                selected.Add(new AiConversationHistoryTurn(turn.Question, turn.Markdown));
                remaining -= required;
                continue;
            }
            var answerBudget = remaining - turn.Question.Length;
            if (answerBudget > HistoryTruncationMarker.Length)
            {
                var answer = TruncateAtParagraph(turn.Markdown, answerBudget - HistoryTruncationMarker.Length)
                             + HistoryTruncationMarker;
                selected.Add(new AiConversationHistoryTurn(turn.Question, answer));
            }
            break;
        }
        selected.Reverse();
        return selected;
    }

    internal static string FormatTokenSpend(AiAgentUsage usage)
    {
        var cache = usage.CachedInputTokens > 0
            ? UiTextCatalog.Message("CachedUncached", usage.CachedInputTokens, usage.InputTokens - usage.CachedInputTokens)
            : UiTextCatalog.Message("CacheMissUnavailableUncached", usage.InputTokens);
        return UiTextCatalog.Message("InputOutputTotalTokens", usage.InputTokens, cache, usage.OutputTokens, usage.TotalTokens);
    }

    private static string TruncateAtParagraph(string value, int limit)
    {
        if (value.Length <= limit)
            return value;
        var candidate = value[..Math.Max(0, limit)];
        var split = candidate.LastIndexOf("\n\n", StringComparison.Ordinal);
        return (split > limit / 2 ? candidate[..split] : candidate).TrimEnd();
    }

    private ValueTask ApplyRunEventAsync(ChatTurnViewModel turn, AiRunEvent value, CancellationToken cancellationToken)
    {
        if (_disposed || cancellationToken.IsCancellationRequested) return ValueTask.CompletedTask;
        if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            turn.ApplyEvent(value);
            return ValueTask.CompletedTask;
        }
        return DispatchRunEventAsync(turn, value, cancellationToken);
    }

    private async ValueTask DispatchRunEventAsync(ChatTurnViewModel turn, AiRunEvent value, CancellationToken cancellationToken) =>
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_disposed && !cancellationToken.IsCancellationRequested) turn.ApplyEvent(value);
        });

    private async Task TrySaveAsync()
    {
        if (_conversation is null)
            return;
        try { await _store.SaveAsync(_conversation); }
        catch { StatusMessage = UiTextCatalog.Message("YouCanKeepUsingThisConversationButLocal"); }
    }

    private void SyncConversationFromTurns()
    {
        if (_conversation is null)
            return;
        _conversation = _conversation with
        {
            Turns = Turns.Select(turn => turn.Model).ToArray(),
            UpdatedAt = Turns.LastOrDefault()?.Model.UpdatedAt ?? _conversation.UpdatedAt,
        };
    }

    private void OnRuntimeStatusChanged(object? sender, ChatAvailability availability)
    {
        var startupAttempt = WindowsStartupInvestigation.CurrentAiAttempt;
        void ApplyCurrent()
        {
            if (!_disposed && ReferenceEquals(sender, _runtime)) ApplyAvailability(availability, startupAttempt);
        }
        if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
            ApplyCurrent();
        else
            Dispatcher.UIThread.Post(ApplyCurrent);
    }

    private void ApplyAvailability(ChatAvailability availability, int startupAttempt = 0)
    {
        var phase = availability.State switch
        {
            ChatAvailabilityState.Starting => StartupInvestigationPhase.AiUiStarting,
            ChatAvailabilityState.Ready => StartupInvestigationPhase.AiUiReady,
            ChatAvailabilityState.Disabled => StartupInvestigationPhase.AiUiDisabled,
            ChatAvailabilityState.Failed => StartupInvestigationPhase.AiUiFailed,
            _ => StartupInvestigationPhase.AiUiStopped,
        };
        if (availability.State != ChatAvailabilityState.Answering)
            WindowsStartupInvestigation.Checkpoint(phase, startupAttempt);
        _availability = availability.State;
        if (!IsAnswering || availability.State is ChatAvailabilityState.Disabled or ChatAvailabilityState.Failed)
            StatusMessage = availability.Message;
        RefreshProperties();
    }

    private void RaiseConversationProperties()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteActionText));
        OnPropertyChanged(nameof(CanFavorite));
        OnPropertyChanged(nameof(CurrentConversationId));
        OnPropertyChanged(nameof(SubmittedQuestion));
        OnPropertyChanged(nameof(AnswerDocument));
        OnPropertyChanged(nameof(Tools));
        OnPropertyChanged(nameof(Sources));
        OnPropertyChanged(nameof(UsageText));
        OnPropertyChanged(nameof(RunState));
        OnPropertyChanged(nameof(HasAnswer));
        RefreshProperties();
    }

    private void RefreshProperties()
    {
        OnPropertyChanged(nameof(IsAnswering));
        OnPropertyChanged(nameof(IsSendVisible));
        OnPropertyChanged(nameof(IsStopVisible));
        OnPropertyChanged(nameof(ComposerPlaceholder));
        OnPropertyChanged(nameof(CanEditQuestion));
        OnPropertyChanged(nameof(IsQuestionReadOnly));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(IsAiUnavailable));
        OnPropertyChanged(nameof(IsAvailabilityStatusVisible));
        OnPropertyChanged(nameof(CanStartNewConversation));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SendCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        NewConversationCommand.NotifyCanExecuteChanged();
    }
}
