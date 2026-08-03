using Daily_Bread.Data;
using Daily_Bread.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// §3.1 server-side theme rules: household-scoped, child-writable (authorship
/// is the point), and a kid's theme is his — a sibling can't overwrite or
/// delete it, while the author and any parent can.
/// </summary>
public sealed class ThemeFileServiceTests : IAsyncLifetime
{
    private static readonly Guid HouseholdA = Guid.NewGuid();
    private static readonly Guid HouseholdB = Guid.NewGuid();

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestDbContextFactory _contextFactory = null!;
    private ThemeFileService _service = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _contextFactory = new TestDbContextFactory(options);

        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();
        _service = new ThemeFileService(_contextFactory);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Child_Can_Create_And_Update_Their_Own_Theme()
    {
        var created = await _service.UpsertAsync(HouseholdA, "victors-blue", "meta: {}", "victor", authorIsParent: false);
        Assert.True(created.Success, created.ErrorMessage);

        var updated = await _service.UpsertAsync(HouseholdA, "victors-blue", "meta: { name: V2 }", "victor", authorIsParent: false);
        Assert.True(updated.Success, updated.ErrorMessage);
        Assert.Equal("meta: { name: V2 }", updated.Data!.Yaml);
    }

    [Fact]
    public async Task Sibling_Cannot_Overwrite_Someone_Elses_Theme_But_Parent_Can()
    {
        _ = await _service.UpsertAsync(HouseholdA, "victors-blue", "a", "victor", authorIsParent: false);

        var sibling = await _service.UpsertAsync(HouseholdA, "victors-blue", "b", "emma", authorIsParent: false);
        Assert.False(sibling.Success);

        var parent = await _service.UpsertAsync(HouseholdA, "victors-blue", "c", "shaun", authorIsParent: true);
        Assert.True(parent.Success, parent.ErrorMessage);
    }

    [Fact]
    public async Task Households_Are_Isolated_And_Share_Slugs_Freely()
    {
        var a = await _service.UpsertAsync(HouseholdA, "night", "a", "victor", authorIsParent: false);
        var b = await _service.UpsertAsync(HouseholdB, "night", "b", "stranger", authorIsParent: false);
        Assert.True(a.Success && b.Success);

        var listA = await _service.ListAsync(HouseholdA);
        Assert.Single(listA);
        Assert.Equal("a", listA[0].Yaml);
    }

    [Fact]
    public async Task Delete_Is_Author_Or_Parent()
    {
        _ = await _service.UpsertAsync(HouseholdA, "night", "a", "victor", authorIsParent: false);

        var sibling = await _service.DeleteAsync(HouseholdA, "night", "emma", requesterIsParent: false);
        Assert.False(sibling.Success);

        var author = await _service.DeleteAsync(HouseholdA, "night", "victor", requesterIsParent: false);
        Assert.True(author.Success, author.ErrorMessage);
        Assert.Empty(await _service.ListAsync(HouseholdA));
    }

    [Fact]
    public async Task Slug_And_Size_Are_Validated()
    {
        var badSlug = await _service.UpsertAsync(HouseholdA, "Not A Slug!", "a", "victor", authorIsParent: false);
        Assert.False(badSlug.Success);

        var tooBig = await _service.UpsertAsync(
            HouseholdA, "big", new string('x', ThemeFileService.MaxYamlBytes + 1), "victor", authorIsParent: false);
        Assert.False(tooBig.Success);

        var empty = await _service.UpsertAsync(HouseholdA, "empty", "  ", "victor", authorIsParent: false);
        Assert.False(empty.Success);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
