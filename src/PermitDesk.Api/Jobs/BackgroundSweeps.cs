using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Integrations;

namespace PermitDesk.Api.Jobs;

/// <summary>
/// Zoning tells us about results by webhook, and the webhook is not dependable.
/// Anything still pending after a grace period is read back directly.
/// </summary>
public class ZoningReconciler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ZoningReconciler> _log;
    private readonly TimeSpan _grace;

    public ZoningReconciler(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ZoningReconciler> log)
    {
        _scopes = scopes;
        _log = log;
        _grace = TimeSpan.FromSeconds(configuration.GetValue("Zoning:ReconcileAfterSeconds", 360));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _log.LogError(ex, "Zoning reconciliation sweep failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PermitDeskDbContext>();
        var zoning = scope.ServiceProvider.GetRequiredService<ZoningClient>();
        var outcomes = scope.ServiceProvider.GetRequiredService<ZoningOutcomes>();

        var cutoff = DateTime.UtcNow.Subtract(_grace);
        var overdue = await db.ZoningChecks.AsNoTracking()
            .Where(c => c.Status == ZoningCheck.Pending && c.RequestedAt < cutoff)
            .OrderBy(c => c.RequestedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var check in overdue)
        {
            var current = await zoning.GetCheckAsync(check.Handle, ct);
            if (current.Status != "complete" || current.Result == null) continue;

            _log.LogInformation("Zoning check {Handle} reconciled by read-back as {Result}", check.Handle, current.Result);
            await outcomes.ApplyAsync(check.Handle, current.Result, current.Reason, ct);
        }
    }
}

/// <summary>
/// Plan Review has no callback. Outstanding submissions are polled until a
/// reviewer has decided.
/// </summary>
public class ReviewPoller : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ReviewPoller> _log;
    private readonly TimeSpan _interval;

    public ReviewPoller(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ReviewPoller> log)
    {
        _scopes = scopes;
        _log = log;
        _interval = TimeSpan.FromSeconds(configuration.GetValue("Review:PollSeconds", 120));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await PollAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _log.LogError(ex, "Plan review poll failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PermitDeskDbContext>();
        var review = scope.ServiceProvider.GetRequiredService<ReviewClient>();

        var outstanding = await db.ReviewSubmissions
            .Where(r => r.Outcome == null)
            .OrderBy(r => r.SubmittedAt)
            .ToListAsync(ct);

        foreach (var submission in outstanding)
        {
            var decision = await review.GetDecisionAsync(submission.ExternalId, ct);
            if (decision == null) continue;

            var application = await db.Applications.FirstAsync(a => a.Id == submission.ApplicationId, ct);
            var from = application.State;

            submission.Outcome = decision.Outcome;
            submission.DecidedAt = DateTime.UtcNow;

            switch (decision.Outcome)
            {
                case "approved":
                    JobQueue.Enqueue(db, JobKinds.FeeQuote, new ApplicationJob(application.Id));
                    break;

                case "denied":
                    application.State = ApplicationStates.Denied;
                    AuditTrail.Append(db, application.Id, from, ApplicationStates.Denied,
                        "plan-reviewer", AuditTrail.SourceReview, decision.Reason ?? "Denied at plan review");
                    Notifier.Queue(db, application.Id, Notifier.Denied);
                    break;

                case "corrections_required":
                    foreach (var item in decision.Items ?? new List<ReviewCorrection>())
                    {
                        db.CorrectionItems.Add(new CorrectionItem
                        {
                            ApplicationId = application.Id,
                            Cycle = submission.Cycle,
                            Code = item.Code,
                            Narrative = item.Narrative,
                            Citation = item.Citation
                        });
                    }

                    application.State = ApplicationStates.CorrectionsRequired;
                    AuditTrail.Append(db, application.Id, from, ApplicationStates.CorrectionsRequired,
                        "plan-reviewer", AuditTrail.SourceReview,
                        $"{decision.Items?.Count ?? 0} correction item(s) returned in review {submission.ExternalId}");
                    Notifier.Queue(db, application.Id, Notifier.CorrectionsRequired);
                    break;

                default:
                    _log.LogWarning("Review {ReviewId} returned unrecognised outcome {Outcome}",
                        submission.ExternalId, decision.Outcome);
                    submission.Outcome = null;
                    submission.DecidedAt = null;
                    continue;
            }

            await db.SaveChangesAsync(ct);
            _log.LogInformation("Review {ReviewId} for application {ApplicationId} decided: {Outcome}",
                submission.ExternalId, application.Id, decision.Outcome);
        }
    }
}

/// <summary>
/// Moves issued permits to expired once their validity window has passed.
/// </summary>
public class PermitExpirySweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PermitExpirySweeper> _log;

    public PermitExpirySweeper(IServiceScopeFactory scopes, ILogger<PermitExpirySweeper> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        try
        {
            do
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _log.LogError(ex, "Permit expiry sweep failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PermitDeskDbContext>();
        var now = DateTime.UtcNow;

        var lapsed = await (
            from application in db.Applications
            where application.State == ApplicationStates.Issued
            where !db.Permits.Any(p => p.ApplicationId == application.Id && p.ValidUntil > now)
            select application).ToListAsync(ct);

        foreach (var application in lapsed)
        {
            application.State = ApplicationStates.Expired;
            AuditTrail.Append(db, application.Id, ApplicationStates.Issued, ApplicationStates.Expired,
                AuditTrail.SystemActor, AuditTrail.SourceSystem, "Validity window ended without a final inspection");
        }

        if (lapsed.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _log.LogInformation("Expired {Count} permit(s)", lapsed.Count);
        }
    }
}
