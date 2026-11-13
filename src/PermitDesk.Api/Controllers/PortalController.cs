using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Auth;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Models;

namespace PermitDesk.Api.Controllers;

[ApiController]
public abstract class PortalController : ControllerBase
{
    protected int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    protected bool IsStaff => User.IsInRole(Roles.Technician);

    protected string Actor => $"{(IsStaff ? "technician" : "applicant")}:{CurrentUserId}";

    /// <summary>
    /// Loads an application the caller is allowed to see: their own, or any
    /// application for counter staff.
    /// </summary>
    protected async Task<PermitApplication?> FindVisibleAsync(PermitDeskDbContext db, int id, CancellationToken ct)
    {
        var application = await db.Applications.Include(a => a.Parcel).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (application == null) return null;
        if (application.ApplicantUserId != CurrentUserId && !IsStaff) return null;
        return application;
    }

    protected static async Task<ApplicationDetail> BuildDetailAsync(PermitDeskDbContext db, PermitApplication application, CancellationToken ct)
    {
        var id = application.Id;
        var transitions = await db.Transitions.AsNoTracking().Where(t => t.ApplicationId == id).ToListAsync(ct);
        var items = await db.CorrectionItems.AsNoTracking().Where(i => i.ApplicationId == id).ToListAsync(ct);
        var responses = await db.CorrectionResponses.AsNoTracking().Where(r => r.ApplicationId == id).ToListAsync(ct);
        var quote = await db.FeeQuotes.AsNoTracking().Where(q => q.ApplicationId == id)
            .OrderByDescending(q => q.CreatedAt).ThenByDescending(q => q.Id).FirstOrDefaultAsync(ct);
        var permit = await db.Permits.AsNoTracking().Where(p => p.ApplicationId == id)
            .OrderByDescending(p => p.IssuedAt).ThenByDescending(p => p.Id).FirstOrDefaultAsync(ct);
        var inspections = await db.Inspections.AsNoTracking().Where(i => i.ApplicationId == id).ToListAsync(ct);

        return ApplicationMapper.ToDetail(application, transitions, items, responses, quote, permit, inspections, DateTime.UtcNow);
    }

    protected ObjectResult Invalid(string message) =>
        UnprocessableEntity(new { error = message });

    protected ObjectResult Invalid(IDictionary<string, string> errors) =>
        UnprocessableEntity(new { error = "Some details need attention.", errors });
}
