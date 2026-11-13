using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Integrations;
using PermitDesk.Api.Jobs;

namespace PermitDesk.Api.Controllers;

/// <summary>
/// Callbacks from other city services. Each one is signed with a secret
/// shared with that service; anything unsigned is refused.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("hooks")]
public class WebhooksController : ControllerBase
{
    private readonly PermitDeskDbContext _db;
    private readonly ZoningOutcomes _zoningOutcomes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhooksController> _log;

    public WebhooksController(PermitDeskDbContext db, ZoningOutcomes zoningOutcomes, IConfiguration configuration, ILogger<WebhooksController> log)
    {
        _db = db;
        _zoningOutcomes = zoningOutcomes;
        _configuration = configuration;
        _log = log;
    }

    public record ZoningEvent(string Handle, string Result, string? Reason);

    public record PaymentEvent(string PaymentId, string Status, double Amount, DateTimeOffset? CapturedAt);

    [HttpPost("zoning")]
    public async Task<IActionResult> Zoning(CancellationToken ct)
    {
        var evt = await ReadSignedAsync<ZoningEvent>("Zoning", ct);
        if (evt == null) return Unauthorized();

        var known = await _zoningOutcomes.ApplyAsync(evt.Handle, evt.Result, evt.Reason, ct);
        return known ? Ok() : NotFound();
    }

    [HttpPost("cashiering")]
    public async Task<IActionResult> Cashiering(CancellationToken ct)
    {
        var evt = await ReadSignedAsync<PaymentEvent>("Cashiering", ct);
        if (evt == null) return Unauthorized();

        var payment = await _db.Payments.Include(p => p.Application)
            .FirstOrDefaultAsync(p => p.ExternalId == evt.PaymentId, ct);
        if (payment == null) return NotFound();

        if (evt.Status != "captured")
        {
            payment.Status = evt.Status;
            await _db.SaveChangesAsync(ct);
            return Ok();
        }

        var now = DateTime.UtcNow;
        var application = payment.Application!;
        var from = application.State;

        payment.Status = "captured";
        payment.CapturedAt = evt.CapturedAt?.UtcDateTime ?? now;

        var sequence = await _db.Database
            .SqlQueryRaw<long>("SELECT nextval('permit_number_seq') AS \"Value\"")
            .SingleAsync(ct);
        var validityDays = _configuration.GetValue("Permits:ValidityDays", 180);

        var permit = new Permit
        {
            ApplicationId = application.Id,
            PermitNumber = $"BP-{now:yyyy}-{sequence:D5}",
            IssuedAt = now,
            ValidUntil = now.AddDays(validityDays)
        };
        _db.Permits.Add(permit);

        application.State = ApplicationStates.Issued;
        AuditTrail.Append(_db, application.Id, from, ApplicationStates.Issued, AuditTrail.SystemActor,
            AuditTrail.SourceCashiering, $"Payment {evt.PaymentId} captured. Permit {permit.PermitNumber} issued");
        Notifier.Queue(_db, application.Id, Notifier.Issued);

        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Permit {PermitNumber} issued for application {ApplicationId} on payment {PaymentId}",
            permit.PermitNumber, application.Id, evt.PaymentId);
        return Ok();
    }

    private async Task<T?> ReadSignedAsync<T>(string service, CancellationToken ct) where T : class
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();

        var secret = _configuration[$"Services:{service}:WebhookSecret"]
            ?? throw new InvalidOperationException($"Services:{service}:WebhookSecret is not configured");

        if (!WebhookSignature.IsValid(Request.Headers[WebhookSignature.Header].FirstOrDefault(), body, secret))
        {
            _log.LogWarning("Rejected {Service} webhook with a missing or invalid signature", service);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JobQueue.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
