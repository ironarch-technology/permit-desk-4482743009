using PermitDesk.Api.Domain;
using PermitDesk.Api.Models;

namespace PermitDesk.Tests;

public class ApplicationMapperTests
{
    private static readonly DateTime Now = new(2026, 3, 10, 15, 0, 0, DateTimeKind.Utc);

    private static PermitApplication Sample(string state = ApplicationStates.PlanReview) => new()
    {
        Id = 42,
        Reference = "MP-2026-000042",
        ApplicantUserId = 7,
        State = state,
        ParcelAddress = "112 Beacon Hill Road",
        WorkType = "addition",
        ScopeOfWork = "Single-storey rear addition with a new bathroom.",
        DeclaredValuation = 88_000,
        ContactName = "Dana Okafor",
        ContactEmail = "dana.okafor@example.test",
        ContactPhone = "555-0114",
        SubmittedAt = Now.AddDays(-3),
        UpdatedAt = Now.AddDays(-1)
    };

    private static ApplicationDetail Detail(
        PermitApplication application,
        IEnumerable<Transition>? transitions = null,
        IEnumerable<CorrectionItem>? items = null,
        IEnumerable<CorrectionResponse>? responses = null,
        FeeQuote? quote = null,
        Permit? permit = null,
        IEnumerable<Inspection>? inspections = null) =>
        ApplicationMapper.ToDetail(
            application,
            transitions ?? Array.Empty<Transition>(),
            items ?? Array.Empty<CorrectionItem>(),
            responses ?? Array.Empty<CorrectionResponse>(),
            quote,
            permit,
            inspections ?? Array.Empty<Inspection>(),
            Now);

    [Theory]
    [InlineData(ApplicationStates.Draft, "Draft")]
    [InlineData(ApplicationStates.Submitted, "Submitted")]
    [InlineData(ApplicationStates.ZoningCheck, "Zoning check")]
    [InlineData(ApplicationStates.ZoningHold, "Zoning hold")]
    [InlineData(ApplicationStates.PlanReview, "Plan review")]
    [InlineData(ApplicationStates.CorrectionsRequired, "Corrections required")]
    [InlineData(ApplicationStates.FeesAssessed, "Fees due")]
    [InlineData(ApplicationStates.Issued, "Permit issued")]
    [InlineData(ApplicationStates.Denied, "Denied")]
    [InlineData(ApplicationStates.Withdrawn, "Withdrawn")]
    [InlineData(ApplicationStates.Expired, "Expired")]
    public void StateLabel_has_a_label_for_every_state(string state, string expected)
    {
        Assert.Equal(expected, ApplicationMapper.StateLabel(state));
    }

