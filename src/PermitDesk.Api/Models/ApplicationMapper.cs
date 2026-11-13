using System.Text.Json;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Integrations;
using PermitDesk.Api.Jobs;

namespace PermitDesk.Api.Models;

public static class ApplicationMapper
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        [ApplicationStates.Draft] = "Draft",
        [ApplicationStates.Submitted] = "Submitted",
        [ApplicationStates.ZoningCheck] = "Zoning check",
        [ApplicationStates.ZoningHold] = "Zoning hold",
        [ApplicationStates.PlanReview] = "Plan review",
        [ApplicationStates.CorrectionsRequired] = "Corrections required",
        [ApplicationStates.FeesAssessed] = "Fees due",
        [ApplicationStates.Issued] = "Permit issued",
        [ApplicationStates.Denied] = "Denied",
        [ApplicationStates.Withdrawn] = "Withdrawn",
        [ApplicationStates.Expired] = "Expired",
    };

    public static string StateLabel(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return "Unknown";
        return Labels.TryGetValue(state, out var label) ? label : state;
    }

    public static string ActorLabel(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return "Permit Desk";

        var role = actor.Split(':')[0];
        return role switch
        {
            "applicant" => "Applicant",
            "technician" => "Permit technician",
            "plan-reviewer" => "Plan reviewer",
            "system" => "Permit Desk",
            _ => "Permit Desk"
        };
    }

    public static ApplicationSummary ToSummary(PermitApplication a) =>
        new(a.Id, a.Reference, a.State, StateLabel(a.State), a.ParcelAddress, a.WorkType,
            a.DeclaredValuation, a.SubmittedAt, a.UpdatedAt);

    public static StaffApplicationRow ToStaffRow(PermitApplication a) =>
        new(a.Id, a.Reference, a.State, StateLabel(a.State), a.ParcelAddress, a.WorkType, a.ScopeOfWork,
            a.DeclaredValuation, a.ContactName, a.ContactEmail, a.ContactPhone, a.ContractorLicenceNumber,
            a.SubmittedAt);

    public static TimelineEntry ToTimelineEntry(Transition t) =>
        new(t.OccurredAt, t.FromState, t.ToState, StateLabel(t.ToState), ActorLabel(t.Actor), t.Source, t.Reason);

    public static FeeQuoteModel ToFeeQuote(FeeQuote quote, DateTime utcNow)
    {
        var lines = string.IsNullOrWhiteSpace(quote.Breakdown)
            ? new List<FeeLine>()
            : JsonSerializer.Deserialize<List<FeeLine>>(quote.Breakdown, JobQueue.Json) ?? new List<FeeLine>();

        return new FeeQuoteModel(quote.ExternalId, quote.Amount, lines, quote.ExpiresAt, quote.IsExpired(utcNow));
    }

    public static ApplicationDetail ToDetail(
        PermitApplication a,
        IEnumerable<Transition> transitions,
        IEnumerable<CorrectionItem> items,
        IEnumerable<CorrectionResponse> responses,
        FeeQuote? quote,
        Permit? permit,
        IEnumerable<Inspection> inspections,
        DateTime utcNow)
    {
        var responsesByCode = responses.ToDictionary(r => r.ItemCode);

        var corrections = items
            .OrderBy(i => i.Cycle).ThenBy(i => i.Id)
            .Select(i =>
            {
                responsesByCode.TryGetValue(i.Code, out var response);
                return new CorrectionModel(i.Id, i.Cycle, i.Code, i.Narrative, i.Citation, response?.Body, response?.RespondedAt);
            })
            .ToList();

        var outstanding = a.State == ApplicationStates.CorrectionsRequired
            ? corrections.Where(c => c.Cycle == a.CorrectionCycle).ToList()
            : new List<CorrectionModel>();

        var isContractor = a.ApplicantType == "contractor";

        return new ApplicationDetail(
            a.Id,
            a.Reference,
            a.State,
            StateLabel(a.State),
            LifecycleRules.IsTerminal(a.State),
            a.ParcelAddress,
            a.WorkType,
            a.ScopeOfWork,
            a.DeclaredValuation,
            a.ApplicantType,
            new ContactModel(a.ContactName, a.ContactEmail, a.ContactPhone),
            isContractor ? new ContractorModel(a.ContractorLicenceNumber, a.ContractorLicenceExpiry) : null,
            a.CorrectionCycle,
            a.SubmittedAt,
            transitions.OrderBy(t => t.OccurredAt).ThenBy(t => t.Id).Select(ToTimelineEntry).ToList(),
            outstanding,
            corrections,
            quote == null ? null : ToFeeQuote(quote, utcNow),
            permit == null ? null : new PermitModel(permit.PermitNumber, permit.IssuedAt, permit.ValidUntil),
            inspections.OrderBy(i => i.SlotStart)
                .Select(i => new InspectionModel(i.Id, i.InspectionType, i.SlotStart, i.Status))
                .ToList());
    }
}
