using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Integrations;
using PermitDesk.Api.Jobs;
using PermitDesk.Api.Models;

namespace PermitDesk.Api.Controllers;

[Authorize]
[Route("api/applications/{id:int}/inspections")]
public class InspectionsController : PortalController
{
    public static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>
    {
        ["footing"] = "Footing and foundation",
        ["framing"] = "Framing",
        ["electrical"] = "Electrical rough-in",
        ["final"] = "Final inspection"
    };

    private readonly PermitDeskDbContext _db;
    private readonly SchedulingClient _scheduling;
    private readonly ILogger<InspectionsController> _log;

    public InspectionsController(PermitDeskDbContext db, SchedulingClient scheduling, ILogger<InspectionsController> log)
    {
        _db = db;
        _scheduling = scheduling;
        _log = log;
    }

    [HttpGet("slots")]
    public async Task<IActionResult> Slots(int id, [FromQuery] string? type, CancellationToken ct)
    {
        var application = await FindVisibleAsync(_db, id, ct);
        if (application == null) return NotFound();
        if (application.State != ApplicationStates.Issued)
        {
            return Conflict(new { error = "Inspections can be booked once the permit is issued." });
        }

        if (type == null || !Types.ContainsKey(type)) return Invalid("Choose an inspection type.");

        var slots = await _scheduling.SearchSlotsAsync(type, application.Parcel?.District ?? "", ct);
        if (!slots.Ok)
        {
            _log.LogWarning("Slot search failed for application {ApplicationId}: {Error}", id, slots.Error);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "The inspection calendar is not available right now. Try again shortly." });
        }

        return Ok(slots.Value);
    }

    [HttpPost]
    public async Task<IActionResult> Book(int id, [FromBody] BookingRequest request, CancellationToken ct)
    {
        var application = await FindVisibleAsync(_db, id, ct);
        if (application == null || application.ApplicantUserId != CurrentUserId) return NotFound();
        if (application.State != ApplicationStates.Issued)
        {
            return Conflict(new { error = "Inspections can be booked once the permit is issued." });
        }

        if (request.InspectionType == null || !Types.ContainsKey(request.InspectionType))
        {
            return Invalid("Choose an inspection type.");
        }

        if (string.IsNullOrWhiteSpace(request.SlotId)) return Invalid("Choose a time.");

        var booking = await _scheduling.BookAsync(request.SlotId, application.Reference, request.InspectionType, ct);
        if (!booking.Ok)
        {
            _log.LogError("Booking failed for application {ApplicationId}: {Error}", id, booking.Error);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Something went wrong booking your inspection." });
        }

        var inspection = new Inspection
        {
            ApplicationId = id,
            InspectionType = request.InspectionType,
            SlotId = request.SlotId,
            SlotStart = booking.Value!.Start.UtcDateTime,
            ExternalId = booking.Value.BookingId,
            Status = booking.Value.Status
        };
        _db.Inspections.Add(inspection);
        JobQueue.Enqueue(_db, JobKinds.InspectionConfirm, new InspectionJob(inspection.ExternalId), TimeSpan.FromSeconds(5));
        await _db.SaveChangesAsync(ct);

        return Ok(new InspectionModel(inspection.Id, inspection.InspectionType, inspection.SlotStart, inspection.Status));
    }
}
