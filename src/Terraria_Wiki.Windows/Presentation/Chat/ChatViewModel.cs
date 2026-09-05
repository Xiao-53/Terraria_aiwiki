using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Presentation.Favorites;
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
        DisplayName = displayName;
        _status = status;
    }

    public string Name { get; }
    public string DisplayName { get; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}

internal sealed class ChatSourceViewModel
{
    private readonly Func<AiAgentSource, Task> _open;

    public ChatSourceViewModel(AiAgentSource source, Func<AiAgentSource, Task> open)
    {
        Source = source;
        _open = open;
        Title = source.ResolvedTitle;
        Detail = BuildDetail(source);
        OpenCommand = new AsyncCommand(_ => OpenAsync());
    }

    public AiAgentSource Source { get; }
    public string Title { get; }
    public string Detail { get; }
    public AsyncCommand OpenCommand { get; }

    internal Task OpenAsync() => _open(Source);

    private static string BuildDetail(AiAgentSource source)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(source.Anchor))
            details.Add("§ " + source.Anchor);
        if (!string.Equals(source.RequestedTitle, source.ResolvedTitle, StringComparison.Ordinal))
            details.Add("请求：" + source.RequestedTitle);
        if (source.LastModified is not null)
            details.Add(source.LastModified.Value.ToString("yyyy-MM-dd"));
        return string.Join(" · ", details);
    }
}

