using Daily_Bread.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Daily_Bread.Api.Controllers;

/// <summary>
/// Balance and transaction history. Children see their own; parents may query
/// household members.
/// </summary>
[ApiController]
[Route("api/v1/ledger")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class LedgerController : ControllerBase
{
    private readonly ILedgerService _ledgerService;
    private readonly IHouseholdGuard _guard;
    private readonly IPayoutService _payout;
    private readonly ICurrentUserContext _currentUser;

    public LedgerController(
        ILedgerService ledgerService,
        IHouseholdGuard guard,
        IPayoutService payout,
        ICurrentUserContext currentUser)
    {
        _ledgerService = ledgerService;
        _guard = guard;
        _payout = payout;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Parent add/subtract on a child's balance. Amount is signed (negative
    /// subtracts); Description is the audited reason. Parent/Admin only; the
    /// child is guarded to the caller's household.
    /// </summary>
    [HttpPost("adjust")]
    [Authorize(Roles = "Parent,Admin")]
    public async Task<ActionResult<BalanceResponse>> Adjust([FromBody] LedgerAdjustRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return BadRequest(new ApiError("MissingReason", "A reason for the adjustment is required."));
        }
        if (request.Amount == 0)
        {
            return BadRequest(new ApiError("ZeroAmount", "Enter a non-zero amount."));
        }

        var target = await _guard.ResolveTargetUserAsync(request.UserId, ct);
        if (target.Outcome == GuardOutcome.Forbidden) return Forbid(JwtBearerDefaults.AuthenticationScheme);
        if (target.Outcome == GuardOutcome.NotFound) return NotFound(new ApiError("UserNotFound", "User not found."));

        await _currentUser.InitializeAsync();
        var result = await _payout.AddAdjustmentAsync(
            target.User!.Id, request.Amount, _currentUser.UserId, request.Description.Trim());
        if (!result.Success)
        {
            return BadRequest(new ApiError("AdjustFailed", result.ErrorMessage ?? "Could not adjust the balance."));
        }

        var balance = await _ledgerService.GetUserBalanceAsync(target.User!.Id);
        return Ok(new BalanceResponse(target.User!.Id, balance));
    }

    [HttpGet("balance")]
    public async Task<ActionResult<BalanceResponse>> Balance([FromQuery] string? userId, CancellationToken ct)
    {
        var target = await _guard.ResolveTargetUserAsync(userId, ct);
        if (target.Outcome == GuardOutcome.Forbidden)
        {
            return Forbid(JwtBearerDefaults.AuthenticationScheme);
        }
        if (target.Outcome == GuardOutcome.NotFound)
        {
            return NotFound(new ApiError("UserNotFound", "User not found."));
        }

        var balance = await _ledgerService.GetUserBalanceAsync(target.User!.Id);
        return Ok(new BalanceResponse(target.User!.Id, balance));
    }

    /// <summary>Transaction history, newest first, optionally bounded by date.</summary>
    [HttpGet("history")]
    public async Task<ActionResult<LedgerHistoryResponse>> History(
        [FromQuery] string? userId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var target = await _guard.ResolveTargetUserAsync(userId, ct);
        if (target.Outcome == GuardOutcome.Forbidden)
        {
            return Forbid(JwtBearerDefaults.AuthenticationScheme);
        }
        if (target.Outcome == GuardOutcome.NotFound)
        {
            return NotFound(new ApiError("UserNotFound", "User not found."));
        }

        limit = Math.Clamp(limit, 1, 200);
        var transactions = await _ledgerService.GetUserTransactionsAsync(target.User!.Id, from, to);

        var dtos = transactions
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.Id)
            .Take(limit)
            .Select(t => new TransactionDto(
                t.Id,
                t.Amount,
                t.Type.ToString(),
                t.Description,
                t.TransactionDate,
                t.ChoreDefinitionId))
            .ToList();

        return Ok(new LedgerHistoryResponse(target.User!.Id, dtos));
    }
}
