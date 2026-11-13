using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Auth;
using PermitDesk.Api.Domain;

namespace PermitDesk.Api.Data;

/// <summary>
/// Local development data: a handful of accounts, the Mountport parcels the
/// mock zoning service knows about, and applications at different stages.
/// </summary>
public static class SeedData
{
    public const string DevelopmentPassword = "mountport-dev";

    public static async Task RunAsync(PermitDeskDbContext db, IPasswordHasher<User> hasher, ILogger log)
    {
        if (await db.Users.AnyAsync())
        {
            log.LogInformation("Seed data already present, nothing to do");
            return;
        }

        User NewUser(string email, string name, string phone, string role)
        {
            var user = new User { Email = email, FullName = name, Phone = phone, Role = role };
            user.PasswordHash = hasher.HashPassword(user, DevelopmentPassword);
            return user;
        }

        var dana = NewUser("dana.okafor@example.test", "Dana Okafor", "555-0114", Roles.Applicant);
        var rui = NewUser("rui@castellanosbuilding.example.test", "Rui Castellanos", "555-0167", Roles.Applicant);
        var priya = NewUser("priya.nair@example.test", "Priya Nair", "555-0139", Roles.Applicant);
        var marcus = NewUser("marcus.webb@mountport.example.test", "Marcus Webb", "555-0100", Roles.Technician);
        db.Users.AddRange(dana, rui, priya, marcus);

        var parcels = new[]
        {
            new Parcel { ParcelNumber = "NR-0140-22", Address = "14 Larkspur Lane", District = "North Ridge", ZoningCode = "R1" },
            new Parcel { ParcelNumber = "NR-0310-07", Address = "31 Alder Court", District = "North Ridge", ZoningCode = "R1" },
            new Parcel { ParcelNumber = "NR-0260-15", Address = "26 Windmill Close", District = "North Ridge", ZoningCode = "R1" },
            new Parcel { ParcelNumber = "EB-1120-03", Address = "112 Beacon Hill Road", District = "Eastbank", ZoningCode = "R2" },
            new Parcel { ParcelNumber = "EB-0830-11", Address = "83 Orchard Terrace", District = "Eastbank", ZoningCode = "R2" },
            new Parcel { ParcelNumber = "OH-2080-01", Address = "208 Harbour Street", District = "Old Harbour", ZoningCode = "C2" },
            new Parcel { ParcelNumber = "OH-0090-04", Address = "9 Cooper's Yard", District = "Old Harbour", ZoningCode = "C1" },
            new Parcel { ParcelNumber = "OH-4500-02", Address = "450 Ferry Landing", District = "Old Harbour", ZoningCode = "C2" },
            new Parcel { ParcelNumber = "MW-0770-09", Address = "77 Foundry Road", District = "Millworks", ZoningCode = "M1" },
            new Parcel { ParcelNumber = "MW-3010-05", Address = "301 Granary Street", District = "Millworks", ZoningCode = "M1" },
            new Parcel { ParcelNumber = "MW-0180-13", Address = "18 Tannery Row", District = "Millworks", ZoningCode = "M1" },
            new Parcel { ParcelNumber = "EB-0050-00", Address = "5 Saltmarsh Way", District = "Eastbank", ZoningCode = "OS" },
        };
        db.Parcels.AddRange(parcels);

        db.WorkTypes.AddRange(
            new WorkType { Code = "deck_patio", Label = "Deck or patio", MinValuation = 500, MaxValuation = 75_000, SortOrder = 1 },
            new WorkType { Code = "reroof", Label = "Re-roofing", MinValuation = 500, MaxValuation = 150_000, SortOrder = 2 },
            new WorkType { Code = "interior_remodel", Label = "Interior remodel", MinValuation = 1_000, MaxValuation = 400_000, SortOrder = 3 },
            new WorkType { Code = "addition", Label = "Addition or extension", MinValuation = 5_000, MaxValuation = 750_000, SortOrder = 4 },
            new WorkType { Code = "new_construction", Label = "New construction", MinValuation = 50_000, MaxValuation = 5_000_000, SortOrder = 5 },
            new WorkType { Code = "commercial_fitout", Label = "Commercial fit-out", MinValuation = 5_000, MaxValuation = 2_000_000, SortOrder = 6 },
            new WorkType { Code = "demolition", Label = "Demolition", MinValuation = 1_000, MaxValuation = 250_000, SortOrder = 7 });

        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        Parcel At(string address) => parcels.Single(p => p.Address == address);

        PermitApplication NewApplication(int number, User owner, Parcel parcel, string workType, string scope, double valuation, string state, DateTime created)
        {
            var isContractor = owner == rui;
            return new PermitApplication
            {
                Reference = $"MP-{created:yyyy}-{number:D6}",
                ApplicantUserId = owner.Id,
                State = state,
                ParcelId = parcel.Id,
                ParcelAddress = parcel.Address,
                WorkType = workType,
                ScopeOfWork = scope,
                DeclaredValuation = valuation,
                ApplicantType = isContractor ? "contractor" : "resident",
                ContactName = owner.FullName,
                ContactEmail = owner.Email,
                ContactPhone = owner.Phone,
                ContractorLicenceNumber = isContractor ? "MPL-204417" : null,
                ContractorLicenceExpiry = isContractor ? DateOnly.FromDateTime(now.AddMonths(14)) : null,
                SubmittedAt = state == ApplicationStates.Draft ? null : created.AddMinutes(12),
                CreatedAt = created,
                UpdatedAt = created
            };
        }

        var draft = NewApplication(2301, dana, At("14 Larkspur Lane"), "deck_patio",
            "Replace the existing rear deck with a 4m x 5m timber deck on concrete piers, including steps to the garden.",
            18_500, ApplicationStates.Draft, now.AddDays(-2));

        var issued = NewApplication(2214, dana, At("14 Larkspur Lane"), "reroof",
            "Strip and replace asphalt shingles on the main house roof. No structural changes to rafters or sheathing.",
            24_000, ApplicationStates.Issued, now.AddDays(-41));

        var corrections = NewApplication(2288, priya, At("83 Orchard Terrace"), "interior_remodel",
            "Remove the wall between kitchen and dining room, install a flush beam, and relocate the kitchen sink and range.",
            62_000, ApplicationStates.CorrectionsRequired, now.AddDays(-9));

        var hold = NewApplication(2295, rui, At("18 Tannery Row"), "commercial_fitout",
            "Fit out the ground floor of the former tannery office as a bakery and cafe with seating for 24 and a new accessible washroom.",
            185_000, ApplicationStates.ZoningHold, now.AddDays(-4));

        var denied = NewApplication(2251, rui, At("5 Saltmarsh Way"), "new_construction",
            "Construct a single-storey boat storage building with a concrete slab and steel frame beside the existing slipway.",
            340_000, ApplicationStates.Denied, now.AddDays(-23));

        db.Applications.AddRange(draft, issued, corrections, hold, denied);
        await db.SaveChangesAsync();

        void Step(PermitApplication application, double minutes, string? from, string to, string actor, string source, string reason) =>
            db.Transitions.Add(new Transition
            {
                ApplicationId = application.Id,
                FromState = from,
                ToState = to,
                Actor = actor,
                Source = source,
                Reason = reason,
                OccurredAt = application.CreatedAt.AddMinutes(minutes)
            });

        void Intake(PermitApplication application, string handle)
        {
            var applicant = $"applicant:{application.ApplicantUserId}";
            Step(application, 0, null, ApplicationStates.Draft, applicant, AuditTrail.SourcePortal, "Draft started");
            Step(application, 12, ApplicationStates.Draft, ApplicationStates.Submitted, applicant, AuditTrail.SourcePortal, "Draft submitted from the portal");
            Step(application, 13, ApplicationStates.Submitted, ApplicationStates.ZoningCheck, AuditTrail.SystemActor, AuditTrail.SourceSystem, $"Zoning check {handle} requested");
        }

        Step(draft, 0, null, ApplicationStates.Draft, $"applicant:{dana.Id}", AuditTrail.SourcePortal, "Draft started");

        Intake(issued, "zc-seed-2214");
        Step(issued, 14, ApplicationStates.ZoningCheck, ApplicationStates.PlanReview, AuditTrail.SystemActor, AuditTrail.SourceZoning, "Permitted under current zoning");
        Step(issued, 2 * 1440, ApplicationStates.PlanReview, ApplicationStates.FeesAssessed, AuditTrail.SystemActor, AuditTrail.SourceCashiering, "Plan review approved. Fee quote q-seed-2214 issued");
        Step(issued, 2 * 1440 + 95, ApplicationStates.FeesAssessed, ApplicationStates.Issued, AuditTrail.SystemActor, AuditTrail.SourceCashiering, "Payment pay-seed-2214 captured. Permit BP-2026-04087 issued");

        var issuedAt = issued.CreatedAt.AddMinutes(2 * 1440 + 95);
        var seededQuote = new FeeQuote
        {
            ApplicationId = issued.Id,
            ExternalId = "q-seed-2214",
            Amount = 512.25,
            Breakdown = "[{\"code\":\"BASE\",\"label\":\"Permit base fee\",\"amount\":180},{\"code\":\"VAL\",\"label\":\"Valuation fee\",\"amount\":300},{\"code\":\"DIST\",\"label\":\"North Ridge district surcharge\",\"amount\":24},{\"code\":\"TECH\",\"label\":\"Records technology fee\",\"amount\":8.25}]",
            ExpiresAt = issued.CreatedAt.AddDays(32),
            CreatedAt = issued.CreatedAt.AddDays(2)
        };
        db.FeeQuotes.Add(seededQuote);
        db.ZoningChecks.Add(new ZoningCheck { ApplicationId = issued.Id, Handle = "zc-seed-2214", Status = "pass", RequestedAt = issued.CreatedAt.AddMinutes(13), ResolvedAt = issued.CreatedAt.AddMinutes(14) });
        db.ReviewSubmissions.Add(new ReviewSubmission { ApplicationId = issued.Id, Cycle = 0, ExternalId = "rv-seed-2214", Outcome = "approved", SubmittedAt = issued.CreatedAt.AddMinutes(14), DecidedAt = issued.CreatedAt.AddDays(2) });

        Intake(corrections, "zc-seed-2288");
        Step(corrections, 14, ApplicationStates.ZoningCheck, ApplicationStates.PlanReview, AuditTrail.SystemActor, AuditTrail.SourceZoning, "Permitted under current zoning");
        Step(corrections, 4 * 1440, ApplicationStates.PlanReview, ApplicationStates.CorrectionsRequired, "plan-reviewer", AuditTrail.SourceReview, "2 correction item(s) returned in review rv-seed-2288");
        db.ZoningChecks.Add(new ZoningCheck { ApplicationId = corrections.Id, Handle = "zc-seed-2288", Status = "pass", RequestedAt = corrections.CreatedAt.AddMinutes(13), ResolvedAt = corrections.CreatedAt.AddMinutes(14) });
        db.ReviewSubmissions.Add(new ReviewSubmission { ApplicationId = corrections.Id, Cycle = 0, ExternalId = "rv-seed-2288", Outcome = "corrections_required", SubmittedAt = corrections.CreatedAt.AddMinutes(14), DecidedAt = corrections.CreatedAt.AddDays(4) });
        db.CorrectionItems.AddRange(
            new CorrectionItem { ApplicationId = corrections.Id, Cycle = 0, Code = "STR-210", Narrative = "Provide beam sizing calculations and bearing details for the new flush beam, stamped by a structural engineer.", Citation = "Mountport Building Code 16.04.210", CreatedAt = corrections.CreatedAt.AddDays(4) },
            new CorrectionItem { ApplicationId = corrections.Id, Cycle = 0, Code = "PLB-118", Narrative = "Show the drain, waste and vent layout for the relocated kitchen sink, including pipe sizes and slope.", Citation = "Mountport Plumbing Code 18.02.118", CreatedAt = corrections.CreatedAt.AddDays(4) });

        Intake(hold, "zc-seed-2295");
        Step(hold, 15, ApplicationStates.ZoningCheck, ApplicationStates.ZoningHold, AuditTrail.SystemActor, AuditTrail.SourceZoning, "Parcel records conflict: MW-0180-13 carries both M1 and a pending C1 overlay");
        db.ZoningChecks.Add(new ZoningCheck { ApplicationId = hold.Id, Handle = "zc-seed-2295", Status = "indeterminate", Reason = "Parcel records conflict", RequestedAt = hold.CreatedAt.AddMinutes(13), ResolvedAt = hold.CreatedAt.AddMinutes(15) });

        Intake(denied, "zc-seed-2251");
        Step(denied, 14, ApplicationStates.ZoningCheck, ApplicationStates.Denied, AuditTrail.SystemActor, AuditTrail.SourceZoning, "New construction is not permitted in the Open Space district");
        db.ZoningChecks.Add(new ZoningCheck { ApplicationId = denied.Id, Handle = "zc-seed-2251", Status = "fail", Reason = "New construction is not permitted in the Open Space district", RequestedAt = denied.CreatedAt.AddMinutes(13), ResolvedAt = denied.CreatedAt.AddMinutes(14) });

        await db.SaveChangesAsync();

        // The reroof permit was paid for and issued.
        issued.State = ApplicationStates.Issued;
        db.Permits.Add(new Permit { ApplicationId = issued.Id, PermitNumber = "BP-2026-04087", IssuedAt = issuedAt, ValidUntil = issuedAt.AddDays(180) });
        await db.SaveChangesAsync();

        log.LogInformation("Seeded {Users} users, {Parcels} parcels and {Applications} applications", 4, parcels.Length, 5);
    }
}