internal sealed class ChatViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(75);
    private readonly SafeMarkdownParser _markdownParser;
    private readonly Func<AiAgentSource, Task> _openSource;
    private readonly Action _openWiki;
    private readonly Func<ChatFavoriteRecord, bool, Task<bool>>? _toggleFavorite;
    private readonly StringBuilder _streamingMarkdown = new();
    private IChatRuntimeGateway? _runtime;
    private IDisposable? _runtimeOwner;
    private CancellationTokenSource? _answerCancellation;
    private CancellationTokenSource? _scheduledRender;
    private DateTimeOffset _lastRender = DateTimeOffset.MinValue;
    private string _question = string.Empty;
    private string _submittedQuestion = string.Empty;
    private string _statusMessage = "AI Runtime 正在启动。";
    private string _usageText = string.Empty;
    private SafeMarkdownDocument _answerDocument = SafeMarkdownDocument.Empty;
    private ChatRunState _runState = ChatRunState.Empty;
    private ChatAvailabilityState _availability = ChatAvailabilityState.Starting;
    private Guid? _favoriteId;
    private bool _isFavorite;
    private bool _disposed;

    public ChatViewModel(
        SafeMarkdownParser markdownParser,
        Func<AiAgentSource, Task> openSource,
        Action openWiki,
        Func<ChatFavoriteRecord, bool, Task<bool>>? toggleFavorite = null)
    {
        _markdownParser = markdownParser ?? throw new ArgumentNullException(nameof(markdownParser));
        _openSource = openSource ?? throw new ArgumentNullException(nameof(openSource));
        _openWiki = openWiki ?? throw new ArgumentNullException(nameof(openWiki));
        _toggleFavorite = toggleFavorite;
        SendCommand = new AsyncCommand(_ => SendAsync(), _ => CanSend);
        StopCommand = new AsyncCommand(_ => StopAsync(), _ => IsAnswering);
        RetryCommand = new AsyncCommand(_ => RetryAsync(), _ => CanRetry);
        OpenWikiCommand = new RelayCommand(_ => _openWiki());
        ToggleFavoriteCommand = new AsyncCommand(_ => ToggleFavoriteAsync(), _ => CanFavorite);
    }

    public ObservableCollection<ChatToolProgressViewModel> Tools { get; } = [];
    public ObservableCollection<ChatSourceViewModel> Sources { get; } = [];

    public string Question
    {
        get => _question;
        set
        {
            if (SetProperty(ref _question, value))
                RefreshCommands();
        }
    }

    public string SubmittedQuestion
    {
        get => _submittedQuestion;
        private set
        {
            if (SetProperty(ref _submittedQuestion, value))
            {
                OnPropertyChanged(nameof(HasSubmittedQuestion));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string UsageText
    {
        get => _usageText;
        private set
        {
            if (SetProperty(ref _usageText, value))
                OnPropertyChanged(nameof(HasUsage));
        }
    }

    public SafeMarkdownDocument AnswerDocument
    {
        get => _answerDocument;
        private set
        {
            if (SetProperty(ref _answerDocument, value))
            {
                OnPropertyChanged(nameof(HasAnswer));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(IsThinking));
                OnPropertyChanged(nameof(IsStreamingAnswer));
                OnPropertyChanged(nameof(ActivityTitle));
            }
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
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsSendVisible));
            OnPropertyChanged(nameof(IsStopVisible));
            OnPropertyChanged(nameof(CanEditQuestion));
            OnPropertyChanged(nameof(IsQuestionReadOnly));
            OnPropertyChanged(nameof(CanFavorite));
            OnPropertyChanged(nameof(IsThinking));
            OnPropertyChanged(nameof(IsStreamingAnswer));
            OnPropertyChanged(nameof(IsToolTraceVisible));
            OnPropertyChanged(nameof(IsTerminalStatusVisible));
            OnPropertyChanged(nameof(ComposerPlaceholder));
            RefreshCommands();
        }
    }

    public bool IsAnswering => RunState == ChatRunState.Answering;
    public bool IsEmpty => !HasSubmittedQuestion && !HasAnswer;
    public bool HasSubmittedQuestion => !string.IsNullOrWhiteSpace(SubmittedQuestion);
    public bool HasAnswer => AnswerDocument.Source.Length > 0;
    public bool HasUsage => UsageText.Length > 0;
    public bool HasSources => Sources.Count > 0;
    public bool HasTools => Tools.Count > 0;
    public bool IsThinking => IsAnswering && !HasAnswer;
    public bool IsStreamingAnswer => IsAnswering && HasAnswer;
    public bool IsToolTraceVisible => HasTools && !IsAnswering;
    public bool IsTerminalStatusVisible => HasSubmittedQuestion && !IsAnswering;
    public string ActivityTitle => HasAnswer
        ? "正在生成回答"
        : HasTools ? "正在查阅本地 Wiki" : "正在分析问题";
    public bool IsSendVisible => !IsAnswering;
    public bool IsStopVisible => IsAnswering;
    public string ComposerPlaceholder => IsAnswering ? "AI 正在回答…" : "向 AI 提问…";
    public bool CanEditQuestion => _availability == ChatAvailabilityState.Ready && !IsAnswering;
    public bool IsQuestionReadOnly => !CanEditQuestion;
    public bool CanSend => _availability == ChatAvailabilityState.Ready
                           && !IsAnswering
                           && !string.IsNullOrWhiteSpace(Question);
    public bool CanRetry => !IsAnswering
                            && _availability is ChatAvailabilityState.Disabled or ChatAvailabilityState.Failed;
    public bool IsAiUnavailable => _availability is ChatAvailabilityState.Disabled or ChatAvailabilityState.Failed;
    public bool CanFavorite => _toggleFavorite is not null && RunState == ChatRunState.Completed && HasAnswer;

    public bool IsFavorite
    {
        get => _isFavorite;
        private set => SetProperty(ref _isFavorite, value);
    }

    public AsyncCommand SendCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand RetryCommand { get; }
    public RelayCommand OpenWikiCommand { get; }
    public AsyncCommand ToggleFavoriteCommand { get; }

    public bool LoadFavorite(ChatFavoriteRecord favorite)
    {
        ArgumentNullException.ThrowIfNull(favorite);
        if (IsAnswering)
            return false;

        _scheduledRender?.Cancel();
        _scheduledRender = null;
        _streamingMarkdown.Clear();
        Question = string.Empty;
        SubmittedQuestion = favorite.Question;
        AnswerDocument = _markdownParser.Parse(favorite.Markdown);
        UsageText = string.Empty;
        Tools.Clear();
        Sources.Clear();
        foreach (var source in favorite.Sources)
            Sources.Add(new ChatSourceViewModel(source.ToAgentSource(), _openSource));
        OnPropertyChanged(nameof(HasTools));
        OnPropertyChanged(nameof(HasSources));
        _favoriteId = favorite.Id;
        IsFavorite = true;
        RunState = ChatRunState.Completed;
        StatusMessage = "已打开收藏的单轮问答。";
        return true;
    }

    public void MarkFavoriteRemoved(Guid id)
    {
        if (_favoriteId == id)
            IsFavorite = false;
    }

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
        StatusMessage = "AI Runtime 不可用；离线 Wiki 仍可使用。";
        RefreshAvailabilityProperties();
    }

    public async Task SendAsync()
    {
        var runtime = _runtime;
        var question = Question.Trim();
        if (runtime is null || !CanSend || question.Length == 0)
            return;

        ResetForQuestion(question);
        Question = string.Empty;
        _answerCancellation = new CancellationTokenSource();
        var current = _answerCancellation;
        RunState = ChatRunState.Answering;
        StatusMessage = "正在查询本地 Wiki…";
        try
        {
            var answer = await runtime.AnswerAsync(question, ApplyRunEventAsync, current.Token);
            FlushMarkdown(answer.Markdown);
            Sources.Clear();
            foreach (var source in answer.Sources)
                Sources.Add(new ChatSourceViewModel(source, _openSource));
            OnPropertyChanged(nameof(HasSources));
            UsageText = FormatTokenSpend(answer.Usage);
            RunState = ChatRunState.Completed;
            StatusMessage = answer.Status == "answered"
                ? "回答完成。"
                : "本地证据不足，请尝试更明确的问题。";
        }
        catch (OperationCanceledException)
        {
            FlushScheduledMarkdown();
            RunState = ChatRunState.Cancelled;
            StatusMessage = "AI 回答已取消。";
        }
        catch (AiRuntimeException exception)
        {
            FlushScheduledMarkdown();
            RunState = ChatRunState.Failed;
            StatusMessage = exception.Message + " 离线 Wiki 仍可使用。";
        }
        catch
        {
            FlushScheduledMarkdown();
            RunState = ChatRunState.Failed;
            StatusMessage = "AI 回答失败；离线 Wiki 仍可使用。";
        }
        finally
        {
            if (ReferenceEquals(current, _answerCancellation))
            {
                _answerCancellation.Dispose();
                _answerCancellation = null;
            }
            RefreshCommands();
        }
    }

    public async Task StopAsync()
    {
        if (!IsAnswering)
            return;
        _answerCancellation?.Cancel();
        try
        {
            if (_runtime is not null)
                await _runtime.CancelAsync();
        }
        catch (AiRuntimeException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    public async Task RetryAsync()
    {
        if (_runtime is null || !CanRetry)
            return;
        StatusMessage = "正在重新启动 AI Runtime…";
        try
        {
            await _runtime.RetryAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "重试 AI 已取消。";
        }
        catch
        {
            StatusMessage = "AI Runtime 启动失败；离线 Wiki 仍可使用。";
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _answerCancellation?.Cancel();
        _answerCancellation?.Dispose();
        _scheduledRender?.Cancel();
        _scheduledRender?.Dispose();
        DetachRuntime();
    }

    private void ResetForQuestion(string question)
    {
        _scheduledRender?.Cancel();
        _scheduledRender?.Dispose();
        _scheduledRender = null;
        _streamingMarkdown.Clear();
        _lastRender = DateTimeOffset.MinValue;
        _favoriteId = null;
        IsFavorite = false;
        SubmittedQuestion = question;
        AnswerDocument = SafeMarkdownDocument.Empty;
        UsageText = string.Empty;
        Tools.Clear();
        Sources.Clear();
        OnPropertyChanged(nameof(HasTools));
        OnPropertyChanged(nameof(IsToolTraceVisible));
        OnPropertyChanged(nameof(ActivityTitle));
        OnPropertyChanged(nameof(HasSources));
    }

    private ValueTask ApplyRunEventAsync(AiRunEvent value)
    {
        if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            ApplyRunEvent(value);
            return ValueTask.CompletedTask;
        }
        return DispatchRunEventAsync(value);
    }

    private async ValueTask DispatchRunEventAsync(AiRunEvent value) =>
        await Dispatcher.UIThread.InvokeAsync(() => ApplyRunEvent(value));

    private void ApplyRunEvent(AiRunEvent value)
    {
        switch (value.Kind)
        {
            case AiRunEventKind.RunStarted:
                StatusMessage = "模型已开始分析。";
                break;
            case AiRunEventKind.ToolStarted:
                Tools.Add(new ChatToolProgressViewModel(
                    value.Name ?? "wiki_tool",
                    DisplayToolName(value.Name),
                    "正在执行…"));
                OnPropertyChanged(nameof(HasTools));
                OnPropertyChanged(nameof(IsToolTraceVisible));
                OnPropertyChanged(nameof(ActivityTitle));
                break;
            case AiRunEventKind.ToolFinished:
            {
                var item = Tools.LastOrDefault(tool => tool.Name == value.Name && tool.Status == "正在执行…");
                if (item is null)
                {
                    item = new ChatToolProgressViewModel(
                        value.Name ?? "wiki_tool",
                        DisplayToolName(value.Name),
                        DisplayToolStatus(value.Status));
                    Tools.Add(item);
                }
                else
                {
                    item.Status = DisplayToolStatus(value.Status);
                }
                OnPropertyChanged(nameof(HasTools));
                OnPropertyChanged(nameof(IsToolTraceVisible));
                OnPropertyChanged(nameof(ActivityTitle));
                break;
            }
            case AiRunEventKind.AnswerDelta:
                AppendMarkdown(value.Delta ?? string.Empty);
                break;
        }
    }

    private void AppendMarkdown(string delta)
    {
        if (delta.Length == 0)
            return;
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
        var scheduled = _scheduledRender;
        _ = ScheduleRenderAsync(RenderInterval - elapsed, scheduled);
    }

    private async Task ScheduleRenderAsync(TimeSpan delay, CancellationTokenSource scheduled)
    {
        try
        {
            await Task.Delay(delay, scheduled.Token);
            void Render()
            {
                if (ReferenceEquals(scheduled, _scheduledRender))
                {
                    _scheduledRender = null;
                    RenderStreamingMarkdown(DateTimeOffset.UtcNow);
                }
            }
            if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
                Render();
            else
                await Dispatcher.UIThread.InvokeAsync(Render);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            scheduled.Dispose();
        }
    }

    private void RenderStreamingMarkdown(DateTimeOffset now)
    {
        _lastRender = now;
        AnswerDocument = _markdownParser.Parse(_streamingMarkdown.ToString());
    }

    private void FlushMarkdown(string finalMarkdown)
    {
        _scheduledRender?.Cancel();
        _scheduledRender = null;
        _streamingMarkdown.Clear();
        _streamingMarkdown.Append(finalMarkdown);
        AnswerDocument = _markdownParser.Parse(finalMarkdown);
        _lastRender = DateTimeOffset.UtcNow;
    }

    private void FlushScheduledMarkdown()
    {
        _scheduledRender?.Cancel();
        _scheduledRender = null;
        if (_streamingMarkdown.Length > 0)
            AnswerDocument = _markdownParser.Parse(_streamingMarkdown.ToString());
    }

    private void OnRuntimeStatusChanged(object? sender, ChatAvailability availability)
    {
        if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
            ApplyAvailability(availability);
        else
            Dispatcher.UIThread.Post(() => ApplyAvailability(availability));
    }

    private void ApplyAvailability(ChatAvailability availability)
    {
        _availability = availability.State;
        if (!IsAnswering || availability.State is ChatAvailabilityState.Disabled or ChatAvailabilityState.Failed)
            StatusMessage = availability.Message;
        if (RunState == ChatRunState.Empty && availability.State == ChatAvailabilityState.Ready)
            RunState = ChatRunState.Ready;
        RefreshAvailabilityProperties();
    }

    private void RefreshAvailabilityProperties()
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(IsAiUnavailable));
        OnPropertyChanged(nameof(CanEditQuestion));
        OnPropertyChanged(nameof(IsQuestionReadOnly));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SendCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
    }

    internal async Task ToggleFavoriteAsync()
    {
        if (_toggleFavorite is null || !CanFavorite)
            return;
        var id = _favoriteId ?? Guid.NewGuid();
        var record = new ChatFavoriteRecord(
            id,
            SubmittedQuestion,
            AnswerDocument.Source,
            Sources.Select(source => ChatFavoriteSource.FromAgentSource(source.Source)).ToArray(),
            DateTimeOffset.Now);
        var favorite = await _toggleFavorite(record, IsFavorite);
        _favoriteId = id;
        IsFavorite = favorite;
        StatusMessage = favorite ? "已收藏该对话。" : "已取消收藏该对话。";
    }

    private static string DisplayToolName(string? name) => name switch
    {
        "search_wiki" => "搜索 Wiki",
        "read_wiki" => "读取页面",
        _ => "Wiki 工具",
    };

    private static string DisplayToolStatus(string? status) => status switch
    {
        "succeeded" => "完成",
        "not_found" => "未找到",
        _ => "失败",
    };

    private static string FormatTokenSpend(AiAgentUsage usage) =>
        $"输入 {usage.InputTokens} · 输出 {usage.OutputTokens} · 合计 {usage.TotalTokens} Token";
}
