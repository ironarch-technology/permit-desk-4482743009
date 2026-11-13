using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Jobs;
using PermitDesk.Api.Models;

namespace PermitDesk.Api.Controllers;

[Authorize]
[Route("api/submissions")]
public class SubmissionsController : PortalController
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex LicencePattern = new(@"^MPL-\d{6}$", RegexOptions.Compiled);

    private readonly PermitDeskDbContext _db;
    private readonly ILogger<SubmissionsController> _log;

    public SubmissionsController(PermitDeskDbContext db, ILogger<SubmissionsController> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Submits an application, either from a saved draft or straight from the
    /// form. Safe to replay with the same submission key.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("intake")]
    public async Task<IActionResult> Submit([FromBody] SubmissionRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        var errors = new Dictionary<string, string>();

        var submissionKey = request.SubmissionKey;
        if (string.IsNullOrWhiteSpace(submissionKey))
        {
            submissionKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        }

        submissionKey = string.IsNullOrWhiteSpace(submissionKey) ? null : submissionKey.Trim();

        if (submissionKey != null)
        {
            if (submissionKey.Length > 100)
            {
                return Invalid("The submission key is too long.");
            }

            var previous = await _db.Applications.Include(a => a.Parcel)
                .FirstOrDefaultAsync(a => a.ApplicantUserId == userId && a.SubmissionKey == submissionKey, ct);
            if (previous != null)
            {
                _log.LogInformation("Submission replayed for application {ApplicationId}", previous.Id);
                return Ok(await BuildDetailAsync(_db, previous, ct));
            }
        }

        PermitApplication? application = null;
        if (request.DraftId != null)
        {
            application = await _db.Applications.FirstOrDefaultAsync(
                a => a.Id == request.DraftId && a.ApplicantUserId == userId, ct);
            if (application == null)
            {
                return NotFound();
            }

            if (application.State != ApplicationStates.Draft)
            {
                return Conflict(new { error = "This application has already been submitted." });
            }
        }

        // Parcel
        Parcel? parcel = null;
        var parcelAddress = request.ParcelAddress?.Trim();
        if (string.IsNullOrWhiteSpace(parcelAddress))
        {
            errors["parcelAddress"] = "Enter the address of the property.";
        }
        else
        {
            var lowered = parcelAddress.ToLower();
            parcel = await _db.Parcels.FirstOrDefaultAsync(p => p.Address.ToLower() == lowered, ct);
            if (parcel == null)
            {
                errors["parcelAddress"] = "We could not find that address in Mountport parcel records.";
            }
        }

        // Work type and valuation
        WorkType? workType = null;
        if (string.IsNullOrWhiteSpace(request.WorkType))
        {
            errors["workType"] = "Choose the type of work.";
        }
        else
        {
            workType = await _db.WorkTypes.FirstOrDefaultAsync(w => w.Code == request.WorkType, ct);
            if (workType == null)
            {
                errors["workType"] = "Choose the type of work.";
            }
        }

        if (request.DeclaredValuation == null)
        {
            errors["declaredValuation"] = "Enter the estimated value of the work.";
        }
        else if (request.DeclaredValuation <= 0)
        {
            errors["declaredValuation"] = "The estimated value must be more than zero.";
        }
        else if (workType != null)
        {
            if (request.DeclaredValuation < workType.MinValuation)
            {
                errors["declaredValuation"] = $"{workType.Label} permits start at ${workType.MinValuation:N0}.";
            }
            else if (request.DeclaredValuation > workType.MaxValuation)
            {
                errors["declaredValuation"] = $"{workType.Label} permits are limited to ${workType.MaxValuation:N0}. Larger projects need a different work type.";
            }
        }

        // Scope of work
        var scope = request.ScopeOfWork?.Trim();
        if (string.IsNullOrWhiteSpace(scope))
        {
            errors["scopeOfWork"] = "Describe the work you plan to do.";
        }
        else if (scope.Length < 20)
        {
            errors["scopeOfWork"] = "Give a little more detail about the work, at least a sentence.";
        }
        else if (scope.Length > 5000)
        {
            errors["scopeOfWork"] = "The description is limited to 5,000 characters.";
        }

        // Contact details
        var contactName = request.ContactName?.Trim();
        var contactEmail = request.ContactEmail?.Trim();
        var contactPhone = request.ContactPhone?.Trim();

        if (string.IsNullOrWhiteSpace(contactName))
        {
            errors["contactName"] = "Enter your full name.";
        }

        if (string.IsNullOrWhiteSpace(contactEmail))
        {
            errors["contactEmail"] = "Enter an email address.";
        }
        else if (!EmailPattern.IsMatch(contactEmail))
        {
            errors["contactEmail"] = "Enter an email address in the format name@example.com.";
        }

        if (string.IsNullOrWhiteSpace(contactPhone))
        {
            errors["contactPhone"] = "Enter a phone number.";
        }
        else if (contactPhone.Count(char.IsDigit) < 7)
        {
            errors["contactPhone"] = "Enter a phone number with at least 7 digits.";
        }

        // Contractor details
        var applicantType = request.ApplicantType == "contractor" ? "contractor" : "resident";
        var licenceNumber = request.ContractorLicenceNumber?.Trim().ToUpperInvariant();
        if (applicantType == "contractor")
        {
            if (string.IsNullOrWhiteSpace(licenceNumber))
            {
                errors["contractorLicenceNumber"] = "Enter your Mountport contractor licence number.";
            }
            else if (!LicencePattern.IsMatch(licenceNumber))
            {
                errors["contractorLicenceNumber"] = "Licence numbers look like MPL-123456.";
            }

            if (request.ContractorLicenceExpiry == null)
            {
                errors["contractorLicenceExpiry"] = "Enter the date your licence expires.";
            }
            else if (request.ContractorLicenceExpiry <= DateOnly.FromDateTime(DateTime.UtcNow))
            {
                errors["contractorLicenceExpiry"] = "Your licence has expired. Renew it before applying.";
            }
        }

        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        var now = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        if (application == null)
        {
            application = new PermitApplication
            {
                Reference = await ApplicationsController.NextReferenceAsync(_db, ct),
                ApplicantUserId = userId,
                CreatedAt = now
            };
            _db.Applications.Add(application);
        }

        application.ParcelId = parcel!.Id;
        application.ParcelAddress = parcel.Address;
        application.WorkType = workType!.Code;
        application.ScopeOfWork = scope;
        application.DeclaredValuation = request.DeclaredValuation;
        application.ApplicantType = applicantType;
        application.ContactName = contactName;
        application.ContactEmail = contactEmail;
        application.ContactPhone = contactPhone;
        application.ContractorLicenceNumber = applicantType == "contractor" ? licenceNumber : null;
        application.ContractorLicenceExpiry = applicantType == "contractor" ? request.ContractorLicenceExpiry : null;
        application.SubmissionKey = submissionKey;
        application.SubmittedAt = now;
        application.State = ApplicationStates.Submitted;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (submissionKey != null)
        {
            // Two copies of the same submission arrived together; the other one won.
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            var winner = await _db.Applications.Include(a => a.Parcel)
                .FirstOrDefaultAsync(a => a.ApplicantUserId == userId && a.SubmissionKey == submissionKey, ct);
            if (winner == null) throw;
            return Ok(await BuildDetailAsync(_db, winner, ct));
        }

        _db.Transitions.Add(new Transition
        {
            ApplicationId = application.Id,
            FromState = ApplicationStates.Draft,
            ToState = ApplicationStates.Submitted,
            Actor = Actor,
            Source = AuditTrail.SourcePortal,
            Reason = request.DraftId == null ? "Submitted from the portal" : "Draft submitted from the portal",
            OccurredAt = now
        });

        _db.Jobs.Add(new Job
        {
            Kind = JobKinds.ZoningDispatch,
            Payload = JsonSerializer.Serialize(new ApplicationJob(application.Id), JobQueue.Json),
            RunAt = now
        });

        _db.Jobs.Add(new Job
        {
            Kind = JobKinds.Notify,
            Payload = JsonSerializer.Serialize(new NotifyJob(application.Id, Notifier.Submitted), JobQueue.Json),
            RunAt = now
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _log.LogInformation("Application {ApplicationId} ({Reference}) submitted for {WorkType} in {District}",
            application.Id, application.Reference, workType.Code, parcel.District);

        application.Parcel = parcel;
        return CreatedAtAction(nameof(ApplicationsController.Get), "Applications", new { id = application.Id },
            await BuildDetailAsync(_db, application, ct));
    }
}
