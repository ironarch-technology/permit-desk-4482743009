using System.Text.Json;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;

namespace PermitDesk.Api.Jobs;

public record ApplicationJob(int ApplicationId);

public record NotifyJob(int ApplicationId, string Template);

public record InspectionJob(string BookingId);

public interface IJobHandler
{
    string Kind { get; }

    Task HandleAsync(string payload, CancellationToken ct);
}

public static class JobKinds
{
    public const string ZoningDispatch = "zoning.dispatch";
    public const string ReviewSubmit = "review.submit";
    public const string FeeQuote = "fees.quote";
    public const string InspectionConfirm = "inspection.confirm";
    public const string Notify = "notify";
}

/// <summary>
/// Adds work to the queue as part of the caller's unit of work. The row only
/// exists if the change that needed it commits.
/// </summary>
public static class JobQueue
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Enqueue(PermitDeskDbContext db, string kind, object payload, TimeSpan? delay = null)
    {
        db.Jobs.Add(new Job
        {
            Kind = kind,
            Payload = JsonSerializer.Serialize(payload, Json),
            RunAt = DateTime.UtcNow.Add(delay ?? TimeSpan.Zero)
        });
    }

    public static T Read<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, Json)
        ?? throw new InvalidOperationException($"Job payload could not be read as {typeof(T).Name}");
}

public static class Notifier
{
    public const string Submitted = "application_submitted";
    public const string CorrectionsRequired = "corrections_required";
    public const string FeesAssessed = "fees_assessed";
    public const string Issued = "permit_issued";
    public const string Denied = "application_denied";
    public const string Withdrawn = "application_withdrawn";
    public const string ZoningHold = "zoning_hold";

    public static void Queue(PermitDeskDbContext db, int applicationId, string template) =>
        JobQueue.Enqueue(db, JobKinds.Notify, new NotifyJob(applicationId, template));
}
