using Daily_Bread.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// DTO for displaying a user in the admin list.
/// </summary>
public class UserListItem
{
    public required string Id { get; init; }
    public required string UserName { get; init; }
    public string? Email { get; init; }
    public required List<string> Roles { get; init; }
    public bool IsLockedOut { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// DTO for creating a new user.
/// </summary>
public class CreateUserRequest
{
    public required string UserName { get; set; }
    public string? Email { get; set; }
    public required string Password { get; set; }
    public required string Role { get; set; }
    public string? DisplayName { get; set; } // For Child role - creates ChildProfile
}

/// <summary>
/// DTO for updating a user's role.
/// </summary>
public class UpdateUserRoleRequest
{
    public required string UserId { get; set; }
    public required string NewRole { get; set; }
}

/// <summary>
/// DTO for resetting a user's password.
/// </summary>
public class ResetPasswordRequest
{
    public required string UserId { get; set; }
    public required string NewPassword { get; set; }
}

/// <summary>
/// Service interface for user management operations.
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    /// Gets all users with their roles.
    /// </summary>
    Task<List<UserListItem>> GetAllUsersAsync();

    /// <summary>
    /// Gets a single user by ID.
    /// </summary>
    Task<UserListItem?> GetUserByIdAsync(string userId);

    /// <summary>
    /// Creates a new user with the specified role.
    /// </summary>
    Task<ServiceResult<string>> CreateUserAsync(CreateUserRequest request);

    /// <summary>
    /// Changes a user's role.
    /// </summary>
    Task<ServiceResult> UpdateUserRoleAsync(UpdateUserRoleRequest request);

    /// <summary>
    /// Resets a user's password.
    /// </summary>
    Task<ServiceResult> ResetPasswordAsync(ResetPasswordRequest request);

    /// <summary>
    /// Locks out a user account.
    /// </summary>
    Task<ServiceResult> LockoutUserAsync(string userId);

    /// <summary>
    /// Unlocks a user account.
    /// </summary>
    Task<ServiceResult> UnlockUserAsync(string userId);

    /// <summary>
    /// Deletes a user account.
    /// </summary>
    Task<ServiceResult> DeleteUserAsync(string userId);

    /// <summary>
    /// Gets available roles for assignment.
    /// </summary>
    Task<List<string>> GetAvailableRolesAsync();
}

/// <summary>
/// Service for managing user accounts (CRUD operations).
/// </summary>
public class UserManagementService : IUserManagementService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ApplicationDbContext _context;
    private readonly IChildProfileService _childProfileService;

