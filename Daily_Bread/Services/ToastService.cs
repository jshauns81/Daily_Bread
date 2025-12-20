namespace Daily_Bread.Services;

/// <summary>
/// Types of toast notifications.
/// </summary>
public enum ToastType
{
    Success,
    Error,
    Warning,
    Info
}

/// <summary>
/// Represents a toast notification message.
/// </summary>
public class ToastMessage
{
    public Guid Id { get; } = Guid.NewGuid();
    public required string Title { get; init; }
    public string? Message { get; init; }
    public ToastType Type { get; init; } = ToastType.Info;
    public int DurationMs { get; init; } = 4000;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public bool IsVisible { get; set; } = true;
    
    /// <summary>
    /// Icon based on toast type.
    /// </summary>
    public string Icon => Type switch
    {
        ToastType.Success => "?",
        ToastType.Error => "?",
        ToastType.Warning => "?",
        ToastType.Info => "?",
        _ => "?"
    };
    
    /// <summary>
    /// CSS class based on toast type.
    /// </summary>
    public string CssClass => Type switch
    {
        ToastType.Success => "toast-success",
        ToastType.Error => "toast-error",
        ToastType.Warning => "toast-warning",
        ToastType.Info => "toast-info",
        _ => "toast-info"
    };
}

/// <summary>
/// Service for managing toast notifications.
/// </summary>
public interface IToastService
{
    /// <summary>
    /// Event fired when toasts change.
    /// </summary>
    event Action? OnChange;
    
    /// <summary>
    /// Gets all active toasts.
    /// </summary>
    IReadOnlyList<ToastMessage> Toasts { get; }
    
    /// <summary>
    /// Shows a success toast.
    /// </summary>
    void ShowSuccess(string title, string? message = null, int durationMs = 4000);
    
    /// <summary>
    /// Shows an error toast.
    /// </summary>
    void ShowError(string title, string? message = null, int durationMs = 6000);
    
    /// <summary>
    /// Shows a warning toast.
    /// </summary>
    void ShowWarning(string title, string? message = null, int durationMs = 5000);
    
    /// <summary>
    /// Shows an info toast.
    /// </summary>
    void ShowInfo(string title, string? message = null, int durationMs = 4000);
    
    /// <summary>
    /// Shows a custom toast.
    /// </summary>
    void Show(ToastMessage toast);
    
    /// <summary>
    /// Removes a specific toast.
    /// </summary>
    void Remove(Guid toastId);
    
    /// <summary>
    /// Clears all toasts.
    /// </summary>
    void Clear();
}

/// <summary>
/// Implementation of toast notification service.
/// </summary>
public class ToastService : IToastService, IDisposable
{
    private readonly List<ToastMessage> _toasts = [];
    private readonly object _lock = new();
    
    public event Action? OnChange;
    
    public IReadOnlyList<ToastMessage> Toasts
    {
        get
        {
            lock (_lock)
            {
                return _toasts.ToList().AsReadOnly();
            }
        }
    }
    
    public void ShowSuccess(string title, string? message = null, int durationMs = 4000)
    {
        Show(new ToastMessage
        {
            Title = title,
            Message = message,
            Type = ToastType.Success,
            DurationMs = durationMs
        });
    }
    
    public void ShowError(string title, string? message = null, int durationMs = 6000)
    {
        Show(new ToastMessage
        {
            Title = title,
            Message = message,
            Type = ToastType.Error,
            DurationMs = durationMs
        });
    }
    
    public void ShowWarning(string title, string? message = null, int durationMs = 5000)
    {
        Show(new ToastMessage
        {
            Title = title,
            Message = message,
            Type = ToastType.Warning,
            DurationMs = durationMs
        });
    }
    
    public void ShowInfo(string title, string? message = null, int durationMs = 4000)
    {
        Show(new ToastMessage
        {
            Title = title,
            Message = message,
            Type = ToastType.Info,
            DurationMs = durationMs
        });
    }
    
    public void Show(ToastMessage toast)
    {
        lock (_lock)
        {
            _toasts.Add(toast);
            
            // Limit to 5 toasts max
            while (_toasts.Count > 5)
            {
                _toasts.RemoveAt(0);
            }
        }
        
        OnChange?.Invoke();
        
        // Auto-remove after duration
        if (toast.DurationMs > 0)
        {
            _ = RemoveAfterDelayAsync(toast.Id, toast.DurationMs);
        }
    }
    
    public void Remove(Guid toastId)
    {
        lock (_lock)
        {
            var toast = _toasts.FirstOrDefault(t => t.Id == toastId);
            if (toast != null)
            {
                toast.IsVisible = false;
                _toasts.Remove(toast);
            }
        }
        
        OnChange?.Invoke();
    }
    
    public void Clear()
    {
        lock (_lock)
        {
            _toasts.Clear();
        }
        
        OnChange?.Invoke();
    }
    
    private async Task RemoveAfterDelayAsync(Guid toastId, int delayMs)
    {
        await Task.Delay(delayMs);
        Remove(toastId);
    }
    
    public void Dispose()
    {
        Clear();
    }
}
