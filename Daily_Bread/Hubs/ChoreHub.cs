using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Daily_Bread.Hubs;

/// <summary>
/// SignalR hub for real-time chore notifications across family members.
/// This hub is intentionally minimal - it just establishes the endpoint.
/// Server-side delivery is done via IHubContext&lt;ChoreHub&gt; in ChoreNotificationService.
/// </summary>
[Authorize]
public class ChoreHub : Hub
{
    private readonly ILogger<ChoreHub> _logger;
    public const string ParentsGroup = "parents";

    
    // Track connected clients for diagnostics
    private static int _connectedClients;

    public ChoreHub(ILogger<ChoreHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        if (string.IsNullOrEmpty(Context.UserIdentifier))
        {
            throw new HubException("Authenticated connection has no user identifier.");
        }

        if (Context.User?.IsInRole("Parent") == true || Context.User?.IsInRole("Admin") == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ParentsGroup);
        }

        var count = Interlocked.Increment(ref _connectedClients);
        _logger.LogDebug("ChoreHub: Client connected: {ConnectionId}. Total clients: {Count}",
            Context.ConnectionId, count);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var count = Interlocked.Decrement(ref _connectedClients);
        _logger.LogDebug(
            "ChoreHub: Client disconnected: {ConnectionId}, Exception: {Exception}. Total clients: {Count}",
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