    public UserManagementService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ApplicationDbContext context,
        IChildProfileService childProfileService)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _context = context;
        _childProfileService = childProfileService;
    }

    public async Task<List<UserListItem>> GetAllUsersAsync()
    {
        var users = await _userManager.Users.ToListAsync();
        var userItems = new List<UserListItem>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            userItems.Add(new UserListItem
            {
                Id = user.Id,
                UserName = user.UserName ?? "",
                Email = user.Email,
                Roles = roles.ToList(),
                IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
                LockoutEnd = user.LockoutEnd
            });
        }

        return userItems.OrderBy(u => u.UserName).ToList();
    }

    public async Task<UserListItem?> GetUserByIdAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        return new UserListItem
        {
            Id = user.Id,
            UserName = user.UserName ?? "",
            Email = user.Email,
            Roles = roles.ToList(),
            IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
            LockoutEnd = user.LockoutEnd
        };
    }

    public async Task<ServiceResult<string>> CreateUserAsync(CreateUserRequest request)
    {
        // Validate role
        var validRoles = new[] { SeedData.ParentRole, SeedData.ChildRole };
        if (!validRoles.Contains(request.Role))
        {
            return ServiceResult<string>.Fail($"Invalid role. Must be one of: {string.Join(", ", validRoles)}");
        }

        // Check if username already exists
        var existingUser = await _userManager.FindByNameAsync(request.UserName);
        if (existingUser != null)
        {
            return ServiceResult<string>.Fail($"Username '{request.UserName}' is already taken.");
        }

        // Create the user
        var user = new ApplicationUser
        {
            UserName = request.UserName,
            Email = request.Email,
            EmailConfirmed = true // Skip email confirmation for admin-created users
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            return ServiceResult<string>.Fail($"Failed to create user: {errors}");
        }

        // Assign role
        var roleResult = await _userManager.AddToRoleAsync(user, request.Role);
        if (!roleResult.Succeeded)
        {
            // Rollback user creation
            await _userManager.DeleteAsync(user);
            var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
            return ServiceResult<string>.Fail($"Failed to assign role: {errors}");
        }

        // If Child role, create ChildProfile with ledger account
        if (request.Role == SeedData.ChildRole)
        {
            var displayName = string.IsNullOrWhiteSpace(request.DisplayName) 
                ? request.UserName 
                : request.DisplayName;

            var profileResult = await _childProfileService.CreateProfileAsync(user.Id, displayName);
            if (!profileResult.Success)
            {
                // Log warning but don't fail - profile can be created later
                // The user is created successfully, just the profile failed
            }
        }

        return ServiceResult<string>.Ok(user.Id);
    }

    public async Task<ServiceResult> UpdateUserRoleAsync(UpdateUserRoleRequest request)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null)
        {
            return ServiceResult.Fail("User not found.");
        }

        // Prevent changing Admin role
        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Contains(SeedData.AdminRole))
        {
            return ServiceResult.Fail("Cannot change the role of an Admin user.");
        }

        // Validate new role
        var validRoles = new[] { SeedData.ParentRole, SeedData.ChildRole };
        if (!validRoles.Contains(request.NewRole))
        {
            return ServiceResult.Fail($"Invalid role. Must be one of: {string.Join(", ", validRoles)}");
        }

        // Remove existing roles (except Admin)
        var rolesToRemove = currentRoles.Where(r => r != SeedData.AdminRole).ToList();
        if (rolesToRemove.Any())
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                var errors = string.Join(", ", removeResult.Errors.Select(e => e.Description));
                return ServiceResult.Fail($"Failed to remove existing roles: {errors}");
            }
        }

        // Add new role
        var addResult = await _userManager.AddToRoleAsync(user, request.NewRole);
        if (!addResult.Succeeded)
        {
            var errors = string.Join(", ", addResult.Errors.Select(e => e.Description));
            return ServiceResult.Fail($"Failed to assign new role: {errors}");
        }

        // If changing to Child role and no profile exists, create one
        if (request.NewRole == SeedData.ChildRole)
        {
            var existingProfile = await _childProfileService.GetProfileByUserIdAsync(user.Id);
            if (existingProfile == null)
            {
                await _childProfileService.CreateProfileAsync(user.Id, user.UserName ?? "Child");
            }
        }

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null)
        {
            return ServiceResult.Fail("User not found.");
        }

        // Remove existing password and set new one
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ServiceResult.Fail($"Failed to reset password: {errors}");
        }

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> LockoutUserAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Fail("User not found.");
        }

        // Prevent locking out Admin users
        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains(SeedData.AdminRole))
        {
            return ServiceResult.Fail("Cannot lock out an Admin user.");
        }

        // Set lockout end to far future (effectively permanent until unlocked)
        var result = await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ServiceResult.Fail($"Failed to lock out user: {errors}");
        }

        // Enable lockout if not already enabled
        await _userManager.SetLockoutEnabledAsync(user, true);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UnlockUserAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Fail("User not found.");
        }

        // Clear lockout
        var result = await _userManager.SetLockoutEndDateAsync(user, null);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ServiceResult.Fail($"Failed to unlock user: {errors}");
        }

        // Also reset access failed count
        await _userManager.ResetAccessFailedCountAsync(user);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteUserAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Fail("User not found.");
        }

        // Prevent deleting Admin users
        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains(SeedData.AdminRole))
        {
            return ServiceResult.Fail("Cannot delete an Admin user.");
        }

        // Check for child profile and handle related data
        var childProfile = await _context.ChildProfiles
            .Include(p => p.LedgerAccounts)
                .ThenInclude(a => a.LedgerTransactions)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (childProfile != null)
        {
            // Check if there are any transactions - if so, we can't fully delete
            var hasTransactions = childProfile.LedgerAccounts
                .Any(a => a.LedgerTransactions.Count > 0);

            if (hasTransactions)
            {
                // Soft delete: Mark profile and accounts as inactive, keep transactions for history
                childProfile.IsActive = false;
                childProfile.ModifiedAt = DateTime.UtcNow;

                foreach (var account in childProfile.LedgerAccounts)
                {
                    account.IsActive = false;
                    account.ModifiedAt = DateTime.UtcNow;
                }

                // Clear the user reference but keep the profile for historical records
                // We need to update the ChoreDefinitions to unassign this user
                var choreDefinitions = await _context.ChoreDefinitions
                    .Where(c => c.AssignedUserId == userId)
                    .ToListAsync();
                
                foreach (var chore in choreDefinitions)
                {
                    chore.AssignedUserId = null;
                }

                await _context.SaveChangesAsync();

                // Now delete the user - the profile will remain orphaned but inactive
                // First, we need to clear the UserId on the profile to avoid FK constraint
                // Actually, with Cascade delete on ChildProfile.User, this should work
                // But we set it to cascade, so deleting user will delete profile
                // The issue is LedgerAccount has Restrict...
                
                // So we need to manually handle this:
                // 1. Remove the UserId from ChildProfile (make it nullable or handle differently)
                // Since UserId is required, we can't null it. Let's just lock out the user instead.
                
                return ServiceResult.Fail(
                    "This user has transaction history that must be preserved. " +
                    "The account has been deactivated instead. Use 'Lock Account' to prevent login.");
            }
            else
            {
                // No transactions - safe to delete everything
                // Delete ledger accounts first (they have Restrict delete behavior)
                _context.LedgerAccounts.RemoveRange(childProfile.LedgerAccounts);
                
                // Delete child profile
                _context.ChildProfiles.Remove(childProfile);
                
                await _context.SaveChangesAsync();
            }
        }

        // Unassign any chores assigned to this user
        var assignedChores = await _context.ChoreDefinitions
            .Where(c => c.AssignedUserId == userId)
            .ToListAsync();
        
        foreach (var chore in assignedChores)
        {
            chore.AssignedUserId = null;
        }

        // Clear any chore log references to this user
        var completedLogs = await _context.ChoreLogs
            .Where(c => c.CompletedByUserId == userId || c.ApprovedByUserId == userId)
            .ToListAsync();
        
        foreach (var log in completedLogs)
        {
            if (log.CompletedByUserId == userId)
                log.CompletedByUserId = null;
            if (log.ApprovedByUserId == userId)
                log.ApprovedByUserId = null;
        }

        await _context.SaveChangesAsync();

        // Delete the user
        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ServiceResult.Fail($"Failed to delete user: {errors}");
        }

        return ServiceResult.Ok();
    }

    public async Task<List<string>> GetAvailableRolesAsync()
    {
        // Return only Parent and Child roles - Admin is not assignable through UI
        return await Task.FromResult(new List<string> { SeedData.ParentRole, SeedData.ChildRole });
    }
}
