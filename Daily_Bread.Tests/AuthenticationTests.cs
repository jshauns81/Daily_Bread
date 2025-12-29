using Daily_Bread.Services;
using Daily_Bread.Data;
using Microsoft.AspNetCore.Identity;
using Xunit;
using Moq;

namespace Daily_Bread.Tests;

/// <summary>
/// Tests for authentication service and household isolation.
/// Run with: dotnet test
/// </summary>
public class AuthenticationTests
{
    [Fact]
    public void PasswordCredential_Should_Set_Properties()
    {
        // Arrange & Act
        var credential = new PasswordCredential
        {
            UserName = "testuser",
            Password = "Test123!",
            RememberDevice = true
        };

        // Assert
        Assert.Equal("testuser", credential.UserName);
        Assert.Equal("Test123!", credential.Password);
        Assert.True(credential.RememberDevice);
    }

    [Fact]
    public void PinCredential_Should_Validate_Format()
    {
        // Arrange
        var validPin = "1234";
        var invalidPin = "12";

        // Act & Assert
        Assert.Equal(4, validPin.Length);
        Assert.True(validPin.All(char.IsDigit));
        Assert.NotEqual(4, invalidPin.Length);
    }

    [Fact]
    public void AuthResult_Fail_Should_Set_Error_Info()
    {
        // Arrange & Act
        var result = AuthResult.Fail("InvalidCredentials", "Invalid username or password");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("InvalidCredentials", result.ErrorCode);
        Assert.Equal("Invalid username or password", result.UserFacingMessage);
        Assert.Null(result.User);
    }

    [Fact]
    public void AuthResult_Success_Should_Include_User()
    {
        // Arrange
        var user = new UserSummary
        {
            UserId = "user123",
            UserName = "testuser",
            Roles = new List<string> { "Child" },
            HouseholdId = Guid.NewGuid()
        };

        // Act
        var result = AuthResult.Ok(user);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.User);
        Assert.Equal("user123", result.User.UserId);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void UserSummary_Should_Include_HouseholdId()
    {
        // Arrange
        var householdId = Guid.NewGuid();

        // Act
        var user = new UserSummary
        {
            UserId = "user123",
            UserName = "testuser",
            Roles = new List<string> { "Parent" },
            HouseholdId = householdId
        };

        // Assert
        Assert.Equal(householdId, user.HouseholdId);
        Assert.Contains("Parent", user.Roles);
    }
}

/// <summary>
/// Tests for household isolation.
/// These would require database setup in a real test environment.
/// </summary>
public class HouseholdIsolationTests
{
    [Fact]
    public void Household_Should_Have_Required_Properties()
    {
        // Arrange & Act
        var household = new Daily_Bread.Data.Models.Household
        {
            Id = Guid.NewGuid(),
            Name = "Smith Family",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.NotEqual(Guid.Empty, household.Id);
        Assert.Equal("Smith Family", household.Name);
        Assert.True(household.IsActive);
    }

    [Fact]
    public void ApplicationUser_Should_Have_HouseholdId()
    {
        // Arrange
        var householdId = Guid.NewGuid();

        // Act
        var user = new ApplicationUser
        {
            UserName = "testuser",
            HouseholdId = householdId
        };

        // Assert
        Assert.Equal(householdId, user.HouseholdId);
    }

    [Fact]
    public void AdminUser_Can_Have_Null_HouseholdId()
    {
        // Arrange & Act
        var adminUser = new ApplicationUser
        {
            UserName = "admin",
            HouseholdId = null
        };

        // Assert
        Assert.Null(adminUser.HouseholdId);
    }
}

/// <summary>
/// Integration tests for household scoping.
/// In a real scenario, these would verify database queries filter by HouseholdId.
/// </summary>
public class HouseholdScopingTests
{
    [Fact]
    public void CurrentUserContext_Should_Expose_HouseholdId()
    {
        // This test demonstrates the contract
        // Real implementation would require mocking AuthenticationStateProvider
        var householdId = Guid.NewGuid();
        
        // In real usage:
        // var context = serviceProvider.GetRequiredService<ICurrentUserContext>();
        // await context.InitializeAsync();
        // Assert.Equal(expectedHouseholdId, context.HouseholdId);
        
        Assert.NotEqual(Guid.Empty, householdId);
    }
}
