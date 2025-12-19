namespace Daily_Bread.Services;

/// <summary>
/// Audit log entry types.
/// </summary>
public enum AuditEventType
{
    LoginSuccess,
    LoginFailure,
    Logout,
    PasswordReset,
    UserCreated,
    UserDeleted,
    UserLocked,
    UserUnlocked,
    RoleChanged,
    HouseholdCreated,
    PrivilegedAction
}

/// <summary>
/// Service for logging security and audit events.
/// Does NOT log passwords or other secrets.
/// </summary>
public interface IAuditLogService
{
    Task LogLoginSuccessAsync(string userId, string method, Guid? householdId);
    Task LogLoginFailureAsync(string userIdentifier, string method, string reason, Guid? householdId);
    Task LogLogoutAsync(string userId, Guid? householdId);
    Task LogPasswordResetAsync(string userId, string performedBy, Guid? householdId);
    Task LogUserCreatedAsync(string userId, string createdBy, string role, Guid? householdId);
    Task LogUserDeletedAsync(string userId, string deletedBy, Guid? householdId);
    Task LogUserLockedAsync(string userId, string performedBy, Guid? householdId);
    Task LogUserUnlockedAsync(string userId, string performedBy, Guid? householdId);
    Task LogRoleChangedAsync(string userId, string oldRole, string newRole, string performedBy, Guid? householdId);
    Task LogPrivilegedActionAsync(string action, string userId, Guid? householdId, string? details = null);
}

/// <summary>
/// Implementation of audit logging service.
/// Currently uses ILogger; can be extended to write to database or external service.
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(ILogger<AuditLogService> logger)
    {
        _logger = logger;
    }

    public Task LogLoginSuccessAsync(string userId, string method, Guid? householdId)
    {
        _logger.LogInformation(
            "AUDIT: Login success - User: {UserId}, Method: {Method}, Household: {HouseholdId}",
            userId, method, householdId);
        return Task.CompletedTask;
    }

    public Task LogLoginFailureAsync(string userIdentifier, string method, string reason, Guid? householdId)
    {
        _logger.LogWarning(
            "AUDIT: Login failure - User: {UserIdentifier}, Method: {Method}, Reason: {Reason}, Household: {HouseholdId}",
            userIdentifier, method, reason, householdId);
        return Task.CompletedTask;
    }

    public Task LogLogoutAsync(string userId, Guid? householdId)
    {
        _logger.LogInformation(
            "AUDIT: Logout - User: {UserId}, Household: {HouseholdId}",
            userId, householdId);
        return Task.CompletedTask;
    }

    public Task LogPasswordResetAsync(string userId, string performedBy, Guid? householdId)
    {
        _logger.LogWarning(
            "AUDIT: Password reset - User: {UserId}, PerformedBy: {PerformedBy}, Household: {HouseholdId}",
            userId, performedBy, householdId);
        return Task.CompletedTask;
    }

    public Task LogUserCreatedAsync(string userId, string createdBy, string role, Guid? householdId)
    {
        _logger.LogInformation(
            "AUDIT: User created - User: {UserId}, CreatedBy: {CreatedBy}, Role: {Role}, Household: {HouseholdId}",
            userId, createdBy, role, householdId);
        return Task.CompletedTask;
    }

    public Task LogUserDeletedAsync(string userId, string deletedBy, Guid? householdId)
    {
        _logger.LogWarning(
            "AUDIT: User deleted - User: {UserId}, DeletedBy: {DeletedBy}, Household: {HouseholdId}",
            userId, deletedBy, householdId);
        return Task.CompletedTask;
    }

    public Task LogUserLockedAsync(string userId, string performedBy, Guid? householdId)
    {
        _logger.LogWarning(
            "AUDIT: User locked - User: {UserId}, PerformedBy: {PerformedBy}, Household: {HouseholdId}",
            userId, performedBy, householdId);
        return Task.CompletedTask;
    }

    public Task LogUserUnlockedAsync(string userId, string performedBy, Guid? householdId)
    {
        _logger.LogInformation(
            "AUDIT: User unlocked - User: {UserId}, PerformedBy: {PerformedBy}, Household: {HouseholdId}",
            userId, performedBy, householdId);
        return Task.CompletedTask;
    }

    public Task LogRoleChangedAsync(string userId, string oldRole, string newRole, string performedBy, Guid? householdId)
    {
        _logger.LogWarning(
            "AUDIT: Role changed - User: {UserId}, OldRole: {OldRole}, NewRole: {NewRole}, PerformedBy: {PerformedBy}, Household: {HouseholdId}",
            userId, oldRole, newRole, performedBy, householdId);
        return Task.CompletedTask;
    }

    public Task LogPrivilegedActionAsync(string action, string userId, Guid? householdId, string? details = null)
    {
        _logger.LogWarning(
            "AUDIT: Privileged action - Action: {Action}, User: {UserId}, Household: {HouseholdId}, Details: {Details}",
            action, userId, householdId, details ?? "N/A");
        return Task.CompletedTask;
    }
}