    [Fact]
    public void StateLabel_covers_all_known_states()
    {
        foreach (var state in ApplicationStates.All)
        {
            Assert.NotEqual(state, ApplicationMapper.StateLabel(state));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StateLabel_is_unknown_for_missing_state(string? state)
    {
        Assert.Equal("Unknown", ApplicationMapper.StateLabel(state));
    }

    [Fact]
    public void StateLabel_falls_back_to_the_raw_value()
    {
        Assert.Equal("on_appeal", ApplicationMapper.StateLabel("on_appeal"));
    }

    [Theory]
    [InlineData("applicant:7", "Applicant")]
    [InlineData("technician:3", "Permit technician")]
    [InlineData("plan-reviewer", "Plan reviewer")]
    [InlineData("system", "Permit Desk")]
    [InlineData("something-else", "Permit Desk")]
    [InlineData("", "Permit Desk")]
    [InlineData(null, "Permit Desk")]
    public void ActorLabel_hides_identifiers(string? actor, string expected)
    {
        Assert.Equal(expected, ApplicationMapper.ActorLabel(actor));
    }

    [Fact]
    public void Summary_copies_the_listing_fields()
    {
        var summary = ApplicationMapper.ToSummary(Sample());

        Assert.Equal(42, summary.Id);
        Assert.Equal("MP-2026-000042", summary.Reference);
        Assert.Equal(ApplicationStates.PlanReview, summary.State);
        Assert.Equal("Plan review", summary.StateLabel);
        Assert.Equal("112 Beacon Hill Road", summary.ParcelAddress);
        Assert.Equal("addition", summary.WorkType);
        Assert.Equal(88_000, summary.DeclaredValuation);
        Assert.Equal(Now.AddDays(-3), summary.SubmittedAt);
        Assert.Equal(Now.AddDays(-1), summary.UpdatedAt);
    }

    [Fact]
    public void Summary_of_a_bare_draft_has_nulls()
    {
        var summary = ApplicationMapper.ToSummary(new PermitApplication { Id = 1, Reference = "MP-2026-000001" });

        Assert.Equal("Draft", summary.StateLabel);
        Assert.Null(summary.ParcelAddress);
        Assert.Null(summary.WorkType);
        Assert.Null(summary.DeclaredValuation);
        Assert.Null(summary.SubmittedAt);
    }

    [Fact]
    public void StaffRow_includes_contact_details()
    {
        var application = Sample();
        application.ContractorLicenceNumber = "MPL-204417";

        var row = ApplicationMapper.ToStaffRow(application);

        Assert.Equal("Dana Okafor", row.ApplicantName);
        Assert.Equal("dana.okafor@example.test", row.ContactEmail);
        Assert.Equal("555-0114", row.ContactPhone);
        Assert.Equal("MPL-204417", row.ContractorLicenceNumber);
        Assert.Equal("Single-storey rear addition with a new bathroom.", row.ScopeOfWork);
    }

    [Fact]
    public void Detail_copies_contact_details()
    {
        var detail = Detail(Sample());

        Assert.Equal("Dana Okafor", detail.Contact.Name);
        Assert.Equal("dana.okafor@example.test", detail.Contact.Email);
        Assert.Equal("555-0114", detail.Contact.Phone);
    }

    [Fact]
    public void Detail_has_no_contractor_block_for_a_resident()
    {
        Assert.Null(Detail(Sample()).Contractor);
    }

    [Fact]
    public void Detail_has_a_contractor_block_for_a_contractor()
    {
        var application = Sample();
        application.ApplicantType = "contractor";
        application.ContractorLicenceNumber = "MPL-204417";
        application.ContractorLicenceExpiry = new DateOnly(2027, 6, 30);

        var detail = Detail(application);

        Assert.NotNull(detail.Contractor);
        Assert.Equal("MPL-204417", detail.Contractor!.LicenceNumber);
        Assert.Equal(new DateOnly(2027, 6, 30), detail.Contractor.LicenceExpiry);
    }

    [Theory]
    [InlineData(ApplicationStates.Denied, true)]
    [InlineData(ApplicationStates.Withdrawn, true)]
    [InlineData(ApplicationStates.Expired, true)]
    [InlineData(ApplicationStates.Issued, false)]
    [InlineData(ApplicationStates.PlanReview, false)]
    public void Detail_flags_terminal_states(string state, bool terminal)
    {
        Assert.Equal(terminal, Detail(Sample(state)).Terminal);
    }

    [Fact]
    public void Timeline_is_ordered_oldest_first()
    {
        var transitions = new[]
        {
            new Transition { Id = 2, ToState = ApplicationStates.Submitted, Actor = "applicant:7", Source = "portal", OccurredAt = Now.AddHours(-2) },
            new Transition { Id = 3, ToState = ApplicationStates.ZoningCheck, Actor = "system", Source = "permit-desk", OccurredAt = Now.AddHours(-1) },
            new Transition { Id = 1, ToState = ApplicationStates.Draft, Actor = "applicant:7", Source = "portal", OccurredAt = Now.AddHours(-3) },
        };

        var timeline = Detail(Sample(), transitions).Timeline;

        Assert.Equal(new[] { "draft", "submitted", "zoning_check" }, timeline.Select(t => t.ToState));
    }

    [Fact]
    public void Timeline_entries_carry_label_actor_and_reason()
    {
        var transitions = new[]
        {
            new Transition
            {
                Id = 1, FromState = ApplicationStates.ZoningCheck, ToState = ApplicationStates.PlanReview,
                Actor = "system", Source = "zoning", Reason = "Permitted under R2", OccurredAt = Now
            }
        };

        var entry = Assert.Single(Detail(Sample(), transitions).Timeline);

        Assert.Equal("Plan review", entry.Label);
        Assert.Equal("Permit Desk", entry.Actor);
        Assert.Equal("zoning", entry.Source);
        Assert.Equal("Permitted under R2", entry.Reason);
        Assert.Equal(ApplicationStates.ZoningCheck, entry.FromState);
    }

    [Fact]
    public void Timeline_breaks_ties_by_id()
    {
        var transitions = new[]
        {
            new Transition { Id = 9, ToState = ApplicationStates.PlanReview, Actor = "system", Source = "zoning", OccurredAt = Now },
            new Transition { Id = 8, ToState = ApplicationStates.ZoningCheck, Actor = "system", Source = "permit-desk", OccurredAt = Now },
        };

        var timeline = Detail(Sample(), transitions).Timeline;

        Assert.Equal(ApplicationStates.ZoningCheck, timeline[0].ToState);
    }

    [Fact]
    public void Outstanding_corrections_are_empty_outside_the_corrections_state()
    {
        var items = new[] { new CorrectionItem { Id = 1, Cycle = 0, Code = "EGR-102", Narrative = "n", Citation = "c" } };

        var detail = Detail(Sample(ApplicationStates.PlanReview), items: items);

        Assert.Empty(detail.OutstandingCorrections);
        Assert.Single(detail.CorrectionHistory);
    }

    [Fact]
    public void Outstanding_corrections_are_the_current_cycle()
    {
        var application = Sample(ApplicationStates.CorrectionsRequired);
        application.CorrectionCycle = 1;
        var items = new[]
        {
            new CorrectionItem { Id = 1, Cycle = 0, Code = "EGR-102", Narrative = "n", Citation = "c" },
            new CorrectionItem { Id = 2, Cycle = 1, Code = "ENR-330", Narrative = "n", Citation = "c" },
        };

        var detail = Detail(application, items: items);

        var outstanding = Assert.Single(detail.OutstandingCorrections);
        Assert.Equal("ENR-330", outstanding.Code);
        Assert.Equal(2, detail.CorrectionHistory.Count);
    }

    [Fact]
    public void Correction_carries_its_response()
    {
        var items = new[] { new CorrectionItem { Id = 1, Cycle = 0, Code = "EGR-102", Narrative = "n", Citation = "c" } };
        var responses = new[] { new CorrectionResponse { ItemCode = "EGR-102", Body = "See sheet A2.", RespondedAt = Now } };

        var correction = Assert.Single(Detail(Sample(), items: items, responses: responses).CorrectionHistory);

        Assert.Equal("See sheet A2.", correction.Response);
        Assert.Equal(Now, correction.RespondedAt);
    }

    [Fact]
    public void Correction_without_a_response_has_nulls()
    {
        var items = new[] { new CorrectionItem { Id = 1, Cycle = 0, Code = "EGR-102", Narrative = "n", Citation = "c" } };

        var correction = Assert.Single(Detail(Sample(), items: items).CorrectionHistory);

        Assert.Null(correction.Response);
        Assert.Null(correction.RespondedAt);
    }

    [Fact]
    public void FeeQuote_is_absent_when_there_is_none()
    {
        Assert.Null(Detail(Sample()).FeeQuote);
    }

    [Fact]
    public void FeeQuote_lines_are_parsed_from_the_breakdown()
    {
        var quote = new FeeQuote
        {
            ExternalId = "q-1",
            Amount = 420.5,
            Breakdown = "[{\"code\":\"BASE\",\"label\":\"Permit base fee\",\"amount\":410},{\"code\":\"TECH\",\"label\":\"Records technology fee\",\"amount\":10.5}]",
            ExpiresAt = Now.AddDays(1)
        };

        var model = ApplicationMapper.ToFeeQuote(quote, Now);

        Assert.Equal("q-1", model.QuoteId);
        Assert.Equal(420.5, model.Amount);
        Assert.Equal(2, model.Lines.Count);
        Assert.Equal("BASE", model.Lines[0].Code);
        Assert.Equal(10.5, model.Lines[1].Amount);
        Assert.False(model.Expired);
    }

    [Fact]
    public void FeeQuote_with_an_empty_breakdown_has_no_lines()
    {
        var model = ApplicationMapper.ToFeeQuote(new FeeQuote { ExternalId = "q-2", Breakdown = "", ExpiresAt = Now.AddDays(1) }, Now);

        Assert.Empty(model.Lines);
    }

    [Fact]
    public void FeeQuote_is_expired_at_its_expiry_time()
    {
        var model = ApplicationMapper.ToFeeQuote(new FeeQuote { ExternalId = "q-3", ExpiresAt = Now }, Now);

        Assert.True(model.Expired);
    }

    [Fact]
    public void Permit_is_mapped_when_present()
    {
        var permit = new Permit { PermitNumber = "BP-2026-04100", IssuedAt = Now, ValidUntil = Now.AddDays(180) };

        var detail = Detail(Sample(ApplicationStates.Issued), permit: permit);

        Assert.Equal("BP-2026-04100", detail.Permit!.PermitNumber);
        Assert.Equal(Now.AddDays(180), detail.Permit.ValidUntil);
    }

    [Fact]
    public void Inspections_are_ordered_by_start_time()
    {
        var inspections = new[]
        {
            new Inspection { Id = 2, InspectionType = "final", SlotStart = Now.AddDays(20), Status = "confirmed" },
            new Inspection { Id = 1, InspectionType = "framing", SlotStart = Now.AddDays(5), Status = "pending" },
        };

        var detail = Detail(Sample(ApplicationStates.Issued), inspections: inspections);

        Assert.Equal(new[] { "framing", "final" }, detail.Inspections.Select(i => i.InspectionType));
    }
}
