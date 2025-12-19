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
}

/// <summary>
/// Implementation of IAppStateService using scoped caching per circuit.
/// This service is scoped, so a new instance is created per SignalR circuit.
/// </summary>
public class AppStateService : IAppStateService
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
    }
    
    public void InvalidateParentDashboard()
    {
        _parentDashboardCache = null;
        _logger.LogDebug("Parent dashboard cache invalidated");
    }
    
    public void InvalidateTrackerCache(string? userId = null, DateOnly? date = null)
    {
        if (userId == null && date == null)
        {
            _trackerCache.Clear();
            _logger.LogDebug("All tracker caches invalidated");
            return;
        }
        
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
    
    public void InvalidateAll()
    {
        _childDashboardCache.Clear();
        _parentDashboardCache = null;
        _trackerCache.Clear();
        _logger.LogDebug("All caches invalidated");
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
