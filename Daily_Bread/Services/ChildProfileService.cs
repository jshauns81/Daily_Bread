using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Summary information for a child profile.
/// </summary>
public class ChildProfileSummary
{
    public int ProfileId { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public bool IsActive { get; init; }
    public int DefaultAccountId { get; init; }
    public decimal TotalBalance { get; init; }
}

/// <summary>
/// Service for managing child profiles and their ledger accounts.
/// </summary>
public interface IChildProfileService
{
    /// <summary>
    /// Gets all child profiles.
    /// </summary>
    Task<List<ChildProfileSummary>> GetAllChildProfilesAsync(bool includeInactive = false);

    /// <summary>
    /// Gets a child profile by ID.
    /// </summary>
    Task<ChildProfile?> GetProfileByIdAsync(int profileId);

    /// <summary>
    /// Gets a child profile by user ID.
    /// </summary>
    Task<ChildProfile?> GetProfileByUserIdAsync(string userId);

    /// <summary>
    /// Gets the default ledger account for a child profile.
    /// </summary>
    Task<LedgerAccount?> GetDefaultAccountAsync(int profileId);

    /// <summary>
    /// Gets all ledger accounts for a child profile.
    /// </summary>
    Task<List<LedgerAccount>> GetAccountsAsync(int profileId);

    /// <summary>
    /// Creates a new child profile with a default account.
    /// </summary>
    Task<ServiceResult<ChildProfile>> CreateProfileAsync(string userId, string displayName);

    /// <summary>
    /// Creates a new ledger account for a child profile.
    /// </summary>
    Task<ServiceResult<LedgerAccount>> CreateAccountAsync(int profileId, string name, bool isDefault = false);

    /// <summary>
    /// Validates that a user has access to a profile (Parent can access any, Child only their own).
    /// </summary>
    Task<bool> CanAccessProfileAsync(string userId, bool isParent, int profileId);

    /// <summary>
    /// Validates that a user has access to an account (Parent can access any, Child only their own).
    /// </summary>
    Task<bool> CanAccessAccountAsync(string userId, bool isParent, int accountId);
}

public class ChildProfileService : IChildProfileService
{
    private readonly ApplicationDbContext _context;

    public ChildProfileService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ChildProfileSummary>> GetAllChildProfilesAsync(bool includeInactive = false)
    {
        // Use a more efficient query that doesn't load all transactions into memory
        var query = _context.ChildProfiles
            .Include(p => p.LedgerAccounts)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        var profiles = await query.ToListAsync();

        // Get account IDs for balance calculation
        var accountIds = profiles
            .SelectMany(p => p.LedgerAccounts.Where(a => a.IsActive))
            .Select(a => a.Id)
            .ToList();

        // Calculate balances in a single query instead of loading all transactions
        var balancesByAccount = await _context.LedgerTransactions
            .Where(t => accountIds.Contains(t.LedgerAccountId))
            .GroupBy(t => t.LedgerAccountId)
            .Select(g => new { AccountId = g.Key, Balance = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Balance);

        return profiles.Select(p =>
        {
            var activeAccounts = p.LedgerAccounts.Where(a => a.IsActive).ToList();
            var totalBalance = activeAccounts.Sum(a => balancesByAccount.GetValueOrDefault(a.Id, 0));
            
            return new ChildProfileSummary
            {
                ProfileId = p.Id,
                UserId = p.UserId,
                DisplayName = p.DisplayName,
                IsActive = p.IsActive,
                DefaultAccountId = activeAccounts.FirstOrDefault(a => a.IsDefault)?.Id 
                    ?? activeAccounts.FirstOrDefault()?.Id ?? 0,
                TotalBalance = totalBalance
            };
        }).OrderBy(p => p.DisplayName).ToList();
    }

    public async Task<ChildProfile?> GetProfileByIdAsync(int profileId)
    {
        return await _context.ChildProfiles
            .Include(p => p.User)
            .Include(p => p.LedgerAccounts)
            .FirstOrDefaultAsync(p => p.Id == profileId);
    }

    public async Task<ChildProfile?> GetProfileByUserIdAsync(string userId)
    {
        return await _context.ChildProfiles
            .Include(p => p.User)
            .Include(p => p.LedgerAccounts)
            .FirstOrDefaultAsync(p => p.UserId == userId);
    }

    public async Task<LedgerAccount?> GetDefaultAccountAsync(int profileId)
    {
        return await _context.LedgerAccounts
            .Where(a => a.ChildProfileId == profileId && a.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<List<LedgerAccount>> GetAccountsAsync(int profileId)
    {
        return await _context.LedgerAccounts
            .Where(a => a.ChildProfileId == profileId && a.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.Name)
            .ToListAsync();
    }

    public async Task<ServiceResult<ChildProfile>> CreateProfileAsync(string userId, string displayName)
    {
        // Check if profile already exists for this user
        var existing = await _context.ChildProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (existing != null)
        {
            return ServiceResult<ChildProfile>.Fail("A profile already exists for this user.");
        }

        var profile = new ChildProfile
        {
            UserId = userId,
            DisplayName = displayName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Add default account
        profile.LedgerAccounts.Add(new LedgerAccount
        {
            Name = "Main",
            IsDefault = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        _context.ChildProfiles.Add(profile);
        await _context.SaveChangesAsync();

        return ServiceResult<ChildProfile>.Ok(profile);
    }

    public async Task<ServiceResult<LedgerAccount>> CreateAccountAsync(int profileId, string name, bool isDefault = false)
    {
        var profile = await _context.ChildProfiles
            .Include(p => p.LedgerAccounts)
            .FirstOrDefaultAsync(p => p.Id == profileId);

        if (profile == null)
        {
            return ServiceResult<LedgerAccount>.Fail("Child profile not found.");
        }

        // Check for duplicate name
        if (profile.LedgerAccounts.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return ServiceResult<LedgerAccount>.Fail($"An account named '{name}' already exists.");
        }

        // If this is the default, clear existing default
        if (isDefault)
        {
            foreach (var account in profile.LedgerAccounts)
            {
                account.IsDefault = false;
            }
        }

        var newAccount = new LedgerAccount
        {
            ChildProfileId = profileId,
            Name = name,
            IsDefault = isDefault,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.LedgerAccounts.Add(newAccount);
        await _context.SaveChangesAsync();

        return ServiceResult<LedgerAccount>.Ok(newAccount);
    }

    public async Task<bool> CanAccessProfileAsync(string userId, bool isParent, int profileId)
    {
        // Parents can access any profile
        if (isParent)
        {
            return await _context.ChildProfiles.AnyAsync(p => p.Id == profileId);
        }

        // Children can only access their own profile
        return await _context.ChildProfiles
            .AnyAsync(p => p.Id == profileId && p.UserId == userId);
    }

    public async Task<bool> CanAccessAccountAsync(string userId, bool isParent, int accountId)
    {
        // Parents can access any account
        if (isParent)
        {
            return await _context.LedgerAccounts.AnyAsync(a => a.Id == accountId);
        }

        // Children can only access accounts linked to their profile
        return await _context.LedgerAccounts
            .AnyAsync(a => a.Id == accountId && a.ChildProfile.UserId == userId);
    }
}
