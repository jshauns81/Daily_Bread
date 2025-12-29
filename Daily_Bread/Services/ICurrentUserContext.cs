using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Daily_Bread.Services;

/// <summary>
/// Provides access to the current authenticated user's context.
/// All data access must use this to enforce household isolation.
/// </summary>
public interface ICurrentUserContext
{
    /// <summary>
    /// Gets the current user's ID.
    /// Throws if no user is authenticated.
    /// </summary>
    string UserId { get; }

    /// <summary>
    /// Gets the current user's household ID.
    /// Returns null for admin-only accounts.
    /// </summary>
    Guid? HouseholdId { get; }

    /// <summary>
    /// Gets the current user's roles.
    /// </summary>
    List<string> Roles { get; }

    /// <summary>
    /// Checks if the current user has a specific role.
    /// </summary>
    bool IsInRole(string role);

    /// <summary>
    /// Checks if the current user is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Initializes the context from the authentication state.
    /// Must be called before accessing properties.
    /// </summary>
    Task InitializeAsync();
}

/// <summary>
/// Implementation of ICurrentUserContext for Blazor Server.
/// </summary>
public class CurrentUserContext : ICurrentUserContext
{
    private readonly AuthenticationStateProvider _authStateProvider;
    private ClaimsPrincipal? _user;

    public CurrentUserContext(AuthenticationStateProvider authStateProvider)
    {
        _authStateProvider = authStateProvider;
    }

    public string UserId
    {
        get
        {
            EnsureInitialized();
            var userId = _user?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                throw new InvalidOperationException("No authenticated user found.");
            }
            return userId;
        }
    }

    public Guid? HouseholdId
    {
        get
        {
            EnsureInitialized();
            var householdIdClaim = _user?.FindFirstValue("HouseholdId");
            if (string.IsNullOrEmpty(householdIdClaim))
            {
                return null;
            }
            return Guid.TryParse(householdIdClaim, out var householdId) ? householdId : null;
        }
    }

    public List<string> Roles
    {
        get
        {
            EnsureInitialized();
            return _user?.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList() ?? [];
        }
    }

    public bool IsInRole(string role)
    {
        EnsureInitialized();
        return _user?.IsInRole(role) ?? false;
    }

    public bool IsAuthenticated
    {
        get
        {
            EnsureInitialized();
            return _user?.Identity?.IsAuthenticated ?? false;
        }
    }

    public async Task InitializeAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        _user = authState.User;
    }

    private void EnsureInitialized()
    {
        if (_user == null)
        {
            throw new InvalidOperationException("CurrentUserContext has not been initialized. Call InitializeAsync first.");
        }
    }
}
