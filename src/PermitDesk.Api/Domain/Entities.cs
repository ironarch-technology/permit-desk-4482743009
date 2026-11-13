namespace PermitDesk.Api.Domain;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public string Role { get; set; } = Auth.Roles.Applicant;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Parcel
{
    public int Id { get; set; }
    public string ParcelNumber { get; set; } = "";
    public string Address { get; set; } = "";
    public string District { get; set; } = "";
    public string ZoningCode { get; set; } = "";
}

public class WorkType
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public double MinValuation { get; set; }
    public double MaxValuation { get; set; }
    public int SortOrder { get; set; }
}

public class PermitApplication
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public int ApplicantUserId { get; set; }
    public User? Applicant { get; set; }
    public string State { get; set; } = ApplicationStates.Draft;
    public int? ParcelId { get; set; }
    public Parcel? Parcel { get; set; }
    public string? ParcelAddress { get; set; }
    public string? WorkType { get; set; }
    public string? ScopeOfWork { get; set; }
    public double? DeclaredValuation { get; set; }
    public string ApplicantType { get; set; } = "resident";
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContractorLicenceNumber { get; set; }
    public DateOnly? ContractorLicenceExpiry { get; set; }
    public string? SubmissionKey { get; set; }
    public int CorrectionCycle { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Transition
{
    public long Id { get; set; }
    public int ApplicationId { get; set; }
    public string? FromState { get; set; }
    public string ToState { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Source { get; set; } = "";
    public string? Reason { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class ZoningCheck
{
    public const string Pending = "pending";

    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public string Handle { get; set; } = "";
    public string Status { get; set; } = Pending;
    public string? Reason { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

public class ReviewSubmission
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public int Cycle { get; set; }
    public string ExternalId { get; set; } = "";
    public string? Outcome { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
}

public class CorrectionItem
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public int Cycle { get; set; }
    public string Code { get; set; } = "";
    public string Narrative { get; set; } = "";
    public string Citation { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CorrectionResponse
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public string ItemCode { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime RespondedAt { get; set; } = DateTime.UtcNow;
}

public class FeeQuote
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public PermitApplication? Application { get; set; }
    public string ExternalId { get; set; } = "";
    public double Amount { get; set; }
    public string Breakdown { get; set; } = "[]";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsExpired(DateTime utcNow) => ExpiresAt <= utcNow;
}

public class Payment
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public PermitApplication? Application { get; set; }
    public int FeeQuoteId { get; set; }
    public string ExternalId { get; set; } = "";
    public double Amount { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime? CapturedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Permit
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public string PermitNumber { get; set; } = "";
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime ValidUntil { get; set; }
}

public class Job
{
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTime RunAt { get; set; } = DateTime.UtcNow;
    public int Attempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public class Inspection
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public string InspectionType { get; set; } = "";
    public string SlotId { get; set; } = "";
    public DateTime SlotStart { get; set; }
    public string ExternalId { get; set; } = "";
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Notification
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public string Recipient { get; set; } = "";
    public string Template { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}
