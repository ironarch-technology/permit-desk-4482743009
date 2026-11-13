using System.Text.Json;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Jobs;

namespace PermitDesk.Api.Integrations;

/// <summary>
/// Requests a fee quote from Cashiering and records it against the application.
/// Used on approval and again whenever a quote has lapsed before payment.
/// </summary>
public class FeeQuoteService
{
    private readonly PermitDeskDbContext _db;
    private readonly CashieringClient _cashiering;

    public FeeQuoteService(PermitDeskDbContext db, CashieringClient cashiering)
    {
        _db = db;
        _cashiering = cashiering;
    }

    /// <remarks>The caller saves.</remarks>
    public async Task<FeeQuote> IssueAsync(PermitApplication application, string reason, CancellationToken ct)
    {
        var district = application.Parcel?.District
            ?? (await _db.Parcels.FindAsync(new object?[] { application.ParcelId }, ct))?.District
            ?? "";

        var quoted = await _cashiering.RequestQuoteAsync(
            application.Reference, application.WorkType!, application.DeclaredValuation ?? 0, district, ct);

        var quote = new FeeQuote
        {
            Application = application,
            ApplicationId = application.Id,
            ExternalId = quoted.QuoteId,
            Amount = quoted.Lines.Sum(line => line.Amount),
            Breakdown = JsonSerializer.Serialize(quoted.Lines, JobQueue.Json),
            ExpiresAt = quoted.ExpiresAt.UtcDateTime
        };

        AuditTrail.Append(_db, application.Id, application.State, ApplicationStates.FeesAssessed,
            AuditTrail.SystemActor, AuditTrail.SourceCashiering, $"{reason}. Fee quote {quoted.QuoteId} issued");

        _db.FeeQuotes.Add(quote);
        return quote;
    }
}
