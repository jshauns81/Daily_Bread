using System.Security.Cryptography;
using Daily_Bread.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Data;

/// <summary>
/// Development data seeder that generates realistic test data for performance testing.
/// Creates a test family with multiple children, extensive chore history, and transactions.
/// 
/// PURPOSE:
/// - Generate enough data to catch N+1 query issues (4 children × 90 days × 10+ chores)
/// - Create realistic transaction history for balance calculations
/// - Provide meaningful data for charts, dashboards, and reports
/// 
/// USAGE:
/// - Only runs when Seed:DevData = true in configuration
/// - Idempotent: checks for existing test family before creating
/// - Creates: 2 parents, 4 children, 8-12 chores each, 90 days history
/// </summary>
public static class DevDataSeeder
{
    private const string TestFamilyMarker = "TestFamily_DailyBread_Dev";
    private const string TestHouseholdName = "Smith Family (Test Data)";
    
    // Test user passwords (development only!)
    private const string ParentPassword = "Parent123!";
    private const string ChildPassword = "Child123!";

    private static readonly Random _random = new(42); // Fixed seed for reproducible data

    // PIN hashing constants (matching KidModeService)
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 10000;

    public static async Task SeedDevDataAsync(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        // Check if dev data seeding is enabled
        var seedDevData = configuration.GetValue<bool?>("Seed:DevData") ?? false;
        if (!seedDevData)
        {
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("DevDataSeeder");

        // Check if test family already exists (idempotent)
        var existingTestUser = await userManager.FindByNameAsync("parent_test");
        if (existingTestUser != null)
        {
            logger.LogInformation("Test family already exists. Skipping dev data seeding.");
            return;
        }

        logger.LogInformation("Starting development data seeding...");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Create household
            var household = new Household
            {
                Id = Guid.NewGuid(),
                Name = TestHouseholdName,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            context.Households.Add(household);
            await context.SaveChangesAsync();

            // Create parents
            var parents = await CreateParentsAsync(userManager, household.Id, logger);
            
            // Create children with profiles
            var children = await CreateChildrenAsync(userManager, context, household.Id, logger);
            
            // Create chores for each child
            var choresByChild = await CreateChoresAsync(context, children, logger);
            
            // Generate 90 days of chore history with transactions
            await GenerateChoreHistoryAsync(context, children, choresByChild, logger);
            
            // Create schedule overrides (vacations, sick days)
            await CreateScheduleOverridesAsync(context, choresByChild, parents, logger);
            
            // Create savings goals
            await CreateSavingsGoalsAsync(context, children, logger);
            
            // Award some achievements
            await AwardAchievementsAsync(context, children, logger);

            stopwatch.Stop();
            logger.LogInformation("Development data seeding completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            
            // Log summary
            LogDataSummary(context, children, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during development data seeding");
            throw;
        }
    }

    private static async Task<List<ApplicationUser>> CreateParentsAsync(
        UserManager<ApplicationUser> userManager, 
        Guid householdId,
        ILogger logger)
    {
        var parents = new List<ApplicationUser>();
        
        var parentData = new[]
        {
            ("parent_test", "Mom Smith"),
            ("parent_test2", "Dad Smith")
        };

        foreach (var (username, displayName) in parentData)
        {
            var user = new ApplicationUser
            {
                UserName = username,
                Email = $"{username}@test.local",
                EmailConfirmed = true,
                HouseholdId = householdId
            };

            var result = await userManager.CreateAsync(user, ParentPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRolesAsync(user, [SeedData.ParentRole]);
                parents.Add(user);
                logger.LogInformation("Created test parent: {Username}", username);
            }
            else
            {
                logger.LogWarning("Failed to create parent {Username}: {Errors}", 
                    username, string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

        return parents;
    }

    private static async Task<List<(ApplicationUser User, ChildProfile Profile, LedgerAccount Account)>> CreateChildrenAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        Guid householdId,
        ILogger logger)
    {
        var children = new List<(ApplicationUser, ChildProfile, LedgerAccount)>();
        
        // 4 children with varying "ages" (affects completion rates)
        var childData = new[]
        {
            ("emma_test", "Emma", "1234", 0.85), // Oldest, most reliable
            ("noah_test", "Noah", "2345", 0.78), // Second oldest
            ("olivia_test", "Olivia", "3456", 0.72), // Middle child
            ("liam_test", "Liam", "4567", 0.68) // Youngest, learning
        };

        foreach (var (username, displayName, pin, _) in childData)
        {
            var user = new ApplicationUser
            {
                UserName = username,
                Email = $"{username}@test.local",
                EmailConfirmed = true,
                HouseholdId = householdId
            };

            var result = await userManager.CreateAsync(user, ChildPassword);
            if (!result.Succeeded)
            {
                logger.LogWarning("Failed to create child {Username}", username);
                continue;
            }

            await userManager.AddToRoleAsync(user, SeedData.ChildRole);

            // Create child profile
            var profile = new ChildProfile
            {
                UserId = user.Id,
                DisplayName = displayName,
                PinHash = HashPin(pin),
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-100) // Account age for achievements
            };
            context.ChildProfiles.Add(profile);
            await context.SaveChangesAsync();

            // Create ledger account
            var account = new LedgerAccount
            {
                ChildProfileId = profile.Id,
                Name = "Main",
                IsDefault = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-100)
            };
            context.LedgerAccounts.Add(account);
            await context.SaveChangesAsync();

            children.Add((user, profile, account));
            logger.LogInformation("Created test child: {DisplayName} ({Username})", displayName, username);
        }

        return children;
    }

    private static async Task<Dictionary<string, List<ChoreDefinition>>> CreateChoresAsync(
        ApplicationDbContext context,
        List<(ApplicationUser User, ChildProfile Profile, LedgerAccount Account)> children,
        ILogger logger)
    {
        var choresByChild = new Dictionary<string, List<ChoreDefinition>>();
        var baseDate = DateTime.UtcNow.AddDays(-100);

        foreach (var (user, profile, _) in children)
        {
            var chores = new List<ChoreDefinition>();
            var sortOrder = 0;

            // ══════════════════════════════════════════════════════════════
            // DAILY EXPECTATION CHORES (no earn value, penalty on miss)
            // ══════════════════════════════════════════════════════════════
            chores.AddRange(new[]
            {
                new ChoreDefinition
                {
                    Name = "Make Bed",
                    Description = "Make your bed neatly each morning",
                    Icon = "🛏️",
                    AssignedUserId = user.Id,
                    EarnValue = 0m,
                    PenaltyValue = 0m, // Uses family default
                    ScheduleType = ChoreScheduleType.SpecificDays,
                    ActiveDays = DaysOfWeek.All,
                    AutoApprove = true,
                    SortOrder = ++sortOrder,
                    CreatedAt = baseDate
                },
                new ChoreDefinition
                {
                    Name = "Brush Teeth (AM)",
                    Description = "Brush teeth in the morning",
                    Icon = "🪥",
                    AssignedUserId = user.Id,
                    EarnValue = 0m,
                    PenaltyValue = 0m,
                    ScheduleType = ChoreScheduleType.SpecificDays,
                    ActiveDays = DaysOfWeek.All,
                    AutoApprove = true,
                    SortOrder = ++sortOrder,
                    CreatedAt = baseDate
                },
                new ChoreDefinition
                {
                    Name = "Brush Teeth (PM)",
                    Description = "Brush teeth before bed",
                    Icon = "🪥",
                    AssignedUserId = user.Id,
                    EarnValue = 0m,
                    PenaltyValue = 0m,
                    ScheduleType = ChoreScheduleType.SpecificDays,
                    ActiveDays = DaysOfWeek.All,
                    AutoApprove = true,
                    SortOrder = ++sortOrder,
                    CreatedAt = baseDate
                },
                new ChoreDefinition
                {
                    Name = "Tidy Room",
                    Description = "Keep your room clean and organized",
                    Icon = "🧹",
                    AssignedUserId = user.Id,
                    EarnValue = 0m,
                    PenaltyValue = 0m,
                    ScheduleType = ChoreScheduleType.SpecificDays,
                    ActiveDays = DaysOfWeek.All,
                    AutoApprove = false, // Requires approval
                    SortOrder = ++sortOrder,
                    CreatedAt = baseDate
                },
                new ChoreDefinition
                {
                    Name = "Reading Time",
                    Description = "Read for 20-30 minutes",
                    Icon = "📚",
                    AssignedUserId = user.Id,
                    EarnValue = 0m,
                    PenaltyValue = 0m,
                    ScheduleType = ChoreScheduleType.SpecificDays,
                    ActiveDays = DaysOfWeek.All,
                    AutoApprove = true,
                    SortOrder = ++sortOrder,
                    CreatedAt = baseDate
                },
                new ChoreDefinition
                {
                    Name = "Set Table",
                    Description = "Help set the dinner table",
                    Icon = "🍽️",
                    AssignedUserId = user.Id,
                    EarnValue = 0m,
                    PenaltyValue = 0m,
                    ScheduleType = ChoreScheduleType.SpecificDays,
                    ActiveDays = DaysOfWeek.All,
                    AutoApprove = false,
                    SortOrder = ++sortOrder,
                    CreatedAt = baseDate
                }
            });

            // ══════════════════════════════════════════════════════════════
            // EARNING CHORES (specific days)
            // ══════════════════════════════════════════════════════════════
            chores.AddRange(new[]
            {
                new ChoreDefinition
                {
                    Name = "Empty Dishwasher",
                    Description = "Unload clean dishes from the dishwasher",
                    Icon = "🍴",
                    AssignedUserId = user.Id,
                    EarnValue = 0.50m,
                    PenaltyValue = 0m,
                    ScheduleType = ChoreScheduleType.SpecificDays,
                    ActiveDays = DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday,
                    AutoApprove = false,
                    SortOrder = ++sortOrder,
                    CreatedAt = baseDate
                },
                new ChoreDefinition
                {
                    Name = "Trash Duty",
                    Description = "Take out the trash",
                    Icon = "🗑️",
                    AssignedUserId = user.Id,
                    EarnValue = 0.75m,
                    PenaltyValue = 0m,
                    ScheduleType = ChoreScheduleType.SpecificDays,
                    ActiveDays = DaysOfWeek.Tuesday | DaysOfWeek.Friday,
                    AutoApprove = false,
                    SortOrder = ++sortOrder,
                    CreatedAt = baseDate
                },
                new ChoreDefinition
                {
                    Name = "Vacuum Room",
                    Description = "Vacuum your bedroom floor",
                    Icon = "🧽",
                    AssignedUserId = user.Id,
                    EarnValue = 1.00m,
                    PenaltyValue = 0m,
                    ScheduleType = ChoreScheduleType.SpecificDays,
                    ActiveDays = DaysOfWeek.Saturday,
                    AutoApprove = false,
                    SortOrder = ++sortOrder,
                    CreatedAt = baseDate
                }
            });

            // ══════════════════════════════════════════════════════════════
            // WEEKLY FREQUENCY CHORES (flexible scheduling)
            // ══════════════════════════════════════════════════════════════
            chores.AddRange(new[]
            {
                new ChoreDefinition
                {
                    Name = "Feed Pet",
                    Description = "Feed the family pet",
                    Icon = "🐕",
                    AssignedUserId = user.Id,
                    EarnValue = 1.50m,
                    PenaltyValue = 0m,
                    ScheduleType = ChoreScheduleType.WeeklyFrequency,
                    WeeklyTargetCount = 3,
                    ActiveDays = DaysOfWeek.All,
                    IsRepeatable = true, // Can earn more
                    AutoApprove = false,
                    SortOrder = ++sortOrder,
                    CreatedAt = baseDate
                },
                new ChoreDefinition
                {
                    Name = "Yard Work",
                    Description = "Help with lawn or garden",
                    Icon = "🌳",
                    AssignedUserId = user.Id,
                    EarnValue = 2.00m,
                    PenaltyValue = 0m,
                    ScheduleType = ChoreScheduleType.WeeklyFrequency,
                    WeeklyTargetCount = 1,
                    ActiveDays = DaysOfWeek.Weekends,
                    IsRepeatable = false,
                    AutoApprove = false,
                    SortOrder = ++sortOrder,
                    CreatedAt = baseDate
                },
                new ChoreDefinition
                {
                    Name = "Learning Activity",
                    Description = "Educational activity (Duolingo, math games, etc.)",
                    Icon = "🧠",
                    AssignedUserId = user.Id,
                    EarnValue = 0.75m,
                    PenaltyValue = 0m,
                    ScheduleType = ChoreScheduleType.WeeklyFrequency,
                    WeeklyTargetCount = 4,
                    ActiveDays = DaysOfWeek.All,
                    IsRepeatable = false,
                    AutoApprove = true,
                    SortOrder = ++sortOrder,
                    CreatedAt = baseDate
                }
            });

            context.ChoreDefinitions.AddRange(chores);
            choresByChild[user.Id] = chores;
            
            logger.LogInformation("Created {Count} chores for {DisplayName}", chores.Count, profile.DisplayName);
        }

        await context.SaveChangesAsync();
        return choresByChild;
    }

    private static async Task GenerateChoreHistoryAsync(
        ApplicationDbContext context,
        List<(ApplicationUser User, ChildProfile Profile, LedgerAccount Account)> children,
        Dictionary<string, List<ChoreDefinition>> choresByChild,
        ILogger logger)
    {
        // Child-specific completion rates
        var completionRates = new Dictionary<string, double>
        {
            ["emma_test"] = 0.85,
            ["noah_test"] = 0.78,
            ["olivia_test"] = 0.72,
            ["liam_test"] = 0.68
        };

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-90);
        var parentId = (await context.Users.FirstOrDefaultAsync(u => u.UserName == "parent_test"))?.Id;

        var allLogs = new List<ChoreLog>();
        var allTransactions = new List<LedgerTransaction>();

        foreach (var (user, profile, account) in children)
        {
            var chores = choresByChild[user.Id];
            var completionRate = completionRates.GetValueOrDefault(user.UserName ?? "", 0.75);
            var transactionDate = startDate;
            
            logger.LogInformation("Generating 90 days of history for {DisplayName} (rate: {Rate:P0})", 
                profile.DisplayName, completionRate);

            for (var date = startDate; date <= today; date = date.AddDays(1))
            {
                var dayOfWeek = GetDayOfWeekFlag(date);
                var isWeekend = dayOfWeek == DaysOfWeek.Saturday || dayOfWeek == DaysOfWeek.Sunday;
                
                // Track weekly frequency chores
                var weekStart = date.AddDays(-(int)date.DayOfWeek);
                
                foreach (var chore in chores)
                {
                    // Check if chore is scheduled for this day
                    var isScheduled = false;
                    
                    if (chore.ScheduleType == ChoreScheduleType.SpecificDays)
                    {
                        isScheduled = (chore.ActiveDays & dayOfWeek) != 0;
                    }
                    else if (chore.ScheduleType == ChoreScheduleType.WeeklyFrequency)
                    {
                        // For weekly chores, randomly distribute throughout the week
                        // Simplified: schedule on random days up to target count
                        var dayIndex = (int)date.DayOfWeek;
                        var targetSpacing = 7.0 / chore.WeeklyTargetCount;
                        isScheduled = dayIndex % Math.Max(1, (int)targetSpacing) == 0;
                        
                        // Don't exceed weekly target
                        if (!chore.IsRepeatable)
                        {
                            var existingThisWeek = allLogs.Count(l => 
                                l.ChoreDefinitionId == chore.Id && 
                                l.Date >= weekStart && 
                                l.Date < weekStart.AddDays(7));
                            if (existingThisWeek >= chore.WeeklyTargetCount)
                                isScheduled = false;
                        }
                    }

                    if (!isScheduled) continue;

                    // Determine completion based on rate with some variance
                    var dailyVariance = _random.NextDouble() * 0.1 - 0.05; // ±5%
                    var effectiveRate = Math.Clamp(completionRate + dailyVariance, 0.4, 0.95);
                    
                    // Weekends slightly higher completion
                    if (isWeekend) effectiveRate += 0.05;
                    
                    var wasCompleted = _random.NextDouble() < effectiveRate;
                    var wasApproved = wasCompleted && (chore.AutoApprove || _random.NextDouble() < 0.95);
                    
                    // Occasionally request help instead of miss
                    var requestedHelp = !wasCompleted && _random.NextDouble() < 0.15;

                    var status = wasApproved ? ChoreStatus.Approved :
                                 wasCompleted ? ChoreStatus.Completed :
                                 requestedHelp ? ChoreStatus.Help :
                                 ChoreStatus.Missed;

                    var completedAt = wasCompleted 
                        ? date.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(8 + _random.Next(0, 12))))
                        : (DateTime?)null;

                    var log = new ChoreLog
                    {
                        ChoreDefinitionId = chore.Id,
                        Date = date,
                        Status = status,
                        CompletedByUserId = wasCompleted ? user.Id : null,
                        CompletedAt = completedAt?.ToUniversalTime(),
                        ApprovedByUserId = wasApproved ? parentId : null,
                        ApprovedAt = wasApproved ? completedAt?.AddMinutes(_random.Next(5, 120)).ToUniversalTime() : null,
                        HelpReason = requestedHelp ? "I need help with this one" : null,
                        HelpRequestedAt = requestedHelp 
                            ? date.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(18))).ToUniversalTime() 
                            : null,
                        Notes = wasCompleted && _random.NextDouble() < 0.1 
                            ? "Completed early today!" 
                            : null,
                        CreatedAt = date.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(6))).ToUniversalTime()
                    };

