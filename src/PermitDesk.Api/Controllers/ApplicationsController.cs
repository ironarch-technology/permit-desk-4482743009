using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Jobs;
using PermitDesk.Api.Models;

namespace PermitDesk.Api.Controllers;

[Authorize]
[Route("api/applications")]
public class ApplicationsController : PortalController
{
    private readonly PermitDeskDbContext _db;
    private readonly ILogger<ApplicationsController> _log;

    public ApplicationsController(PermitDeskDbContext db, ILogger<ApplicationsController> log)
    {
        _db = db;
        _log = log;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var applications = await _db.Applications.AsNoTracking()
            .Where(a => a.ApplicantUserId == CurrentUserId)
            .OrderByDescending(a => a.UpdatedAt)
            .ToListAsync(ct);

        return Ok(applications.Select(ApplicationMapper.ToSummary));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var application = await FindVisibleAsync(_db, id, ct);
        if (application == null) return NotFound();

        return Ok(await BuildDetailAsync(_db, application, ct));
    }

    [HttpPost]
    public async Task<IActionResult> CreateDraft([FromBody] DraftRequest request, CancellationToken ct)
    {
        var application = new PermitApplication
        {
            Reference = await NextReferenceAsync(_db, ct),
            ApplicantUserId = CurrentUserId
        };

        await ApplyDraftAsync(application, request, ct);
        _db.Applications.Add(application);
        await _db.SaveChangesAsync(ct);

        AuditTrail.Append(_db, application.Id, null, ApplicationStates.Draft, Actor, AuditTrail.SourcePortal, "Draft started");
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = application.Id }, await BuildDetailAsync(_db, application, ct));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateDraft(int id, [FromBody] DraftRequest request, CancellationToken ct)
    {
        var application = await FindVisibleAsync(_db, id, ct);
        if (application == null || application.ApplicantUserId != CurrentUserId) return NotFound();
        if (application.State != ApplicationStates.Draft)
        {
            return Conflict(new { error = "Only a draft can be edited." });
        }

        await ApplyDraftAsync(application, request, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(await BuildDetailAsync(_db, application, ct));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteDraft(int id, CancellationToken ct)
    {
        var application = await FindVisibleAsync(_db, id, ct);
        if (application == null || application.ApplicantUserId != CurrentUserId) return NotFound();
        if (application.State != ApplicationStates.Draft)
        {
            return Conflict(new { error = "Only a draft can be deleted. Submitted applications can be withdrawn." });
        }

        _db.Applications.Remove(application);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/withdraw")]
    public async Task<IActionResult> Withdraw(int id, [FromBody] WithdrawRequest request, CancellationToken ct)
    {
        var application = await FindVisibleAsync(_db, id, ct);
        if (application == null || application.ApplicantUserId != CurrentUserId) return NotFound();

        if (!LifecycleRules.CanTransition(application.State, ApplicationStates.Withdrawn))
        {
            return Conflict(new { error = $"An application that is {ApplicationMapper.StateLabel(application.State).ToLowerInvariant()} cannot be withdrawn." });
        }

        var from = application.State;
        application.State = ApplicationStates.Withdrawn;
        AuditTrail.Append(_db, application.Id, from, ApplicationStates.Withdrawn, Actor, AuditTrail.SourcePortal,
            string.IsNullOrWhiteSpace(request.Reason) ? "Withdrawn by applicant" : request.Reason.Trim());
        Notifier.Queue(_db, application.Id, Notifier.Withdrawn);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Application {ApplicationId} withdrawn from {FromState}", application.Id, from);
        return Ok(await BuildDetailAsync(_db, application, ct));
    }

    internal static async Task<string> NextReferenceAsync(PermitDeskDbContext db, CancellationToken ct)
    {
        var next = await db.Database.SqlQueryRaw<long>("SELECT nextval('application_reference_seq') AS \"Value\"").SingleAsync(ct);
        return $"MP-{DateTime.UtcNow:yyyy}-{next:D6}";
    }

    private async Task ApplyDraftAsync(PermitApplication application, DraftRequest request, CancellationToken ct)
    {
        application.ParcelAddress = request.ParcelAddress?.Trim();
        application.WorkType = request.WorkType;
        application.ScopeOfWork = request.ScopeOfWork;
        application.DeclaredValuation = request.DeclaredValuation;
        application.ApplicantType = request.ApplicantType == "contractor" ? "contractor" : "resident";
        application.ContactName = request.ContactName?.Trim();
        application.ContactEmail = request.ContactEmail?.Trim();
        application.ContactPhone = request.ContactPhone?.Trim();
        application.ContractorLicenceNumber = request.ContractorLicenceNumber?.Trim();
        application.ContractorLicenceExpiry = request.ContractorLicenceExpiry;

        if (!string.IsNullOrWhiteSpace(application.ParcelAddress))
        {
            var address = application.ParcelAddress.ToLower();
            var parcel = await _db.Parcels.FirstOrDefaultAsync(p => p.Address.ToLower() == address, ct);
            application.ParcelId = parcel?.Id;
        }
        else
        {
            application.ParcelId = null;
        }
    }
}
