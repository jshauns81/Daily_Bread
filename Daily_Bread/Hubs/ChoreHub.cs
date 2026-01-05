using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Daily_Bread.Hubs;

/// <summary>
/// SignalR hub for real-time chore notifications across family members.
/// This hub is intentionally minimal - it just establishes the endpoint.
/// Server-side broadcasting is done via IHubContext<ChoreHub> in ChoreNotificationService.
/// 
/// Note: [AllowAnonymous] is safe here because each family runs in their own
/// isolated Docker container. Everyone connected to this hub is in the same
/// household. The Blazor app itself still requires authentication.
/// </summary>
[AllowAnonymous]
public class ChoreHub : Hub
{
    private readonly ILogger<ChoreHub> _logger;
    
    // Track connected clients for diagnostics
    private static int _connectedClients = 0;

    public ChoreHub(ILogger<ChoreHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var count = Interlocked.Increment(ref _connectedClients);
        _logger.LogWarning(">>> ChoreHub: Client connected: {ConnectionId}. Total clients: {Count}", 
            Context.ConnectionId, count);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var count = Interlocked.Decrement(ref _connectedClients);
        _logger.LogWarning(
            ">>> ChoreHub: Client disconnected: {ConnectionId}, Exception: {Exception}. Total clients: {Count}",
            Context.ConnectionId,
            exception?.Message ?? "None",
            count);
        await base.OnDisconnectedAsync(exception);
    }
    
    /// <summary>
    /// Gets the current count of connected clients (for diagnostics).
    /// </summary>
    public static int ConnectedClientCount => _connectedClients;
}
