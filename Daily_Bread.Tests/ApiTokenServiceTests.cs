using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Daily_Bread.Api;
using Daily_Bread.Data;
using Daily_Bread.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Phase 0 API auth tests: token issuance, refresh rotation, reuse detection,
/// revocation, and the money-as-string wire convention.
/// </summary>
public sealed class ApiTokenServiceTests : IAsyncLifetime
{
    private const string UserId = "child-1";
    private static readonly Guid HouseholdId = Guid.NewGuid();

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private ApplicationDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(_options);
        await _db.Database.EnsureCreatedAsync();

        _db.Households.Add(new Daily_Bread.Data.Models.Household
        {
            Id = HouseholdId,
            Name = "Test Family"
        });
        _db.Users.Add(new ApplicationUser
        {
            Id = UserId,
            UserName = "kid",
            HouseholdId = HouseholdId
        });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static IConfiguration TestConfig(int accessMinutes = 15, int refreshDays = 90) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:Jwt:SigningKey"] = "unit-test-signing-key-that-is-long-enough-123456",
            ["Api:Jwt:AccessTokenMinutes"] = accessMinutes.ToString(),
            ["Api:Jwt:RefreshTokenDays"] = refreshDays.ToString()
        }).Build();

    private ApiTokenService CreateService(IConfiguration? config = null)
    {
        config ??= TestConfig();
        return new ApiTokenService(
            _db,
            config,
            ApiJwt.ResolveSigningKey(config),
            Mock.Of<IAuditLogService>(),
            NullLogger<ApiTokenService>.Instance);
    }

    private static UserSummary Kid() => new()
    {
        UserId = UserId,
        UserName = "kid",
        Roles = ["Child"],
        HouseholdId = HouseholdId
    };

    // ---------- Issuance ----------

    [Fact]
    public async Task IssueTokens_Access_Token_Carries_Identity_Claims()
    {
        var service = CreateService();
        var tokens = await service.IssueTokensAsync(Kid(), "Kid's iPhone");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        Assert.Equal(UserId, jwt.Claims.First(c => c.Type == "sub").Value);
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Child");
        Assert.Equal(HouseholdId.ToString(), jwt.Claims.First(c => c.Type == ApiJwt.HouseholdClaim).Value);
        Assert.True(jwt.ValidTo > DateTime.UtcNow.AddMinutes(13));
        Assert.True(jwt.ValidTo < DateTime.UtcNow.AddMinutes(17));
    }

    [Fact]
    public async Task IssueTokens_Stores_Only_The_Hash_Of_The_Refresh_Token()
    {
        var service = CreateService();
        var tokens = await service.IssueTokensAsync(Kid(), null);

        var stored = await _db.RefreshTokens.SingleAsync();
        Assert.NotEqual(tokens.RefreshToken, stored.TokenHash);
        Assert.Equal(ApiJwt.HashToken(tokens.RefreshToken), stored.TokenHash);
        Assert.Null(stored.RevokedAtUtc);
        Assert.Equal(UserId, stored.UserId);
    }

    // ---------- Refresh rotation ----------

    [Fact]
    public async Task Refresh_Rotates_The_Token_And_Revokes_The_Old_One()
    {
        var service = CreateService();
        var first = await service.IssueTokensAsync(Kid(), null);

        var second = await service.RefreshAsync(first.RefreshToken);

        Assert.NotNull(second);
        Assert.NotEqual(first.RefreshToken, second!.RefreshToken);
        Assert.Equal(UserId, second.User.UserId);

        var oldRow = await _db.RefreshTokens
            .SingleAsync(t => t.TokenHash == ApiJwt.HashToken(first.RefreshToken));
        Assert.NotNull(oldRow.RevokedAtUtc);
        Assert.Equal(ApiJwt.HashToken(second.RefreshToken), oldRow.ReplacedByTokenHash);
    }

    [Fact]
    public async Task Refresh_Reuse_Of_A_Rotated_Token_Revokes_Everything()
    {
        var service = CreateService();
        var first = await service.IssueTokensAsync(Kid(), null);
        var second = await service.RefreshAsync(first.RefreshToken);
        Assert.NotNull(second);

        // Replay the already-rotated token: theft containment must kick in.
        var replay = await service.RefreshAsync(first.RefreshToken);
        Assert.Null(replay);

        // Every token for the user is now revoked, including the fresh one.
        var active = await _db.RefreshTokens.CountAsync(t => t.RevokedAtUtc == null);
        Assert.Equal(0, active);

        var thirdAttempt = await service.RefreshAsync(second!.RefreshToken);
        Assert.Null(thirdAttempt);
    }

    [Fact]
    public async Task Refresh_With_An_Unknown_Token_Returns_Null()
    {
        var service = CreateService();
        Assert.Null(await service.RefreshAsync("never-issued-token"));
    }

    [Fact]
    public async Task Refresh_With_An_Expired_Token_Returns_Null()
    {
        var service = CreateService();
        var tokens = await service.IssueTokensAsync(Kid(), null);

        var row = await _db.RefreshTokens.SingleAsync();
        row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        Assert.Null(await service.RefreshAsync(tokens.RefreshToken));
    }

    // ---------- Revocation ----------

    [Fact]
    public async Task Revoke_Kills_A_Specific_Token()
    {
        var service = CreateService();
        var tokens = await service.IssueTokensAsync(Kid(), null);

        await service.RevokeAsync(tokens.RefreshToken);

        Assert.Null(await service.RefreshAsync(tokens.RefreshToken));
    }

    [Fact]
    public async Task RevokeAllForUser_Kills_Every_Active_Token()
    {
        var service = CreateService();
        await service.IssueTokensAsync(Kid(), "iPhone");
        await service.IssueTokensAsync(Kid(), "Mac");

        await service.RevokeAllForUserAsync(UserId);

        Assert.Equal(0, await _db.RefreshTokens.CountAsync(t => t.RevokedAtUtc == null));
    }

    // ---------- Signing key + wire conventions ----------

    [Fact]
    public void ResolveSigningKey_Is_Deterministic_For_The_Same_Configured_Key()
    {
        var a = ApiJwt.ResolveSigningKey(TestConfig());
        var b = ApiJwt.ResolveSigningKey(TestConfig());
        Assert.Equal(
            ((SymmetricSecurityKey)a).Key,
            ((SymmetricSecurityKey)b).Key);
    }

    [Fact]
    public void MoneyStringConverter_Writes_Decimal_As_Two_Place_String()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new MoneyStringConverter());

        Assert.Equal("\"2.50\"", JsonSerializer.Serialize(2.5m, options));
        Assert.Equal("\"0.00\"", JsonSerializer.Serialize(0m, options));
        Assert.Equal("\"12.00\"", JsonSerializer.Serialize(12m, options));
    }

    [Fact]
    public void MoneyStringConverter_Reads_Both_String_And_Number()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new MoneyStringConverter());

        Assert.Equal(2.50m, JsonSerializer.Deserialize<decimal>("\"2.50\"", options));
        Assert.Equal(2.50m, JsonSerializer.Deserialize<decimal>("2.50", options));
    }
}
