using Microsoft.JSInterop;

namespace Daily_Bread.Services;

/// <summary>
/// Service for biometric authentication using WebAuthn API.
/// Supports Face ID, Touch ID, Windows Hello, and other platform authenticators.
/// This is scaffolding for future biometric authentication implementation.
/// </summary>
public interface IBiometricAuthService
{
    /// <summary>
    /// Checks if the current platform supports biometric authentication.
    /// </summary>
    Task<BiometricSupportResult> CheckPlatformSupportAsync();
    
    /// <summary>
    /// Registers a new biometric credential for the current user.
    /// </summary>
    Task<BiometricRegistrationResult> RegisterCredentialAsync(string userId, string userName);
    
    /// <summary>
    /// Authenticates using a previously registered biometric credential.
    /// </summary>
    Task<BiometricAuthenticationResult> AuthenticateAsync();
}

public class BiometricAuthService : IBiometricAuthService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<BiometricAuthService> _logger;
    
    public BiometricAuthService(IJSRuntime jsRuntime, ILogger<BiometricAuthService> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }
    
    public async Task<BiometricSupportResult> CheckPlatformSupportAsync()
    {
        try
        {
            // Check if PublicKeyCredential is available in the browser
            var isAvailable = await _jsRuntime.InvokeAsync<bool>(
                "biometricAuth.isAvailable");
            
            if (!isAvailable)
            {
                return new BiometricSupportResult 
                { 
                    IsSupported = false, 
                    Message = "WebAuthn is not supported in this browser" 
                };
            }
            
            // Check for platform authenticator support (Face ID, Touch ID, Windows Hello)
            var hasPlatformAuthenticator = await _jsRuntime.InvokeAsync<bool>(
                "biometricAuth.isPlatformAuthenticatorAvailable");
            
            if (!hasPlatformAuthenticator)
            {
                return new BiometricSupportResult 
                { 
                    IsSupported = false, 
                    Message = "Platform authenticator (Face ID/Touch ID/Windows Hello) is not available" 
                };
            }
            
            // Get platform-specific info
            var platformInfo = await _jsRuntime.InvokeAsync<string>(
                "biometricAuth.getPlatformInfo");
            
            return new BiometricSupportResult 
            { 
                IsSupported = true, 
                Message = $"Biometric authentication is supported: {platformInfo}",
                PlatformInfo = platformInfo
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking biometric support");
            return new BiometricSupportResult 
            { 
                IsSupported = false, 
                Message = "Error checking biometric support" 
            };
        }
    }
    
    public async Task<BiometricRegistrationResult> RegisterCredentialAsync(string userId, string userName)
    {
        try
        {
            // Check platform support first
            var support = await CheckPlatformSupportAsync();
            if (!support.IsSupported)
            {
                return new BiometricRegistrationResult 
                { 
                    Success = false, 
                    Message = support.Message 
                };
            }
            
            // This is scaffolding - in a full implementation, this would:
            // 1. Call the server to get a challenge
            // 2. Use WebAuthn to create a credential
            // 3. Send the credential to the server for storage
            
            _logger.LogInformation("Biometric registration scaffolding called for user {UserId}", userId);
            
            // TODO: Implement full WebAuthn registration flow
            return new BiometricRegistrationResult 
            { 
                Success = false, 
                Message = "Biometric registration is not yet implemented (scaffolding only)" 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering biometric credential");
            return new BiometricRegistrationResult 
            { 
                Success = false, 
                Message = "Error during biometric registration" 
            };
        }
    }
    
    public async Task<BiometricAuthenticationResult> AuthenticateAsync()
    {
        try
        {
            // Check platform support first
            var support = await CheckPlatformSupportAsync();
            if (!support.IsSupported)
            {
                return new BiometricAuthenticationResult 
                { 
                    Success = false, 
                    Message = support.Message 
                };
            }
            
            // This is scaffolding - in a full implementation, this would:
            // 1. Call the server to get a challenge
            // 2. Use WebAuthn to get an assertion
            // 3. Send the assertion to the server for verification
            // 4. Return the authenticated user
            
            _logger.LogInformation("Biometric authentication scaffolding called");
            
            // TODO: Implement full WebAuthn authentication flow
            return new BiometricAuthenticationResult 
            { 
                Success = false, 
                Message = "Biometric authentication is not yet implemented (scaffolding only)" 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during biometric authentication");
            return new BiometricAuthenticationResult 
            { 
                Success = false, 
                Message = "Error during biometric authentication" 
            };
        }
    }
}

/// <summary>
/// Result of checking platform biometric support
/// </summary>
public class BiometricSupportResult
{
    public bool IsSupported { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? PlatformInfo { get; set; }
}

/// <summary>
/// Result of biometric credential registration
/// </summary>
public class BiometricRegistrationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? CredentialId { get; set; }
}

/// <summary>
/// Result of biometric authentication
/// </summary>
public class BiometricAuthenticationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? UserId { get; set; }
}
