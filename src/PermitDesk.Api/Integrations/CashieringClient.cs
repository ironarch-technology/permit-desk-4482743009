using System.Net.Http.Json;

namespace PermitDesk.Api.Integrations;

public record FeeLine(string Code, string Label, double Amount);

public record CashieringQuote(string QuoteId, List<FeeLine> Lines, double Total, DateTimeOffset ExpiresAt);

public record CashieringPayment(string PaymentId, string CheckoutUrl);

public class CashieringException : Exception
{
    public CashieringException(string message, int? statusCode = null, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}

/// <summary>
/// Cashiering Service. Owns the fee schedule and takes the payment. Card and
/// bank details are entered on Cashiering's own checkout page and never pass
/// through Permit Desk.
/// </summary>
public class CashieringClient
{
    private readonly HttpClient _http;

    public CashieringClient(HttpClient http)
    {
        _http = http;
    }

    public Task<CashieringQuote> RequestQuoteAsync(string applicationRef, string workType, double valuation, string district, CancellationToken ct) =>
        PostAsync<CashieringQuote>("quotes", new { applicationRef, workType, valuation, district }, ct);

    public Task<CashieringPayment> CreatePaymentAsync(string quoteId, double amount, string applicationRef, CancellationToken ct) =>
        PostAsync<CashieringPayment>("payments", new { quoteId, amount, applicationRef }, ct);

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(path, body, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new CashieringException("Cashiering is unreachable", inner: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new CashieringException("Cashiering timed out", inner: ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CashieringException($"Cashiering responded {(int)response.StatusCode} to {path}", (int)response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
            ?? throw new CashieringException($"Cashiering returned an empty response to {path}");
    }
}
