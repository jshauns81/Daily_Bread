using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// Savings goals. Children manage their own; parents may manage a household
/// member's goals via userId.
/// </summary>
[ApiController]
[Route("api/v1/goals")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class GoalsController : ControllerBase
{
    private readonly ISavingsGoalService _goalService;
    private readonly IHouseholdGuard _guard;

    public GoalsController(ISavingsGoalService goalService, IHouseholdGuard guard)
    {
        _goalService = goalService;
        _guard = guard;
    }

    private static GoalDto Map(SavingsGoalProgress g) => new(
        g.Id, g.Name, g.Description, g.TargetAmount, g.CurrentBalance,
        g.ProgressPercent, g.Priority, g.IsPrimary, g.IsCompleted, g.ImageUrl);

    private async Task<(string? UserId, ActionResult? Error)> ResolveAsync(string? userId, CancellationToken ct)
    {
        var target = await _guard.ResolveTargetUserAsync(userId, ct);
        return target.Outcome switch
        {
            GuardOutcome.Forbidden => (null, Forbid(JwtBearerDefaults.AuthenticationScheme)),
            GuardOutcome.NotFound => (null, NotFound(new ApiError("UserNotFound", "User not found."))),
            _ => (target.User!.Id, null)
        };
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GoalDto>>> List([FromQuery] string? userId, CancellationToken ct)
    {
        var (targetId, error) = await ResolveAsync(userId, ct);
        if (error != null) return error;

        var goals = await _goalService.GetGoalsWithProgressAsync(targetId!);
        return Ok(goals.Select(Map).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<GoalDto>> Create([FromBody] GoalWriteRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.TargetAmount <= 0)
        {
            return BadRequest(new ApiError("InvalidGoal", "A goal needs a name and a positive target amount."));
        }

        var (targetId, error) = await ResolveAsync(request.UserId, ct);
        if (error != null) return error;

        var result = await _goalService.CreateGoalAsync(targetId!, new SavingsGoalDto
        {
            Name = request.Name.Trim(),
            Description = request.Description,
            TargetAmount = request.TargetAmount,
            ImageUrl = request.ImageUrl,
            Priority = request.Priority,
            IsPrimary = request.IsPrimary
        });

        if (!result.Success)
        {
            return BadRequest(new ApiError("CreateFailed", result.ErrorMessage ?? "Could not create the goal."));
        }

        var created = await _goalService.GetGoalByIdAsync(result.Data, targetId!);
        return created == null
            ? NoContent()
            : Ok(Map(created));
    }

    [HttpPut("{goalId:int}")]
    public async Task<IActionResult> Update(int goalId, [FromBody] GoalWriteRequest request, CancellationToken ct)
    {
        var (targetId, error) = await ResolveAsync(request.UserId, ct);
        if (error != null) return error;

        var result = await _goalService.UpdateGoalAsync(targetId!, new SavingsGoalDto
        {
            Id = goalId,
            Name = request.Name.Trim(),
            Description = request.Description,
            TargetAmount = request.TargetAmount,
            ImageUrl = request.ImageUrl,
            Priority = request.Priority,
            IsPrimary = request.IsPrimary
        });

        if (!result.Success)
        {
            return BadRequest(new ApiError("UpdateFailed", result.ErrorMessage ?? "Could not update the goal."));
        }

        return NoContent();
    }

    [HttpPost("{goalId:int}/primary")]
    public async Task<IActionResult> SetPrimary(int goalId, [FromQuery] string? userId, CancellationToken ct)
    {
        var (targetId, error) = await ResolveAsync(userId, ct);
        if (error != null) return error;

        var result = await _goalService.SetPrimaryGoalAsync(targetId!, goalId);
        if (!result.Success)
        {
            return BadRequest(new ApiError("SetPrimaryFailed", result.ErrorMessage ?? "Could not set the primary goal."));
        }

        return NoContent();
    }

    [HttpDelete("{goalId:int}")]
    public async Task<IActionResult> Delete(int goalId, [FromQuery] string? userId, CancellationToken ct)
    {
        var (targetId, error) = await ResolveAsync(userId, ct);
        if (error != null) return error;

        var result = await _goalService.DeleteGoalAsync(targetId!, goalId);
        if (!result.Success)
        {
            return BadRequest(new ApiError("DeleteFailed", result.ErrorMessage ?? "Could not delete the goal."));
        }

        return NoContent();
    }
}
