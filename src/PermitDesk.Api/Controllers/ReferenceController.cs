using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Data;

namespace PermitDesk.Api.Controllers;

/// <summary>
/// Lookup data the portal needs to draw its forms. The limits come from here
/// so the form and the server cannot disagree about them.
/// </summary>
[Authorize]
[Route("api/reference")]
public class ReferenceController : PortalController
{
    private readonly PermitDeskDbContext _db;

    public ReferenceController(PermitDeskDbContext db)
    {
        _db = db;
    }

    [HttpGet("work-types")]
    public async Task<IActionResult> WorkTypes(CancellationToken ct)
    {
        var types = await _db.WorkTypes.AsNoTracking().OrderBy(w => w.SortOrder).ToListAsync(ct);
        return Ok(types.Select(w => new
        {
            code = w.Code,
            label = w.Label,
            minValuation = w.MinValuation,
            maxValuation = w.MaxValuation
        }));
    }

    [HttpGet("inspection-types")]
    public IActionResult InspectionTypes() =>
        Ok(InspectionsController.Types.Select(t => new { code = t.Key, label = t.Value }));
}
