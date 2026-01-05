using System.Collections.Concurrent;

namespace Daily_Bread.Services;

/// <summary>
/// Scoped state container that caches user data per circuit.
/// Prevents duplicate queries during prerender → interactive handoff.
/// </summary>
public interface IAppStateService
{
    /// <summary>
    /// Gets the child dashboard data, loading it if not cached.
    /// Data is cached per circuit and invalidated on mutation.
    /// </summary>
    Task<ChildDashboardData?> GetChildDashboardAsync(string userId, bool forceRefresh = false);
    
    /// <summary>
    /// Gets the parent dashboard data, loading it if not cached.
    /// </summary>
    Task<ParentDashboardData?> GetParentDashboardAsync(bool forceRefresh = false);
    
    /// <summary>
    /// Gets tracker items for a user on a specific date, using cache when available.
    /// </summary>
    Task<List<TrackerChoreItem>> GetTrackerItemsAsync(string userId, DateOnly date, bool forceRefresh = false);
    
    /// <summary>
    /// Invalidates cached dashboard data after a mutation (chore completion, etc.).
    /// Returns immediately, re-fetching happens on next access.
    /// </summary>
    void InvalidateChildDashboard(string? userId = null);
    
    /// <summary>
    /// Invalidates cached parent dashboard data.
    /// </summary>
    void InvalidateParentDashboard();
    
    /// <summary>
    /// Invalidates tracker cache for a specific date.
    /// </summary>
    void InvalidateTrackerCache(string? userId = null, DateOnly? date = null);
    
    /// <summary>
    /// Invalidates all cached data for the current circuit.
    /// </summary>
    void InvalidateAll();
    
    /// <summary>
    /// Whether the child dashboard has been loaded (prevents duplicate loads).
    /// </summary>
    bool IsChildDashboardLoaded(string userId);
    
    /// <summary>
    /// Whether the parent dashboard has been loaded.
    /// </summary>
    bool IsParentDashboardLoaded { get; }
    
    /// <summary>
    /// Gets the pending approvals count from cached parent dashboard data.
    /// Returns 0 if no data is cached (does NOT query database).
    /// </summary>
    int CachedPendingApprovalsCount { get; }
    
    #region State Change Events
    
    /// <summary>
    /// Event fired when parent dashboard data changes (approvals, etc.).
    /// Components like NavMenu can subscribe to refresh their state.
    /// </summary>
    event Action? OnParentDashboardChanged;
    
    /// <summary>
    /// Event fired when child dashboard data changes (chore completion, etc.).
    /// </summary>
    event Action? OnChildDashboardChanged;
    
    /// <summary>
    /// Event fired when tracker data changes (chore status updates).
    /// </summary>
    event Action? OnTrackerChanged;
    
    /// <summary>
    /// Event fired when any state changes. Components can subscribe to this
    /// for a catch-all refresh trigger instead of individual events.
    /// </summary>
    event Action? OnStateChanged;
    
    #endregion
    
    #region Help Request Modal State
    
    /// <summary>
    /// Event fired when a help request should be opened (e.g., from toast click, push notification).
    /// Subscribers (like MainLayout) should show the HelpResponseModal.
    /// </summary>
    event Action<int>? OnHelpRequestOpened;
    
    /// <summary>
    /// The currently open help request ChoreLogId, or null if none.
    /// </summary>
    int? CurrentHelpRequestId { get; }
    
    /// <summary>
    /// Opens the help response modal for a specific chore log.
    /// Triggers OnHelpRequestOpened event.
    /// </summary>
    /// <param name="choreLogId">The ChoreLog ID with the help request</param>
    void OpenHelpRequest(int choreLogId);
    
    /// <summary>
    /// Closes the help response modal and clears the current help request.
    /// </summary>
    void CloseHelpRequest();
    
    #endregion
}

/// <summary>
/// Implementation of IAppStateService using scoped caching per circuit.
/// This service is scoped, so a new instance is created per SignalR circuit.
/// </summary>
public class AppStateService : IAppStateService, IDisposable
{
    private readonly IDashboardService _dashboardService;
    private readonly ITrackerService _trackerService;
    private readonly ILogger<AppStateService> _logger;
    
    // Cache structures - per circuit (scoped service lifetime)
    private readonly ConcurrentDictionary<string, ChildDashboardCacheEntry> _childDashboardCache = new();
    private ParentDashboardCacheEntry? _parentDashboardCache;
    private readonly ConcurrentDictionary<string, TrackerCacheEntry> _trackerCache = new();
    
    // Cache TTL - short lived to ensure freshness
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    
    // Lock objects for thread safety during loads
    private readonly SemaphoreSlim _childLoadLock = new(1, 1);
    private readonly SemaphoreSlim _parentLoadLock = new(1, 1);
    private readonly SemaphoreSlim _trackerLoadLock = new(1, 1);
    
