using System.Security.Cryptography;
using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// Data for an authenticated kid mode session.
/// </summary>
public class KidModeSession
{
    public int ProfileId { get; init; }
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public DateTime AuthenticatedAt { get; init; }
}

/// <summary>
/// Service interface for Kid Mode authentication.
/// </summary>
public interface IKidModeService
{
    /// <summary>
    /// Validates a PIN and returns session data if valid.
    /// </summary>
    Task<KidModeSession?> ValidatePinAsync(string pin);

    /// <summary>
    /// Sets or updates a PIN for a child profile.
    /// </summary>
    Task<ServiceResult> SetPinAsync(int profileId, string pin);

    /// <summary>
    /// Clears/removes a PIN for a child profile.
    /// </summary>
    Task<ServiceResult> ClearPinAsync(int profileId);

    /// <summary>
    /// Checks if a profile has a PIN set.
    /// </summary>
    Task<bool> HasPinAsync(int profileId);

    /// <summary>
    /// Gets all profiles that have PINs set (for kid mode).
    /// </summary>
    Task<List<KidModeProfile>> GetKidModeProfilesAsync();
}

/// <summary>
/// Summary of a profile available in kid mode.
/// </summary>
public class KidModeProfile
{
    public int ProfileId { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// Service for Kid Mode PIN authentication.
/// </summary>
public class KidModeService : IKidModeService
{
    private readonly ApplicationDbContext _context;
    private const int PinLength = 4;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 10000;

    public KidModeService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<KidModeSession?> ValidatePinAsync(string pin)
    {
        if (string.IsNullOrEmpty(pin) || pin.Length != PinLength || !pin.All(char.IsDigit))
        {
            return null;
        }

        // Get all active profiles with PINs
        var profiles = await _context.ChildProfiles
            .Where(p => p.IsActive && p.PinHash != null)
            .ToListAsync();

        // Check each profile's PIN (we have to check all since PIN isn't unique)
        foreach (var profile in profiles)
        {
            if (VerifyPin(pin, profile.PinHash!))
            {
                return new KidModeSession
                {
                    ProfileId = profile.Id,
                    UserId = profile.UserId,
                    DisplayName = profile.DisplayName,
                    AuthenticatedAt = DateTime.UtcNow
                };
            }
        }

        return null;
    }

    public async Task<ServiceResult> SetPinAsync(int profileId, string pin)
    {
        if (string.IsNullOrEmpty(pin))
        {
            return ServiceResult.Fail("PIN is required.");
        }

        if (pin.Length != PinLength)
        {
            return ServiceResult.Fail($"PIN must be exactly {PinLength} digits.");
        }

        if (!pin.All(char.IsDigit))
        {
            return ServiceResult.Fail("PIN must contain only numbers.");
        }

        var profile = await _context.ChildProfiles.FindAsync(profileId);
        if (profile == null)
        {
            return ServiceResult.Fail("Profile not found.");
        }

        profile.PinHash = HashPin(pin);
        profile.ModifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ClearPinAsync(int profileId)
    {
        var profile = await _context.ChildProfiles.FindAsync(profileId);
        if (profile == null)
        {
            return ServiceResult.Fail("Profile not found.");
        }

        profile.PinHash = null;
        profile.ModifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<bool> HasPinAsync(int profileId)
    {
        return await _context.ChildProfiles
            .AnyAsync(p => p.Id == profileId && p.PinHash != null);
    }

    public async Task<List<KidModeProfile>> GetKidModeProfilesAsync()
    {
        return await _context.ChildProfiles
            .Where(p => p.IsActive && p.PinHash != null)
            .Select(p => new KidModeProfile
            {
                ProfileId = p.Id,
                DisplayName = p.DisplayName
            })
            .ToListAsync();
    }

    /// <summary>
    /// Hashes a PIN using PBKDF2 with a random salt.
    /// </summary>
    private static string HashPin(string pin)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        // Combine salt and hash for storage
        byte[] combined = new byte[SaltSize + HashSize];
        Buffer.BlockCopy(salt, 0, combined, 0, SaltSize);
        Buffer.BlockCopy(hash, 0, combined, SaltSize, HashSize);

        return Convert.ToBase64String(combined);
    }

    /// <summary>
    /// Verifies a PIN against a stored hash.
    /// </summary>
    private static bool VerifyPin(string pin, string storedHash)
    {
        try
        {
            byte[] combined = Convert.FromBase64String(storedHash);
            if (combined.Length != SaltSize + HashSize)
                return false;

            byte[] salt = new byte[SaltSize];
            byte[] storedHashBytes = new byte[HashSize];
            Buffer.BlockCopy(combined, 0, salt, 0, SaltSize);
            Buffer.BlockCopy(combined, SaltSize, storedHashBytes, 0, HashSize);

            byte[] computedHash = Rfc2898DeriveBytes.Pbkdf2(
                pin,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return CryptographicOperations.FixedTimeEquals(computedHash, storedHashBytes);
        }
        catch
        {
            return false;
        }
    }
}
