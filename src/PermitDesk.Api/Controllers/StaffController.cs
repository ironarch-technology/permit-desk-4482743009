using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Auth;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Jobs;
using PermitDesk.Api.Models;

namespace PermitDesk.Api.Controllers;

/// <summary>
/// Front-counter tools for permit technicians.
/// </summary>
[Authorize]
[Route("api/staff")]
public class StaffController : PortalController
{
    private readonly PermitDeskDbContext _db;
    private readonly ILogger<StaffController> _log;

    public StaffController(PermitDeskDbContext db, ILogger<StaffController> log)
    {
        _db = db;
        _log = log;
    }

    [HttpGet("applications")]
    public async Task<IActionResult> Search(
        [FromQuery] string? parcel,
        [FromQuery] string? state,
        [FromQuery] string? applicant,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var query = _db.Applications.AsNoTracking().Where(a => a.State != ApplicationStates.Draft);

        if (!string.IsNullOrWhiteSpace(parcel))
        {
            var pattern = $"%{parcel.Trim()}%";
            query = query.Where(a => EF.Functions.ILike(a.ParcelAddress!, pattern));
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(a => a.State == state);
        }

        if (!string.IsNullOrWhiteSpace(applicant))
        {
            var pattern = $"%{applicant.Trim()}%";
            query = query.Where(a => EF.Functions.ILike(a.ContactName!, pattern));
        }

        if (from != null)
        {
            var start = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(a => a.SubmittedAt >= start);
        }

        if (to != null)
        {
            var end = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(a => a.SubmittedAt < end);
        }

        var results = await query.OrderByDescending(a => a.SubmittedAt).ToListAsync(ct);
        return Ok(results.Select(ApplicationMapper.ToStaffRow));
    }

    /// <summary>
    /// Settles an application that zoning could not decide automatically.
    /// </summary>
    [HttpPost("applications/{id:int}/zoning-resolution")]
    [Authorize(Roles = Roles.Technician)]
    public async Task<IActionResult> ResolveZoningHold(int id, [FromBody] ZoningResolutionRequest request, CancellationToken ct)
    {
        var application = await _db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (application == null) return NotFound();

        var target = request.Outcome switch
        {
            "pass" => ApplicationStates.PlanReview,
            "fail" => ApplicationStates.Denied,
            _ => null
        };

        if (target == null) return Invalid("Choose whether the work is permitted.");
        if (string.IsNullOrWhiteSpace(request.Note)) return Invalid("Record the reason for the decision.");

        if (application.State != ApplicationStates.ZoningHold || !LifecycleRules.CanTransition(application.State, target))
        {
            return Conflict(new { error = "This application is not on zoning hold." });
        }

        application.State = target;
        AuditTrail.Append(_db, id, ApplicationStates.ZoningHold, target, Actor, AuditTrail.SourceCounter, request.Note.Trim());

        if (target == ApplicationStates.PlanReview)
        {
            JobQueue.Enqueue(_db, JobKinds.ReviewSubmit, new ApplicationJob(id));
        }
        else
        {
            Notifier.Queue(_db, id, Notifier.Denied);
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Zoning hold on application {ApplicationId} resolved as {Outcome} by {Actor}", id, request.Outcome, Actor);

        return Ok(await BuildDetailAsync(_db, application, ct));
    }

    [HttpPost("applications/{id:int}/withdraw")]
    [Authorize(Roles = Roles.Technician)]
    public async Task<IActionResult> WithdrawOnBehalf(int id, [FromBody] WithdrawRequest request, CancellationToken ct)
    {
        var application = await _db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (application == null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Reason)) return Invalid("Record why the application is being withdrawn.");

        if (!LifecycleRules.CanTransition(application.State, ApplicationStates.Withdrawn))
        {
            return Conflict(new { error = "This application can no longer be withdrawn." });
        }

        var from = application.State;
        application.State = ApplicationStates.Withdrawn;
        AuditTrail.Append(_db, id, from, ApplicationStates.Withdrawn, Actor, AuditTrail.SourceCounter, request.Reason.Trim());
        Notifier.Queue(_db, id, Notifier.Withdrawn);
        await _db.SaveChangesAsync(ct);

        return Ok(await BuildDetailAsync(_db, application, ct));
    }
}
