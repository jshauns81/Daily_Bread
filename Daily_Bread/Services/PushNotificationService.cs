using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebPush;

namespace Daily_Bread.Services;

// Alias to avoid ambiguity with WebPush.PushSubscription
using DbPushSubscription = Daily_Bread.Data.Models.PushSubscription;

/// <summary>
/// DTO for push subscription from the browser.
/// </summary>
public class PushSubscriptionDto
{
    public required string Endpoint { get; set; }
    public required PushSubscriptionKeys Keys { get; set; }
}

public class PushSubscriptionKeys
{
    public required string P256dh { get; set; }
    public required string Auth { get; set; }
}

/// <summary>
/// Notification payload for push messages.
/// </summary>
public class PushNotificationPayload
{
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string? Icon { get; set; }
    public string? Badge { get; set; }
    public string? Url { get; set; }
    public string? Tag { get; set; }
    public Dictionary<string, object>? Data { get; set; }
}

/// <summary>
/// Service for managing Web Push notifications.
/// </summary>
public interface IPushNotificationService
{
    /// <summary>
    /// Gets or generates VAPID keys for push notifications.
    /// </summary>
    Task<(string PublicKey, string PrivateKey)> GetOrCreateVapidKeysAsync();
    
    /// <summary>
    /// Gets the public VAPID key for client-side subscription.
    /// </summary>
    Task<string> GetPublicVapidKeyAsync();
    
    /// <summary>
    /// Subscribes a user's device to push notifications.
    /// </summary>
    Task<ServiceResult> SubscribeAsync(string userId, PushSubscriptionDto subscription, string? deviceName = null, string? userAgent = null);
    
    /// <summary>
    /// Unsubscribes a user's device from push notifications.
    /// </summary>
    Task<ServiceResult> UnsubscribeAsync(string userId, string endpoint);
    
    /// <summary>
    /// Gets all active subscriptions for a user.
    /// </summary>
    Task<List<DbPushSubscription>> GetUserSubscriptionsAsync(string userId);
    
    /// <summary>
    /// Sends a push notification to a specific user (all their devices).
    /// </summary>
    Task<ServiceResult> SendToUserAsync(string userId, PushNotificationPayload payload);
    
    /// <summary>
    /// Sends a push notification to all users with a specific role.
    /// </summary>
    Task<ServiceResult> SendToRoleAsync(string role, PushNotificationPayload payload);
    
    /// <summary>
    /// Sends a push notification to all parents.
    /// </summary>
    Task<ServiceResult> NotifyParentsAsync(PushNotificationPayload payload);
    
    /// <summary>
    /// Sends a help request notification to all parents.
    /// Includes choreLogId for deep linking to the help response modal.
    /// </summary>
    Task<ServiceResult> SendHelpRequestNotificationAsync(int choreLogId, string childName, string choreName, string? reason);
}