                    allLogs.Add(log);

                    // Create transaction for approved earning chores
                    if (status == ChoreStatus.Approved && chore.EarnValue > 0)
                    {
                        allTransactions.Add(new LedgerTransaction
                        {
                            LedgerAccountId = account.Id,
                            UserId = user.Id,
                            Amount = chore.EarnValue,
                            Type = TransactionType.ChoreEarning,
                            Description = $"{chore.Name} completed",
                            TransactionDate = date,
                            CreatedAt = (completedAt ?? DateTime.UtcNow).ToUniversalTime()
                        });
                    }
                    
                    // Create penalty transaction for missed chores (simplified)
                    if (status == ChoreStatus.Missed && chore.EarnValue == 0 && _random.NextDouble() < 0.3)
                    {
                        allTransactions.Add(new LedgerTransaction
                        {
                            LedgerAccountId = account.Id,
                            UserId = user.Id,
                            Amount = -0.25m, // Default penalty
                            Type = TransactionType.ChoreDeduction,
                            Description = $"{chore.Name} missed",
                            TransactionDate = date,
                            CreatedAt = date.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(23))).ToUniversalTime()
                        });
                    }
                }
            }

            // Add some bonus transactions
            var bonusCount = _random.Next(3, 7);
            for (int i = 0; i < bonusCount; i++)
            {
                var bonusDate = startDate.AddDays(_random.Next(0, 90));
                allTransactions.Add(new LedgerTransaction
                {
                    LedgerAccountId = account.Id,
                    UserId = user.Id,
                    Amount = _random.Next(1, 6) * 0.50m,
                    Type = TransactionType.Bonus,
                    Description = "Good behavior bonus",
                    TransactionDate = bonusDate,
                    CreatedAt = bonusDate.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(18))).ToUniversalTime()
                });
            }

            // Add some payout transactions if balance got high
            var runningBalance = allTransactions
                .Where(t => t.UserId == user.Id)
                .Sum(t => t.Amount);
            
            if (runningBalance > 25)
            {
                var payoutAmount = Math.Floor(runningBalance / 10) * 10;
                allTransactions.Add(new LedgerTransaction
                {
                    LedgerAccountId = account.Id,
                    UserId = user.Id,
                    Amount = -payoutAmount,
                    Type = TransactionType.Payout,
                    Description = "Cash out",
                    TransactionDate = today.AddDays(-7),
                    CreatedAt = today.AddDays(-7).ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(12))).ToUniversalTime()
                });
            }
        }

        // Bulk insert for performance
        logger.LogInformation("Inserting {LogCount} chore logs and {TxnCount} transactions...", 
            allLogs.Count, allTransactions.Count);
        
        context.ChoreLogs.AddRange(allLogs);
        await context.SaveChangesAsync();

        // Now we need to link transactions to logs where applicable
        // Update transactions with ChoreLogIds
        foreach (var log in allLogs.Where(l => l.Status == ChoreStatus.Approved))
        {
            var chore = choresByChild.Values.SelectMany(c => c).First(c => c.Id == log.ChoreDefinitionId);
            if (chore.EarnValue > 0)
            {
                var txn = allTransactions.FirstOrDefault(t => 
                    t.UserId == log.CompletedByUserId &&
                    t.TransactionDate == log.Date &&
                    t.Type == TransactionType.ChoreEarning &&
                    t.Description?.Contains(chore.Name) == true &&
                    t.ChoreLogId == null);
                
                if (txn != null)
                {
                    txn.ChoreLogId = log.Id;
                }
            }
        }

        context.LedgerTransactions.AddRange(allTransactions);
        await context.SaveChangesAsync();
        
        logger.LogInformation("Chore history generation complete");
    }

    private static async Task CreateScheduleOverridesAsync(
        ApplicationDbContext context,
        Dictionary<string, List<ChoreDefinition>> choresByChild,
        List<ApplicationUser> parents,
        ILogger logger)
    {
        var overrides = new List<ChoreScheduleOverride>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var parentId = parents.FirstOrDefault()?.Id;
        
        // Track used combinations to prevent duplicate key violations
        var usedCombinations = new HashSet<(int ChoreId, DateOnly Date)>();

        foreach (var (userId, chores) in choresByChild)
        {
            // Create 1-2 vacation periods (skip all chores)
            var vacationStart = today.AddDays(-_random.Next(30, 60));
            var vacationDays = _random.Next(3, 7);
            
            for (int day = 0; day < vacationDays; day++)
            {
                var vacationDate = vacationStart.AddDays(day);
                foreach (var chore in chores.Take(5)) // Only skip some chores
                {
                    // Skip if this combination already exists
                    if (!usedCombinations.Add((chore.Id, vacationDate)))
                        continue;
                    
                    overrides.Add(new ChoreScheduleOverride
                    {
                        ChoreDefinitionId = chore.Id,
                        Date = vacationDate,
                        Type = ScheduleOverrideType.Remove,
                        CreatedByUserId = parentId,
                        CreatedAt = vacationStart.ToDateTime(TimeOnly.MinValue).ToUniversalTime()
                    });
                }
            }

            // Create a few sick days
            var sickDays = _random.Next(2, 5);
            for (int i = 0; i < sickDays; i++)
            {
                var sickDate = today.AddDays(-_random.Next(10, 80));
                foreach (var chore in chores)
                {
                    // Skip if this combination already exists
                    if (!usedCombinations.Add((chore.Id, sickDate)))
                        continue;
                    
                    overrides.Add(new ChoreScheduleOverride
                    {
                        ChoreDefinitionId = chore.Id,
                        Date = sickDate,
                        Type = ScheduleOverrideType.Remove,
                        CreatedByUserId = parentId,
                        CreatedAt = sickDate.ToDateTime(TimeOnly.MinValue).ToUniversalTime()
                    });
                }
            }
        }

        context.ChoreScheduleOverrides.AddRange(overrides);
        await context.SaveChangesAsync();
        
        logger.LogInformation("Created {Count} schedule overrides", overrides.Count);
    }

    private static async Task CreateSavingsGoalsAsync(
        ApplicationDbContext context,
        List<(ApplicationUser User, ChildProfile Profile, LedgerAccount Account)> children,
        ILogger logger)
    {
        var goalTemplates = new[]
        {
            ("Nintendo Switch Game", 60m, "🎮"),
            ("New Headphones", 35m, "🎧"),
            ("Art Supplies", 25m, "🎨"),
            ("Books", 20m, "📚"),
            ("Movie Night Fund", 15m, "🎬"),
            ("Skateboard", 50m, "🛹"),
            ("Lego Set", 40m, "🧱"),
            ("Bike Accessories", 30m, "🚲")
        };

        var goals = new List<SavingsGoal>();

        foreach (var (user, profile, account) in children)
        {
            // Each child gets 2-3 goals
            var childGoals = goalTemplates
                .OrderBy(_ => _random.Next())
                .Take(_random.Next(2, 4))
                .ToList();

            var isPrimary = true;
            var priority = 1;

            foreach (var (name, amount, icon) in childGoals)
            {
                // Vary the target amount slightly
                var adjustedAmount = amount * (0.8m + (decimal)_random.NextDouble() * 0.4m);
                
                // Some goals might be completed
                var isCompleted = _random.NextDouble() < 0.2;

                goals.Add(new SavingsGoal
                {
                    UserId = user.Id,
                    Name = name,
                    Description = $"Saving up for {name.ToLower()}",
                    TargetAmount = Math.Round(adjustedAmount, 2),
                    ImageUrl = null,
                    Priority = priority++,
                    IsPrimary = isPrimary,
                    IsActive = true,
                    IsCompleted = isCompleted,
                    CompletedAt = isCompleted 
                        ? DateTime.UtcNow.AddDays(-_random.Next(5, 30)) 
                        : null,
                    CreatedAt = DateTime.UtcNow.AddDays(-_random.Next(30, 90))
                });

                isPrimary = false; // Only first goal is primary
            }
        }

        context.SavingsGoals.AddRange(goals);
        await context.SaveChangesAsync();
        
        logger.LogInformation("Created {Count} savings goals", goals.Count);
    }

    private static async Task AwardAchievementsAsync(
        ApplicationDbContext context,
        List<(ApplicationUser User, ChildProfile Profile, LedgerAccount Account)> children,
        ILogger logger)
    {
        // Get some achievements to award
        var achievements = await context.Achievements
            .Where(a => a.IsActive && !a.IsHidden)
            .OrderBy(a => a.Points)
            .Take(20)
            .ToListAsync();

        if (achievements.Count == 0)
        {
            logger.LogWarning("No achievements found to award");
            return;
        }

        var userAchievements = new List<UserAchievement>();
        var achievementProgress = new List<AchievementProgress>();

        foreach (var (user, profile, _) in children)
        {
            // Award 5-10 random achievements to each child
            var toAward = achievements
                .OrderBy(_ => _random.Next())
                .Take(_random.Next(5, 11))
                .ToList();

            foreach (var achievement in toAward)
            {
                var earnedDaysAgo = _random.Next(5, 80);
                
                userAchievements.Add(new UserAchievement
                {
                    UserId = user.Id,
                    AchievementId = achievement.Id,
                    EarnedAt = DateTime.UtcNow.AddDays(-earnedDaysAgo),
                    HasSeen = _random.NextDouble() < 0.9 // Most are seen
                });
            }

            // Add progress for some unearned achievements
            var unearnedWithProgress = achievements
                .Except(toAward)
                .Where(a => a.ProgressTarget.HasValue)
                .Take(5)
                .ToList();

            foreach (var achievement in unearnedWithProgress)
            {
                var progress = _random.Next(1, achievement.ProgressTarget!.Value);
                
                achievementProgress.Add(new AchievementProgress
                {
                    UserId = user.Id,
                    AchievementId = achievement.Id,
                    CurrentValue = progress,
                    TargetValue = achievement.ProgressTarget.Value,
                    StartedAt = DateTime.UtcNow.AddDays(-_random.Next(30, 90)),
                    LastUpdatedAt = DateTime.UtcNow.AddDays(-_random.Next(1, 10))
                });
            }
        }

        context.UserAchievements.AddRange(userAchievements);
        context.AchievementProgress.AddRange(achievementProgress);
        await context.SaveChangesAsync();
        
        logger.LogInformation("Awarded {Count} achievements, created {ProgressCount} progress records", 
            userAchievements.Count, achievementProgress.Count);
    }

    private static void LogDataSummary(
        ApplicationDbContext context,
        List<(ApplicationUser User, ChildProfile Profile, LedgerAccount Account)> children,
        ILogger logger)
    {
        logger.LogInformation("═══════════════════════════════════════════════");
        logger.LogInformation("DEV DATA SEEDING SUMMARY");
        logger.LogInformation("═══════════════════════════════════════════════");
        logger.LogInformation("Children: {Count}", children.Count);
        logger.LogInformation("Total Chores: {Count}", context.ChoreDefinitions.Local.Count);
        logger.LogInformation("Total ChoreLogs: {Count}", context.ChoreLogs.Local.Count);
        logger.LogInformation("Total Transactions: {Count}", context.LedgerTransactions.Local.Count);
        logger.LogInformation("Total Overrides: {Count}", context.ChoreScheduleOverrides.Local.Count);
        logger.LogInformation("Total Goals: {Count}", context.SavingsGoals.Local.Count);
        logger.LogInformation("Total Achievements Awarded: {Count}", context.UserAchievements.Local.Count);
        logger.LogInformation("═══════════════════════════════════════════════");
        logger.LogInformation("Test Logins:");
        logger.LogInformation("  Parent: parent_test / {Password}", ParentPassword);
        logger.LogInformation("  Children: emma_test, noah_test, olivia_test, liam_test / {Password}", ChildPassword);
        logger.LogInformation("  Child PINs: 1234, 2345, 3456, 4567");
        logger.LogInformation("═══════════════════════════════════════════════");
    }

    private static DaysOfWeek GetDayOfWeekFlag(DateOnly date)
    {
        return date.DayOfWeek switch
        {
            DayOfWeek.Sunday => DaysOfWeek.Sunday,
            DayOfWeek.Monday => DaysOfWeek.Monday,
            DayOfWeek.Tuesday => DaysOfWeek.Tuesday,
            DayOfWeek.Wednesday => DaysOfWeek.Wednesday,
            DayOfWeek.Thursday => DaysOfWeek.Thursday,
            DayOfWeek.Friday => DaysOfWeek.Friday,
            DayOfWeek.Saturday => DaysOfWeek.Saturday,
            _ => DaysOfWeek.None
        };
    }

    /// <summary>
    /// Hashes a PIN using PBKDF2 with a random salt (matches KidModeService implementation).
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
}