    // Debounce support - separate CTS for each event type
    private CancellationTokenSource? _childDashboardDebounceCts;
    private CancellationTokenSource? _parentDashboardDebounceCts;
    private CancellationTokenSource? _trackerDebounceCts;
    private CancellationTokenSource? _stateChangedDebounceCts;
    private readonly object _debounceLock = new();
    private const int DebounceDelayMs = 500;
    
    // Help request modal state
    private int? _currentHelpRequestId;
    
    // Track if disposed
    private bool _disposed;
    
    #region State Change Events
    
    /// <inheritdoc />
    public event Action? OnParentDashboardChanged;
    
    /// <inheritdoc />
    public event Action? OnChildDashboardChanged;
    
    /// <inheritdoc />
    public event Action? OnTrackerChanged;
    
    /// <inheritdoc />
    public event Action? OnStateChanged;
    
    /// <summary>
    /// Fires the child dashboard changed event with debouncing.
    /// Multiple calls within DebounceDelayMs will be coalesced into one.
    /// </summary>
    private void NotifyChildDashboardChangedDebounced()
    {
        _logger.LogWarning(">>> Debounce triggered for ChildDashboard - scheduling {DelayMs}ms delay", DebounceDelayMs);
        
        lock (_debounceLock)
        {
            _childDashboardDebounceCts?.Cancel();
            _childDashboardDebounceCts?.Dispose();
            _childDashboardDebounceCts = new CancellationTokenSource();
            var token = _childDashboardDebounceCts.Token;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceDelayMs, token);
                    if (!token.IsCancellationRequested)
                    {
                        _logger.LogWarning(">>> Debounce FIRING OnChildDashboardChanged after {DelayMs}ms delay", DebounceDelayMs);
                        OnChildDashboardChanged?.Invoke();
                    }
                    else
                    {
                        _logger.LogWarning(">>> Debounce CANCELLED for ChildDashboard (superseded by newer call)");
                    }
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning(">>> Debounce CANCELLED for ChildDashboard (TaskCanceledException)");
                }
            });
        }
    }
    
    /// <summary>
    /// Fires the parent dashboard changed event with debouncing.
    /// Multiple calls within DebounceDelayMs will be coalesced into one.
    /// </summary>
    private void NotifyParentDashboardChangedDebounced()
    {
        _logger.LogWarning(">>> Debounce triggered for ParentDashboard - scheduling {DelayMs}ms delay", DebounceDelayMs);
        
        lock (_debounceLock)
        {
            _parentDashboardDebounceCts?.Cancel();
            _parentDashboardDebounceCts?.Dispose();
            _parentDashboardDebounceCts = new CancellationTokenSource();
            var token = _parentDashboardDebounceCts.Token;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceDelayMs, token);
                    if (!token.IsCancellationRequested)
                    {
                        _logger.LogWarning(">>> Debounce FIRING OnParentDashboardChanged after {DelayMs}ms delay", DebounceDelayMs);
                        OnParentDashboardChanged?.Invoke();
                    }
                    else
                    {
                        _logger.LogWarning(">>> Debounce CANCELLED for ParentDashboard (superseded by newer call)");
                    }
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning(">>> Debounce CANCELLED for ParentDashboard (TaskCanceledException)");
                }
            });
        }
    }
    
    /// <summary>
    /// Fires the tracker changed event with debouncing.
    /// Multiple calls within DebounceDelayMs will be coalesced into one.
    /// </summary>
    private void NotifyTrackerChangedDebounced()
    {
        _logger.LogWarning(">>> Debounce triggered for Tracker - scheduling {DelayMs}ms delay", DebounceDelayMs);
        
        lock (_debounceLock)
        {
            _trackerDebounceCts?.Cancel();
            _trackerDebounceCts?.Dispose();
            _trackerDebounceCts = new CancellationTokenSource();
            var token = _trackerDebounceCts.Token;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceDelayMs, token);
                    if (!token.IsCancellationRequested)
                    {
                        _logger.LogWarning(">>> Debounce FIRING OnTrackerChanged after {DelayMs}ms delay", DebounceDelayMs);
                        OnTrackerChanged?.Invoke();
                    }
                    else
                    {
                        _logger.LogWarning(">>> Debounce CANCELLED for Tracker (superseded by newer call)");
                    }
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning(">>> Debounce CANCELLED for Tracker (TaskCanceledException)");
                }
            });
        }
    }
    
    /// <summary>
    /// Notifies all subscribers that state has changed (debounced).
    /// Called at the end of every invalidation method.
    /// </summary>
    private void NotifyStateChangedDebounced()
    {
        lock (_debounceLock)
        {
            _stateChangedDebounceCts?.Cancel();
            _stateChangedDebounceCts?.Dispose();
            _stateChangedDebounceCts = new CancellationTokenSource();
            var token = _stateChangedDebounceCts.Token;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceDelayMs, token);
                    if (!token.IsCancellationRequested)
                    {
                        _logger.LogDebug("Debounced: Firing OnStateChanged");
                        OnStateChanged?.Invoke();
                    }
                }
                catch (TaskCanceledException)
                {
                    // Another invalidation came in, this one was cancelled - expected
                }
            });
        }
    }
    
    #endregion
    
    #region Help Request Modal State
    
    /// <inheritdoc />
    public event Action<int>? OnHelpRequestOpened;
    
    /// <inheritdoc />
    public int? CurrentHelpRequestId => _currentHelpRequestId;
    
    /// <inheritdoc />
    public void OpenHelpRequest(int choreLogId)
    {
        _currentHelpRequestId = choreLogId;
        _logger.LogDebug("Opening help request modal for ChoreLogId={ChoreLogId}", choreLogId);
        OnHelpRequestOpened?.Invoke(choreLogId);
    }
    
    /// <inheritdoc />
    public void CloseHelpRequest()
    {
        var previousId = _currentHelpRequestId;
        _currentHelpRequestId = null;
        _logger.LogDebug("Closed help request modal (was ChoreLogId={ChoreLogId})", previousId);
    }
    
    #endregion
    
    public AppStateService(
        IDashboardService dashboardService,
        ITrackerService trackerService,
        ILogger<AppStateService> logger)
    {
        _dashboardService = dashboardService;
        _trackerService = trackerService;
        _logger = logger;
    }
    
    public bool IsChildDashboardLoaded(string userId)
    {
        return _childDashboardCache.TryGetValue(userId, out var entry) 
            && entry.Data != null 
            && !entry.IsExpired;
    }
    
    public bool IsParentDashboardLoaded => 
        _parentDashboardCache?.Data != null && !_parentDashboardCache.IsExpired;
    
    /// <summary>
    /// Gets the pending approvals count from cached parent dashboard data.
    /// Returns 0 if no data is cached (does NOT query database).
    /// </summary>
    public int CachedPendingApprovalsCount => 
        _parentDashboardCache?.Data?.PendingApprovals.Count ?? 0;
    
    public async Task<ChildDashboardData?> GetChildDashboardAsync(string userId, bool forceRefresh = false)
    {
        // Check cache first (fast path - no lock needed for read)
        if (!forceRefresh && _childDashboardCache.TryGetValue(userId, out var entry) && !entry.IsExpired)
        {
            _logger.LogDebug("Child dashboard cache HIT for user {UserId}", userId);
            return entry.Data;
        }
        
        // Need to load - use lock to prevent duplicate loads during race conditions
        await _childLoadLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock (another thread may have loaded)
            if (!forceRefresh && _childDashboardCache.TryGetValue(userId, out entry) && !entry.IsExpired)
            {
                return entry.Data;
            }
            
            _logger.LogDebug("Loading child dashboard for user {UserId}", userId);
            
            var data = await _dashboardService.GetChildDashboardAsync(userId);
            
            var newEntry = new ChildDashboardCacheEntry
            {
                Data = data,
                LoadedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(CacheTtl)
            };
            
            _childDashboardCache[userId] = newEntry;
            
            _logger.LogDebug("Child dashboard cached for user {UserId}", userId);
            
            return data;
        }
        finally
        {
            _childLoadLock.Release();
        }
    }
    
    public async Task<ParentDashboardData?> GetParentDashboardAsync(bool forceRefresh = false)
    {
        // Check cache first
        if (!forceRefresh && _parentDashboardCache != null && !_parentDashboardCache.IsExpired)
        {
            _logger.LogDebug("Parent dashboard cache HIT");
            return _parentDashboardCache.Data;
        }
        
        await _parentLoadLock.WaitAsync();
        try
        {
            // Double-check after lock
            if (!forceRefresh && _parentDashboardCache != null && !_parentDashboardCache.IsExpired)
            {
                return _parentDashboardCache.Data;
            }
            
            _logger.LogDebug("Loading parent dashboard");
            
            var data = await _dashboardService.GetParentDashboardAsync();
            
            _parentDashboardCache = new ParentDashboardCacheEntry
            {
                Data = data,
                LoadedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(CacheTtl)
            };
            
            return data;
        }
        finally
        {
            _parentLoadLock.Release();
        }
    }
    
    public async Task<List<TrackerChoreItem>> GetTrackerItemsAsync(string userId, DateOnly date, bool forceRefresh = false)
    {
        var cacheKey = $"{userId}:{date:yyyy-MM-dd}";
        
        // Check cache first
        if (!forceRefresh && _trackerCache.TryGetValue(cacheKey, out var entry) && !entry.IsExpired)
        {
            _logger.LogDebug("Tracker cache HIT for {CacheKey}", cacheKey);
            return entry.Items;
        }
        
        await _trackerLoadLock.WaitAsync();
        try
        {
            // Double-check after lock
            if (!forceRefresh && _trackerCache.TryGetValue(cacheKey, out entry) && !entry.IsExpired)
            {
                return entry.Items;
            }
            
            _logger.LogDebug("Loading tracker items for {CacheKey}", cacheKey);
            
            var items = await _trackerService.GetTrackerItemsForUserOnDateAsync(userId, date);
            
            var newEntry = new TrackerCacheEntry
            {
                Items = items,
                LoadedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(CacheTtl)
            };
            
            _trackerCache[cacheKey] = newEntry;
            
            return items;
        }
        finally
        {
            _trackerLoadLock.Release();
        }
    }
    
    public void InvalidateChildDashboard(string? userId = null)
    {
        if (userId != null)
        {
            _childDashboardCache.TryRemove(userId, out _);
            _logger.LogDebug("Child dashboard cache invalidated for user {UserId}", userId);
        }
        else
        {
            _childDashboardCache.Clear();
            _logger.LogDebug("All child dashboard caches invalidated");
        }
        
        // Notify subscribers (debounced)
        NotifyChildDashboardChangedDebounced();
        NotifyStateChangedDebounced();
    }
    
    public void InvalidateParentDashboard()
    {
        _parentDashboardCache = null;
        _logger.LogDebug("Parent dashboard cache invalidated");
        
        // Notify subscribers (debounced)
        NotifyParentDashboardChangedDebounced();
        NotifyStateChangedDebounced();
    }
    
    public void InvalidateTrackerCache(string? userId = null, DateOnly? date = null)
    {
        if (userId == null && date == null)
        {
            _trackerCache.Clear();
            _logger.LogDebug("All tracker caches invalidated");
        }
        else
        {
            // Remove matching entries
            var keysToRemove = _trackerCache.Keys
                .Where(k =>
                {
                    var parts = k.Split(':');
                    if (parts.Length != 2) return false;
                    
                    var matchUserId = userId == null || parts[0] == userId;
                    var matchDate = date == null || parts[1] == date.Value.ToString("yyyy-MM-dd");
                    
                    return matchUserId && matchDate;
                })
                .ToList();
            
            foreach (var key in keysToRemove)
            {
                _trackerCache.TryRemove(key, out _);
            }
            
            _logger.LogDebug("Tracker cache invalidated for userId={UserId}, date={Date}, removed {Count} entries", 
                userId, date, keysToRemove.Count);
        }
        
        // Notify subscribers (debounced)
        NotifyTrackerChangedDebounced();
        NotifyStateChangedDebounced();
    }
    
    public void InvalidateAll()
    {
        _childDashboardCache.Clear();
        _parentDashboardCache = null;
        _trackerCache.Clear();
        _logger.LogDebug("All caches invalidated");
        
        // Notify all specific event subscribers (debounced)
        NotifyParentDashboardChangedDebounced();
        NotifyChildDashboardChangedDebounced();
        NotifyTrackerChangedDebounced();
        
        // Notify generic state changed subscribers (debounced)
        NotifyStateChangedDebounced();
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        // Dispose all debounce CTS
        _childDashboardDebounceCts?.Cancel();
        _childDashboardDebounceCts?.Dispose();
        _parentDashboardDebounceCts?.Cancel();
        _parentDashboardDebounceCts?.Dispose();
        _trackerDebounceCts?.Cancel();
        _trackerDebounceCts?.Dispose();
        _stateChangedDebounceCts?.Cancel();
        _stateChangedDebounceCts?.Dispose();
        
        // Dispose semaphores
        _childLoadLock.Dispose();
        _parentLoadLock.Dispose();
        _trackerLoadLock.Dispose();
    }
    
    // Cache entry structures
    private class ChildDashboardCacheEntry
    {
        public ChildDashboardData? Data { get; init; }
        public DateTime LoadedAt { get; init; }
        public DateTime ExpiresAt { get; init; }
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }
    
    private class ParentDashboardCacheEntry
    {
        public ParentDashboardData? Data { get; init; }
        public DateTime LoadedAt { get; init; }
        public DateTime ExpiresAt { get; init; }
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }
    
    private class TrackerCacheEntry
    {
        public List<TrackerChoreItem> Items { get; init; } = [];
        public DateTime LoadedAt { get; init; }
        public DateTime ExpiresAt { get; init; }
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }
}
