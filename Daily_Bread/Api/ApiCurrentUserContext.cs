using System.Security.Claims;
using Daily_Bread.Services;

namespace Daily_Bread.Api;

/// <summary>
/// ICurrentUserContext for API (bearer) requests: reads the ClaimsPrincipal
/// from HttpContext instead of Blazor's AuthenticationStateProvider, so the
/// existing Services/ layer works unchanged for native clients.
/// Selected per-request by the factory registration in Program.cs
/// (requests under /api/v1 get this; Blazor circuits keep CurrentUserContext).
/// </summary>
public class ApiCurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ApiCurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public string UserId
    {
        get
        {
            var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
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
            var claim = User?.FindFirstValue(ApiJwt.HouseholdClaim);
            if (string.IsNullOrEmpty(claim))
            {
                return null;
            }
            return Guid.TryParse(claim, out var householdId) ? householdId : null;
        }
    }

    public List<string> Roles =>
        User?.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList() ?? [];

    public bool IsInRole(string role) => User?.IsInRole(role) ?? false;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    /// <summary>No-op: HttpContext.User is populated by the authentication middleware.</summary>
    public Task InitializeAsync() => Task.CompletedTask;
}
