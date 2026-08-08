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
        // Kind keeps the ledger honest: the stats tiles report Bonus and
        // Penalty separately, so collapsing them into Adjustment (what the
        // first native sheet did) silently zeroed two of the six numbers.
        // Bonus and Penalty take the magnitude — their sign is their meaning.
        var reason = request.Description.Trim();
        var result = request.Kind?.Trim().ToLowerInvariant() switch
        {
            "bonus" => await _payout.AddBonusAsync(
                target.User!.Id, Math.Abs(request.Amount), _currentUser.UserId, reason),
            "penalty" => await _payout.AddPenaltyAsync(
                target.User!.Id, Math.Abs(request.Amount), _currentUser.UserId, reason),
            null or "" or "adjustment" => await _payout.AddAdjustmentAsync(
                target.User!.Id, request.Amount, _currentUser.UserId, reason),
            _ => ServiceResult.Fail($"Unknown adjustment kind '{request.Kind}'."),
        };
        if (!result.Success)
        {
            return BadRequest(new ApiError("AdjustFailed", result.ErrorMessage ?? "Could not adjust the balance."));
        }

        var balance = await _ledgerService.GetUserBalanceAsync(target.User!.Id);
        return Ok(new BalanceResponse(target.User!.Id, balance));
    }

    /// <summary>
    /// Record a cash-out — bookkeeping only; the actual money moves in the
    /// family's banking app. Parents may record one for any household child,
    /// and a child may record their own (the web's My Balance allowed the
    /// same). PayoutService enforces the real rules: positive amount, within
    /// balance, balance at or over the family threshold.
    /// </summary>
    [HttpPost("cashout")]
    public async Task<ActionResult<BalanceResponse>> CashOut(
        [FromBody] LedgerCashOutRequest request, CancellationToken ct)
    {
        var target = await _guard.ResolveTargetUserAsync(request.UserId, ct);
        if (target.Outcome == GuardOutcome.Forbidden) return Forbid(JwtBearerDefaults.AuthenticationScheme);
        if (target.Outcome == GuardOutcome.NotFound) return NotFound(new ApiError("UserNotFound", "User not found."));

        await _currentUser.InitializeAsync();
        var result = await _payout.ProcessCashOutAsync(
            target.User!.Id, request.Amount, _currentUser.UserId,
            string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim());
        if (!result.Success)
        {
            return BadRequest(new ApiError("CashOutFailed", result.ErrorMessage ?? "Could not record the cash-out."));
        }

        var balance = await _ledgerService.GetUserBalanceAsync(target.User!.Id);
        return Ok(new BalanceResponse(target.User!.Id, balance));
    }

    /// <summary>
    /// The money picture behind the history: lifetime totals by type, the
    /// family's cash-out threshold, and whether the balance clears it.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<LedgerSummaryResponse>> Summary(
        [FromQuery] string? userId, CancellationToken ct)
    {
        var target = await _guard.ResolveTargetUserAsync(userId, ct);
        if (target.Outcome == GuardOutcome.Forbidden) return Forbid(JwtBearerDefaults.AuthenticationScheme);
        if (target.Outcome == GuardOutcome.NotFound) return NotFound(new ApiError("UserNotFound", "User not found."));

        var stats = await _ledgerService.GetUserTransactionStatsAsync(target.User!.Id);
        var threshold = await _payout.GetCashOutThresholdAsync();
        return Ok(new LedgerSummaryResponse(
            target.User!.Id,
            stats.NetBalance,
            threshold,
            stats.NetBalance >= threshold,
            stats.TotalEarnings,
            stats.TotalDeductions,
            stats.TotalBonuses,
            stats.TotalPenalties,
            stats.TotalPayouts,
            stats.TransactionCount));
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