public class PushNotificationService : IPushNotificationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IFamilySettingsService _familySettingsService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PushNotificationService> _logger;
    private readonly IConfiguration _configuration;

    public PushNotificationService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IFamilySettingsService familySettingsService,
        UserManager<ApplicationUser> userManager,
        ILogger<PushNotificationService> logger,
        IConfiguration configuration)
    {
        _contextFactory = contextFactory;
        _familySettingsService = familySettingsService;
        _userManager = userManager;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<(string PublicKey, string PrivateKey)> GetOrCreateVapidKeysAsync()
    {
        var settings = await _familySettingsService.GetSettingsAsync();
        
        if (!string.IsNullOrEmpty(settings.VapidPublicKey) && !string.IsNullOrEmpty(settings.VapidPrivateKey))
        {
            return (settings.VapidPublicKey, settings.VapidPrivateKey);
        }
        
        // Generate new VAPID keys
        var vapidKeys = VapidHelper.GenerateVapidKeys();
        
        // Save to settings
        await using var context = await _contextFactory.CreateDbContextAsync();
        var settingsEntity = await context.FamilySettings.FirstOrDefaultAsync();
        
        if (settingsEntity == null)
        {
            settingsEntity = new FamilySettings();
            context.FamilySettings.Add(settingsEntity);
        }
        
        settingsEntity.VapidPublicKey = vapidKeys.PublicKey;
        settingsEntity.VapidPrivateKey = vapidKeys.PrivateKey;
        settingsEntity.VapidSubject = _configuration["Vapid:Subject"] ?? "mailto:admin@dailybread.app";
        settingsEntity.ModifiedAt = DateTime.UtcNow;
        
        await context.SaveChangesAsync();
        
        _logger.LogInformation("Generated new VAPID keys for push notifications");
        
        return (vapidKeys.PublicKey, vapidKeys.PrivateKey);
    }

    public async Task<string> GetPublicVapidKeyAsync()
    {
        var (publicKey, _) = await GetOrCreateVapidKeysAsync();
        return publicKey;
    }

    public async Task<ServiceResult> SubscribeAsync(string userId, PushSubscriptionDto subscription, string? deviceName = null, string? userAgent = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Check if subscription already exists
        var existing = await context.PushSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == subscription.Endpoint);
        
        if (existing != null)
        {
            // Update existing subscription
            existing.P256dh = subscription.Keys.P256dh;
            existing.Auth = subscription.Keys.Auth;
            existing.IsActive = true;
            existing.FailedAttempts = 0;
            existing.DeviceName = deviceName ?? existing.DeviceName;
            existing.UserAgent = userAgent ?? existing.UserAgent;
        }
        else
        {
            // Create new subscription
            var newSubscription = new DbPushSubscription
            {
                UserId = userId,
                Endpoint = subscription.Endpoint,
                P256dh = subscription.Keys.P256dh,
                Auth = subscription.Keys.Auth,
                DeviceName = deviceName,
                UserAgent = userAgent,
                IsActive = true
            };
            context.PushSubscriptions.Add(newSubscription);
        }
        
        await context.SaveChangesAsync();
        
        _logger.LogInformation("Push subscription saved for user {UserId}", userId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UnsubscribeAsync(string userId, string endpoint)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var subscription = await context.PushSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == endpoint);
        
        if (subscription != null)
        {
            context.PushSubscriptions.Remove(subscription);
            await context.SaveChangesAsync();
            _logger.LogInformation("Push subscription removed for user {UserId}", userId);
        }
        
        return ServiceResult.Ok();
    }

    public async Task<List<DbPushSubscription>> GetUserSubscriptionsAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        return await context.PushSubscriptions
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync();
    }

    public async Task<ServiceResult> SendToUserAsync(string userId, PushNotificationPayload payload)
    {
        var subscriptions = await GetUserSubscriptionsAsync(userId);
        
        if (subscriptions.Count == 0)
        {
            _logger.LogDebug("No push subscriptions found for user {UserId}", userId);
            return ServiceResult.Ok(); // Not an error - user just hasn't subscribed
        }
        
        var failedSubscriptions = new List<DbPushSubscription>();
        
        foreach (var subscription in subscriptions)
        {
            var success = await SendPushAsync(subscription, payload);
            if (!success)
            {
                failedSubscriptions.Add(subscription);
            }
        }
        
        // Mark failed subscriptions
        if (failedSubscriptions.Count > 0)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            foreach (var failed in failedSubscriptions)
            {
                var sub = await context.PushSubscriptions.FindAsync(failed.Id);
                if (sub != null)
                {
                    sub.FailedAttempts++;
                    if (sub.FailedAttempts >= 3)
                    {
                        sub.IsActive = false;
                        _logger.LogWarning("Deactivated push subscription {Id} after {Attempts} failed attempts", 
                            sub.Id, sub.FailedAttempts);
                    }
                }
            }
            await context.SaveChangesAsync();
        }
        
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SendToRoleAsync(string role, PushNotificationPayload payload)
    {
        var usersInRole = await _userManager.GetUsersInRoleAsync(role);
        
        foreach (var user in usersInRole)
        {
            await SendToUserAsync(user.Id, payload);
        }
        
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> NotifyParentsAsync(PushNotificationPayload payload)
    {
        return await SendToRoleAsync("Parent", payload);
    }

    public async Task<ServiceResult> SendHelpRequestNotificationAsync(int choreLogId, string childName, string choreName, string? reason)
    {
        var payload = new PushNotificationPayload
        {
            Title = $"🆘 {childName} needs help!",
            Body = string.IsNullOrEmpty(reason) 
                ? $"Help requested with: {choreName}"
                : $"{choreName}: {reason}",
            Icon = "/web-app-manifest-192x192.png",
            Badge = "/favicon-96x96.png",
            Url = $"/?helpRequestId={choreLogId}",
            Tag = $"help-request-{choreLogId}",
            Data = new Dictionary<string, object>
            {
                ["type"] = "help-request",
                ["choreLogId"] = choreLogId,
                ["choreName"] = choreName,
                ["childName"] = childName
            }
        };
        
        return await NotifyParentsAsync(payload);
    }

    private async Task<bool> SendPushAsync(DbPushSubscription subscription, PushNotificationPayload payload)
    {
        try
        {
            var settings = await _familySettingsService.GetSettingsAsync();
            
            if (string.IsNullOrEmpty(settings.VapidPublicKey) || string.IsNullOrEmpty(settings.VapidPrivateKey))
            {
                _logger.LogWarning("VAPID keys not configured, cannot send push notification");
                return false;
            }
            
            var webPushClient = new WebPushClient();
            
            var vapidDetails = new VapidDetails(
                settings.VapidSubject ?? "mailto:admin@dailybread.app",
                settings.VapidPublicKey,
                settings.VapidPrivateKey);
            
            var pushSubscription = new WebPush.PushSubscription(
                subscription.Endpoint,
                subscription.P256dh,
                subscription.Auth);
            
            var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
            
            await webPushClient.SendNotificationAsync(pushSubscription, payloadJson, vapidDetails);
            
            // Update last used timestamp
            await using var context = await _contextFactory.CreateDbContextAsync();
            var sub = await context.PushSubscriptions.FindAsync(subscription.Id);
            if (sub != null)
            {
                sub.LastUsedAt = DateTime.UtcNow;
                sub.FailedAttempts = 0; // Reset on success
                await context.SaveChangesAsync();
            }
            
            _logger.LogDebug("Push notification sent to subscription {Id}", subscription.Id);
            return true;
        }
        catch (WebPushException ex)
        {
            _logger.LogWarning(ex, "Failed to send push notification to subscription {Id}: {StatusCode}", 
                subscription.Id, ex.StatusCode);
            
            // 410 Gone means the subscription is no longer valid
            if (ex.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                var sub = await context.PushSubscriptions.FindAsync(subscription.Id);
                if (sub != null)
                {
                    sub.IsActive = false;
                    await context.SaveChangesAsync();
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending push notification to subscription {Id}", subscription.Id);
            return false;
        }
    }
}
