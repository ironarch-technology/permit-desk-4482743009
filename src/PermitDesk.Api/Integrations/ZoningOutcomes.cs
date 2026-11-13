using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Jobs;

namespace PermitDesk.Api.Integrations;

/// <summary>
/// Applies a zoning result to its application. Reached from the webhook and
/// from reconciliation, so the same result can turn up twice.
/// </summary>
public class ZoningOutcomes
{
    private readonly PermitDeskDbContext _db;
    private readonly ILogger<ZoningOutcomes> _log;

    public ZoningOutcomes(PermitDeskDbContext db, ILogger<ZoningOutcomes> log)
    {
        _db = db;
        _log = log;
    }

    /// <returns>False when the handle is not one of ours.</returns>
    public async Task<bool> ApplyAsync(string handle, string result, string? reason, CancellationToken ct)
    {
        var check = await _db.ZoningChecks.AsNoTracking().FirstOrDefaultAsync(c => c.Handle == handle, ct);
        if (check == null) return false;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Whoever flips the row from pending owns the outcome.
        var claimed = await _db.ZoningChecks
            .Where(c => c.Id == check.Id && c.Status == ZoningCheck.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, result)
                .SetProperty(c => c.Reason, reason)
                .SetProperty(c => c.ResolvedAt, DateTime.UtcNow), ct);

        if (claimed == 0) return true;

        var application = await _db.Applications.FirstAsync(a => a.Id == check.ApplicationId, ct);
        if (application.State != ApplicationStates.ZoningCheck)
        {
            // Withdrawn while the check was out. Keep the result, change nothing.
            await tx.CommitAsync(ct);
            return true;
        }

        switch (result)
        {
            case "pass":
                application.State = ApplicationStates.PlanReview;
                AuditTrail.Append(_db, application.Id, ApplicationStates.ZoningCheck, ApplicationStates.PlanReview,
                    AuditTrail.SystemActor, AuditTrail.SourceZoning, reason ?? "Permitted under current zoning");
                JobQueue.Enqueue(_db, JobKinds.ReviewSubmit, new ApplicationJob(application.Id));
                break;

            case "fail":
                application.State = ApplicationStates.Denied;
                AuditTrail.Append(_db, application.Id, ApplicationStates.ZoningCheck, ApplicationStates.Denied,
                    AuditTrail.SystemActor, AuditTrail.SourceZoning, reason ?? "Not permitted under current zoning");
                Notifier.Queue(_db, application.Id, Notifier.Denied);
                break;

            case "indeterminate":
                application.State = ApplicationStates.ZoningHold;
                AuditTrail.Append(_db, application.Id, ApplicationStates.ZoningCheck, ApplicationStates.ZoningHold,
                    AuditTrail.SystemActor, AuditTrail.SourceZoning, reason ?? "Parcel records conflict");
                Notifier.Queue(_db, application.Id, Notifier.ZoningHold);
                break;

            default:
                throw new InvalidOperationException($"Zoning returned unknown result '{result}' for {handle}");
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _log.LogInformation("Zoning check {Handle} applied to application {ApplicationId}: {Result}",
            handle, application.Id, result);
        return true;
    }
}
