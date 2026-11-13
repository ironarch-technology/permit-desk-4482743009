using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Integrations;

namespace PermitDesk.Api.Jobs;

public class ZoningDispatchHandler : IJobHandler
{
    private readonly PermitDeskDbContext _db;
    private readonly ZoningClient _zoning;
    private readonly ILogger<ZoningDispatchHandler> _log;

    public ZoningDispatchHandler(PermitDeskDbContext db, ZoningClient zoning, ILogger<ZoningDispatchHandler> log)
    {
        _db = db;
        _zoning = zoning;
        _log = log;
    }

    public string Kind => JobKinds.ZoningDispatch;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var job = JobQueue.Read<ApplicationJob>(payload);
        var application = await _db.Applications.Include(a => a.Parcel)
            .FirstOrDefaultAsync(a => a.Id == job.ApplicationId, ct);

        // Withdrawn before we got to it, or already dispatched by an earlier attempt.
        if (application == null || application.State != ApplicationStates.Submitted) return;

        var handle = await _zoning.SubmitCheckAsync(
            application.Parcel!.ParcelNumber, application.WorkType!, application.ScopeOfWork!, ct);

        _db.ZoningChecks.Add(new ZoningCheck { ApplicationId = application.Id, Handle = handle });
        application.State = ApplicationStates.ZoningCheck;
        AuditTrail.Append(_db, application.Id, ApplicationStates.Submitted, ApplicationStates.ZoningCheck,
            AuditTrail.SystemActor, AuditTrail.SourceSystem, $"Zoning check {handle} requested");

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Application {ApplicationId} dispatched to zoning as {Handle}", application.Id, handle);
    }
}

public class ReviewSubmitHandler : IJobHandler
{
    private readonly PermitDeskDbContext _db;
    private readonly ReviewClient _review;
    private readonly ILogger<ReviewSubmitHandler> _log;

    public ReviewSubmitHandler(PermitDeskDbContext db, ReviewClient review, ILogger<ReviewSubmitHandler> log)
    {
        _db = db;
        _review = review;
        _log = log;
    }

    public string Kind => JobKinds.ReviewSubmit;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var job = JobQueue.Read<ApplicationJob>(payload);
        var application = await _db.Applications.FirstOrDefaultAsync(a => a.Id == job.ApplicationId, ct);
        if (application == null || application.State != ApplicationStates.PlanReview) return;

        var alreadyQueued = await _db.ReviewSubmissions.AnyAsync(
            r => r.ApplicationId == application.Id && r.Cycle == application.CorrectionCycle, ct);
        if (alreadyQueued) return;

        var responses = await _db.CorrectionResponses
            .Where(r => r.ApplicationId == application.Id)
            .Select(r => new ReviewResponse(r.ItemCode, r.Body))
            .ToListAsync(ct);

        var reviewId = await _review.SubmitAsync(new ReviewRequest(
            application.Reference,
            application.CorrectionCycle,
            application.WorkType!,
            application.DeclaredValuation ?? 0,
            application.ScopeOfWork!,
            responses), ct);

        if (reviewId == null)
        {
            throw new InvalidOperationException($"Plan review did not accept application {application.Reference}");
        }

        _db.ReviewSubmissions.Add(new ReviewSubmission
        {
            ApplicationId = application.Id,
            Cycle = application.CorrectionCycle,
            ExternalId = reviewId
        });

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Application {ApplicationId} queued for plan review as {ReviewId} (cycle {Cycle})",
            application.Id, reviewId, application.CorrectionCycle);
    }
}

public class FeeQuoteHandler : IJobHandler
{
    private readonly PermitDeskDbContext _db;
    private readonly FeeQuoteService _quotes;

    public FeeQuoteHandler(PermitDeskDbContext db, FeeQuoteService quotes)
    {
        _db = db;
        _quotes = quotes;
    }

    public string Kind => JobKinds.FeeQuote;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var job = JobQueue.Read<ApplicationJob>(payload);
        var application = await _db.Applications.Include(a => a.Parcel)
            .FirstOrDefaultAsync(a => a.Id == job.ApplicationId, ct);
        if (application == null) return;

        var now = DateTime.UtcNow;
        var hasLiveQuote = await _db.FeeQuotes.AnyAsync(q => q.ApplicationId == application.Id && q.ExpiresAt > now, ct);
        if (hasLiveQuote) return;

        await _quotes.IssueAsync(application, "Plan review approved", ct);
        Notifier.Queue(_db, application.Id, Notifier.FeesAssessed);
        await _db.SaveChangesAsync(ct);
    }
}

public class InspectionConfirmHandler : IJobHandler
{
    private readonly PermitDeskDbContext _db;
    private readonly SchedulingClient _scheduling;

    public InspectionConfirmHandler(PermitDeskDbContext db, SchedulingClient scheduling)
    {
        _db = db;
        _scheduling = scheduling;
    }

    public string Kind => JobKinds.InspectionConfirm;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var job = JobQueue.Read<InspectionJob>(payload);
        var inspection = await _db.Inspections.FirstOrDefaultAsync(i => i.ExternalId == job.BookingId, ct);
        if (inspection == null || inspection.Status != "pending") return;

        var booking = await _scheduling.GetBookingAsync(inspection.ExternalId, ct);
        if (!booking.Ok)
        {
            throw new InvalidOperationException($"Could not read booking {inspection.ExternalId}: {booking.Error}");
        }

        if (booking.Value!.Status == "pending")
        {
            throw new InvalidOperationException($"Booking {inspection.ExternalId} is not confirmed yet");
        }

        inspection.Status = booking.Value.Status;
        await _db.SaveChangesAsync(ct);
    }
}

public class NotificationHandler : IJobHandler
{
    private readonly PermitDeskDbContext _db;
    private readonly ILogger<NotificationHandler> _log;

    public NotificationHandler(PermitDeskDbContext db, ILogger<NotificationHandler> log)
    {
        _db = db;
        _log = log;
    }

    public string Kind => JobKinds.Notify;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var job = JobQueue.Read<NotifyJob>(payload);
        var application = await _db.Applications.FirstOrDefaultAsync(a => a.Id == job.ApplicationId, ct);
        if (application?.ContactEmail == null) return;

        // The city relay picks rows up from this table; locally nothing drains it.
        _db.Notifications.Add(new Notification
        {
            ApplicationId = application.Id,
            Recipient = application.ContactEmail,
            Template = job.Template,
            SentAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Notification {Template} handed to relay for application {ApplicationId}",
            job.Template, application.Id);
    }
}
