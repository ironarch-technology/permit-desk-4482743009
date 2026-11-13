using PermitDesk.Api.Integrations;

namespace PermitDesk.Api.Models;

public record ApplicationSummary(
    int Id,
    string Reference,
    string State,
    string StateLabel,
    string? ParcelAddress,
    string? WorkType,
    double? DeclaredValuation,
    DateTime? SubmittedAt,
    DateTime UpdatedAt);

public record ContactModel(string? Name, string? Email, string? Phone);

public record ContractorModel(string? LicenceNumber, DateOnly? LicenceExpiry);

public record TimelineEntry(DateTime OccurredAt, string? FromState, string ToState, string Label, string Actor, string Source, string? Reason);

public record CorrectionModel(int Id, int Cycle, string Code, string Narrative, string Citation, string? Response, DateTime? RespondedAt);

public record FeeQuoteModel(string QuoteId, double Amount, IReadOnlyList<FeeLine> Lines, DateTime ExpiresAt, bool Expired);

public record PermitModel(string PermitNumber, DateTime IssuedAt, DateTime ValidUntil);

public record InspectionModel(int Id, string InspectionType, DateTime SlotStart, string Status);

public record ApplicationDetail(
    int Id,
    string Reference,
    string State,
    string StateLabel,
    bool Terminal,
    string? ParcelAddress,
    string? WorkType,
    string? ScopeOfWork,
    double? DeclaredValuation,
    string ApplicantType,
    ContactModel Contact,
    ContractorModel? Contractor,
    int CorrectionCycle,
    DateTime? SubmittedAt,
    IReadOnlyList<TimelineEntry> Timeline,
    IReadOnlyList<CorrectionModel> OutstandingCorrections,
    IReadOnlyList<CorrectionModel> CorrectionHistory,
    FeeQuoteModel? FeeQuote,
    PermitModel? Permit,
    IReadOnlyList<InspectionModel> Inspections);

public record StaffApplicationRow(
    int Id,
    string Reference,
    string State,
    string StateLabel,
    string? ParcelAddress,
    string? WorkType,
    string? ScopeOfWork,
    double? DeclaredValuation,
    string? ApplicantName,
    string? ContactEmail,
    string? ContactPhone,
    string? ContractorLicenceNumber,
    DateTime? SubmittedAt);

public class DraftRequest
{
    public string? ParcelAddress { get; set; }
    public string? WorkType { get; set; }
    public string? ScopeOfWork { get; set; }
    public double? DeclaredValuation { get; set; }
    public string? ApplicantType { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContractorLicenceNumber { get; set; }
    public DateOnly? ContractorLicenceExpiry { get; set; }
}

public class SubmissionRequest : DraftRequest
{
    public int? DraftId { get; set; }
    public string? SubmissionKey { get; set; }
}

public record WithdrawRequest(string? Reason);

public record CorrectionResponseInput(int ItemId, string? Response);

public record CorrectionResponsesRequest(List<CorrectionResponseInput>? Responses);

public record BookingRequest(string? SlotId, string? InspectionType);

public record ZoningResolutionRequest(string? Outcome, string? Note);

public record LoginRequest(string? Email, string? Password);
