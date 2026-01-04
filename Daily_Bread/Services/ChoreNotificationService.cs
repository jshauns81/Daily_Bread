using Daily_Bread.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Daily_Bread.Services;

/// <summary>
/// Service for broadcasting real-time notifications via SignalR.
/// Registered as Singleton to outlive individual circuits.
/// Uses IHubContext to send messages - never inject the Hub directly.
/// 
/// IMPORTANT: All SendAsync calls use individual parameters instead of anonymous objects
/// for reliable SignalR deserialization on the client side.
/// </summary>
public interface IChoreNotificationService
{
    /// <summary>
    /// Broadcasts that dashboard data has changed for affected users.
    /// Clients should invalidate their cached data and refresh.
    /// </summary>
    /// <param name="affectedUserIds">User IDs whose dashboards may have changed</param>
    Task NotifyDashboardChangedAsync(params string[] affectedUserIds);

    /// <summary>
    /// Broadcasts a help request alert to all connected clients (parents).
    /// This is a separate event for potential enhanced UX (toast, sound, etc.).
    /// </summary>
    /// <param name="choreLogId">The ChoreLog ID with the help request</param>
    /// <param name="requestingUserId">The child user ID who requested help</param>
    /// <param name="choreName">Name of the chore for display</param>
    /// <param name="childName">Name of the child for display</param>
    Task NotifyHelpRequestedAsync(int choreLogId, string requestingUserId, string choreName, string childName);

    /// <summary>
    /// Broadcasts that a chore was blessed (approved) - notifies the child who completed it.
    /// </summary>
    /// <param name="childUserId">The child user ID whose chore was blessed</param>
    /// <param name="choreName">Name of the chore</param>
    /// <param name="earnedAmount">Amount earned</param>
    /// <param name="parentName">Name of the parent who blessed it</param>
    Task NotifyBlessingGrantedAsync(string childUserId, string choreName, decimal earnedAmount, string? parentName);

    /// <summary>
    /// Broadcasts that a help request was responded to - notifies the child who requested help.
    /// </summary>
    /// <param name="childUserId">The child user ID who requested help</param>
    /// <param name="choreName">Name of the chore</param>
    /// <param name="response">The parent's response type</param>
    /// <param name="parentName">Name of the parent who responded</param>
    Task NotifyHelpRespondedAsync(string childUserId, string choreName, string response, string? parentName);
}

/// <summary>
/// SignalR notification service implementation.
/// All events are sent as individual parameters for reliable client deserialization.
/// </summary>
public class ChoreNotificationService : IChoreNotificationService
{
    private readonly IHubContext<ChoreHub> _hubContext;
    private readonly ILogger<ChoreNotificationService> _logger;

    public ChoreNotificationService(
        IHubContext<ChoreHub> hubContext,
        ILogger<ChoreNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyDashboardChangedAsync(params string[] affectedUserIds)
    {
        if (affectedUserIds.Length == 0)
        {
            _logger.LogDebug("NotifyDashboardChanged called with no affected users, skipping");
            return;
        }

        try
        {
            var timestamp = DateTime.UtcNow;
            
            // Send as individual parameters for reliable deserialization
            // Client: _hubConnection.On<string[], DateTime>("DashboardChanged", ...)
            await _hubContext.Clients.All.SendAsync("DashboardChanged", 
                affectedUserIds, 
                timestamp);

            _logger.LogInformation(
                "DashboardChanged broadcast sent for users: {UserIds}",
                string.Join(", ", affectedUserIds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast DashboardChanged");
        }
    }

    public async Task NotifyHelpRequestedAsync(
        int choreLogId,
        string requestingUserId,
        string choreName,
        string childName)
    {
        try
        {
            var timestamp = DateTime.UtcNow;
            
            // Send as individual parameters for reliable deserialization
            // Client: _hubConnection.On<int, string, string, string, DateTime>("HelpAlert", ...)
            await _hubContext.Clients.All.SendAsync("HelpAlert", 
                choreLogId, 
                requestingUserId, 
                choreName, 
                childName, 
                timestamp);

            _logger.LogInformation(
                "HelpAlert broadcast sent: ChoreLogId={ChoreLogId}, Child={ChildName}, Chore={ChoreName}",
                choreLogId, childName, choreName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast HelpAlert");
        }
    }

    public async Task NotifyBlessingGrantedAsync(
        string childUserId,
        string choreName,
        decimal earnedAmount,
        string? parentName)
    {
        if (string.IsNullOrEmpty(childUserId))
        {
            _logger.LogWarning("NotifyBlessingGranted called with null/empty childUserId, skipping");
            return;
        }

        try
        {
            var effectiveParentName = parentName ?? "Parent";
            var timestamp = DateTime.UtcNow;

            _logger.LogInformation(
                "Sending BlessingGranted: ChildUserId={ChildUserId}, ChoreName={ChoreName}, Amount={Amount}",
                childUserId, choreName, earnedAmount);

            // Send as individual parameters for reliable deserialization
            // Client: _hubConnection.On<string, string, decimal, string, DateTime>("BlessingGranted", ...)
            await _hubContext.Clients.All.SendAsync("BlessingGranted", 
                childUserId, 
                choreName, 
                earnedAmount, 
                effectiveParentName, 
                timestamp);

            _logger.LogInformation(
                "BlessingGranted broadcast sent successfully: Child={ChildUserId}, Chore={ChoreName}, Amount={Amount}",
                childUserId, choreName, earnedAmount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast BlessingGranted");
        }
    }

    public async Task NotifyHelpRespondedAsync(
        string childUserId,
        string choreName,
        string response,
        string? parentName)
    {
        if (string.IsNullOrEmpty(childUserId))
        {
            _logger.LogWarning("NotifyHelpResponded called with null/empty childUserId, skipping");
            return;
        }

        try
        {
            var effectiveParentName = parentName ?? "Parent";
            var timestamp = DateTime.UtcNow;

            _logger.LogInformation(
                "Sending HelpResponded: ChildUserId={ChildUserId}, ChoreName={ChoreName}, Response={Response}",
                childUserId, choreName, response);

            // Send as individual parameters for reliable deserialization
            // Client: _hubConnection.On<string, string, string, string, DateTime>("HelpResponded", ...)
            await _hubContext.Clients.All.SendAsync("HelpResponded", 
                childUserId, 
                choreName, 
                response, 
                effectiveParentName, 
                timestamp);

            _logger.LogInformation(
                "HelpResponded broadcast sent successfully: Child={ChildUserId}, Chore={ChoreName}, Response={Response}",
                childUserId, choreName, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast HelpResponded");
        }
    }
}
