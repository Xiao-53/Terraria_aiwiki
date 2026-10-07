namespace Terraria_Wiki.Windows.Presentation.Infrastructure;

// Presentation-only lifetime shared by the former temporary and persistent status bars.
internal sealed class StatusNotification : ObservableObject, IDisposable
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationTokenSource? _expiry;
    private bool _pageActive = true;
    private bool _busy;
    private bool _hasMessage;
    private bool _isVisible;
    private double _opacity = 1;
    private bool _disposed;

    public StatusNotification(Func<TimeSpan, CancellationToken, Task>? delay = null)
        => _delay = delay ?? ((duration, token) => Task.Delay(duration, token));

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public double Opacity
    {
        get => _opacity;
        private set => SetProperty(ref _opacity, value);
    }

    public void Show(bool hasMessage)
    {
        if (_disposed) return;
        CancelExpiry();
        _hasMessage = hasMessage;
        Opacity = 1;
        IsVisible = _pageActive && hasMessage;
        StartExpiry();
    }

    public void SetBusy(bool busy)
    {
        if (_disposed || _busy == busy) return;
        _busy = busy;
        if (busy)
        {
            CancelExpiry();
            Opacity = 1;
            IsVisible = _pageActive && _hasMessage;
        }
        else StartExpiry();
    }

    public void SetPageActive(bool active)
    {
        if (_disposed || _pageActive == active) return;
        _pageActive = active;
        if (!active)
        {
            CancelExpiry();
            IsVisible = false;
        }
        else if (_busy && _hasMessage)
        {
            Opacity = 1;
            IsVisible = true;
        }
    }

    public void Reset()
    {
        CancelExpiry();
        _hasMessage = false;
        IsVisible = false;
    }

    private void StartExpiry()
    {
        if (_disposed || _busy || !IsVisible || _expiry is not null) return;
        var current = new CancellationTokenSource();
        _expiry = current;
        _ = ExpireAsync(current);
    }

    private async Task ExpireAsync(CancellationTokenSource current)
    {
        var token = current.Token;
        try
        {
            await _delay(TimeSpan.FromSeconds(3), token);
            if (token.IsCancellationRequested || !ReferenceEquals(current, _expiry)) return;
            Opacity = 0;
            await _delay(TimeSpan.FromMilliseconds(180), token);
            if (token.IsCancellationRequested || !ReferenceEquals(current, _expiry)) return;
            IsVisible = false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(current, _expiry))
            {
                _expiry = null;
                current.Dispose();
            }
        }
    }

    private void CancelExpiry()
    {
        var old = _expiry;
        _expiry = null;
        old?.Cancel();
        old?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
    }
}
