using System.Net.Http.Json;

namespace PermitDesk.Api.Integrations;

public record ReviewResponse(string Code, string Response);

public record ReviewRequest(
    string ApplicationRef,
    int Cycle,
    string WorkType,
    double Valuation,
    string ScopeOfWork,
    IReadOnlyList<ReviewResponse> Responses);

public record ReviewCorrection(string Code, string Narrative, string Citation);

public record ReviewDecision(string Outcome, string? Reason, List<ReviewCorrection>? Items);

/// <summary>
/// Plan Review Service. Reviews take anything from hours to days, so callers
/// get null until there is something to act on.
/// </summary>
public class ReviewClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ReviewClient> _log;

    public ReviewClient(HttpClient http, ILogger<ReviewClient> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<string?> SubmitAsync(ReviewRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("reviews", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Plan review rejected {ApplicationRef} with status {Status}",
                    request.ApplicationRef, (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<SubmitResponse>(cancellationToken: ct);
            return body?.ReviewId;
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Plan review unreachable while submitting {ApplicationRef}", request.ApplicationRef);
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("Plan review timed out while submitting {ApplicationRef}", request.ApplicationRef);
            return null;
        }
    }

    public async Task<ReviewDecision?> GetDecisionAsync(string reviewId, CancellationToken ct)
    {
        try
        {
            var response = await _http.GetAsync($"reviews/{reviewId}", ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadFromJsonAsync<StatusResponse>(cancellationToken: ct);
            if (body == null || body.Status != "decided" || body.Outcome == null) return null;

            return new ReviewDecision(body.Outcome, body.Reason, body.Items);
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Plan review unreachable while reading {ReviewId}", reviewId);
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private record SubmitResponse(string ReviewId);

    private record StatusResponse(string Status, string? Outcome, string? Reason, List<ReviewCorrection>? Items);
}
