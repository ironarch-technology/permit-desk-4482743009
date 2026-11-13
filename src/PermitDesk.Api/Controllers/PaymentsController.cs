using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Integrations;
using PermitDesk.Api.Models;

namespace PermitDesk.Api.Controllers;

[Authorize]
[Route("api/applications/{id:int}/payment")]
public class PaymentsController : PortalController
{
    private readonly PermitDeskDbContext _db;
    private readonly CashieringClient _cashiering;
    private readonly FeeQuoteService _quotes;
    private readonly ILogger<PaymentsController> _log;

    public PaymentsController(PermitDeskDbContext db, CashieringClient cashiering, FeeQuoteService quotes, ILogger<PaymentsController> log)
    {
        _db = db;
        _cashiering = cashiering;
        _quotes = quotes;
        _log = log;
    }

    /// <summary>
    /// Starts payment of the current fee quote and returns the Cashiering
    /// checkout page to send the applicant to.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Start(int id, CancellationToken ct)
    {
        var application = await FindVisibleAsync(_db, id, ct);
        if (application == null || application.ApplicantUserId != CurrentUserId) return NotFound();

        if (application.State != ApplicationStates.FeesAssessed)
        {
            return Conflict(new { error = "There are no fees due on this application." });
        }

        var quote = await _db.FeeQuotes.Where(q => q.ApplicationId == id)
            .OrderByDescending(q => q.CreatedAt).ThenByDescending(q => q.Id)
            .FirstOrDefaultAsync(ct);
        if (quote == null)
        {
            return Conflict(new { error = "The fee quote for this application is still being prepared." });
        }

        try
        {
            if (quote.IsExpired(DateTime.UtcNow))
            {
                var fresh = await _quotes.IssueAsync(application, "Previous quote lapsed before payment", ct);
                await _db.SaveChangesAsync(ct);

                return Conflict(new
                {
                    error = "Your fee quote had expired, so we have prepared a new one. Check the amount and pay again.",
                    code = "quote_expired",
                    feeQuote = ApplicationMapper.ToFeeQuote(fresh, DateTime.UtcNow)
                });
            }

            var payment = await _cashiering.CreatePaymentAsync(quote.ExternalId, quote.Amount, application.Reference, ct);

            _db.Payments.Add(new Payment
            {
                ApplicationId = id,
                FeeQuoteId = quote.Id,
                ExternalId = payment.PaymentId,
                Amount = quote.Amount
            });
            await _db.SaveChangesAsync(ct);

            return Ok(new { checkoutUrl = payment.CheckoutUrl });
        }
        catch (CashieringException ex)
        {
            _log.LogError(ex, "Could not start payment for application {ApplicationId}", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "City Cashiering is not available right now. Your application is safe; try again shortly." });
        }
    }
}
