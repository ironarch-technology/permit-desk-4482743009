namespace PermitDesk.Api.Domain;

public static class ApplicationStates
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string ZoningCheck = "zoning_check";
    public const string ZoningHold = "zoning_hold";
    public const string PlanReview = "plan_review";
    public const string CorrectionsRequired = "corrections_required";
    public const string FeesAssessed = "fees_assessed";
    public const string Issued = "issued";
    public const string Denied = "denied";
    public const string Withdrawn = "withdrawn";
    public const string Expired = "expired";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Draft, Submitted, ZoningCheck, ZoningHold, PlanReview, CorrectionsRequired,
        FeesAssessed, Issued, Denied, Withdrawn, Expired
    };
}
