using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Jobs;
using PermitDesk.Api.Models;

namespace PermitDesk.Api.Controllers;

[Authorize]
[Route("api/applications/{id:int}/corrections")]
public class CorrectionsController : PortalController
{
    private readonly PermitDeskDbContext _db;

    public CorrectionsController(PermitDeskDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Records the applicant's answer to each outstanding correction item and
    /// sends the application back to plan review as a new cycle.
    /// </summary>
    [HttpPost("responses")]
    public async Task<IActionResult> Respond(int id, [FromBody] CorrectionResponsesRequest request, CancellationToken ct)
    {
        var application = await FindVisibleAsync(_db, id, ct);
        if (application == null || application.ApplicantUserId != CurrentUserId) return NotFound();

        if (application.State != ApplicationStates.CorrectionsRequired ||
            !LifecycleRules.CanTransition(application.State, ApplicationStates.PlanReview))
        {
            return Conflict(new { error = "This application is not waiting on corrections." });
        }

        var outstanding = await _db.CorrectionItems
            .Where(i => i.ApplicationId == id && i.Cycle == application.CorrectionCycle)
            .ToListAsync(ct);

        var supplied = (request.Responses ?? new List<CorrectionResponseInput>())
            .GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => g.Last().Response?.Trim());

        var errors = new Dictionary<string, string>();
        foreach (var item in outstanding)
        {
            if (!supplied.TryGetValue(item.Id, out var text) || string.IsNullOrWhiteSpace(text))
            {
                errors[$"item-{item.Id}"] = $"Respond to correction {item.Code}.";
            }
            else if (text.Length > 4000)
            {
                errors[$"item-{item.Id}"] = "Responses are limited to 4,000 characters.";
            }
        }

        if (errors.Count > 0) return Invalid(errors);

        var now = DateTime.UtcNow;
        foreach (var item in outstanding)
        {
            var response = await _db.CorrectionResponses
                .FirstOrDefaultAsync(r => r.ApplicationId == id && r.ItemCode == item.Code, ct);

            if (response == null)
            {
                response = new CorrectionResponse { ApplicationId = id, ItemCode = item.Code };
                _db.CorrectionResponses.Add(response);
            }

            response.Body = supplied[item.Id]!;
            response.RespondedAt = now;
        }

        application.CorrectionCycle += 1;
        application.State = ApplicationStates.PlanReview;
        AuditTrail.Append(_db, id, ApplicationStates.CorrectionsRequired, ApplicationStates.PlanReview, Actor,
            AuditTrail.SourcePortal, $"Applicant responded to {outstanding.Count} correction item(s)");
        JobQueue.Enqueue(_db, JobKinds.ReviewSubmit, new ApplicationJob(id));

        await _db.SaveChangesAsync(ct);
        return Ok(await BuildDetailAsync(_db, application, ct));
    }
}
