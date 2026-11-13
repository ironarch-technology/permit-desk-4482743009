using PermitDesk.Api.Data;

namespace PermitDesk.Api.Domain;

/// <summary>
/// Append-only record of who moved an application and why. Rows are never
/// updated; the applicant's status timeline is read straight from here.
/// </summary>
public static class AuditTrail
{
    public const string SourcePortal = "portal";
    public const string SourceCounter = "counter";
    public const string SourceZoning = "zoning";
    public const string SourceReview = "review";
    public const string SourceCashiering = "cashiering";
    public const string SourceSystem = "permit-desk";

    public const string SystemActor = "system";

    public static void Append(
        PermitDeskDbContext db,
        int applicationId,
        string? fromState,
        string toState,
        string actor,
        string source,
        string? reason)
    {
        db.Transitions.Add(new Transition
        {
            ApplicationId = applicationId,
            FromState = fromState,
            ToState = toState,
            Actor = actor,
            Source = source,
            Reason = reason,
            OccurredAt = DateTime.UtcNow
        });
    }
}
