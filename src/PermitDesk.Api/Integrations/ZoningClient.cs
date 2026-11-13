using System.Net.Http.Json;

namespace PermitDesk.Api.Integrations;

public record ZoningCheckStatus(string Handle, string Status, string? Result, string? Reason);

/// <summary>
/// Parcel and Zoning Service. Checks are asynchronous: submit returns a handle
/// and the result arrives later by webhook or by reading the handle back.
/// </summary>
public class ZoningClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ZoningClient> _log;

    public ZoningClient(HttpClient http, ILogger<ZoningClient> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<string> SubmitCheckAsync(string parcelNumber, string workType, string scopeOfWork, CancellationToken ct)
    {
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "checks")
        {
            Content = JsonContent.Create(new { parcelNumber, workType, scopeOfWork })
        }, ct);

        var body = await response.Content.ReadFromJsonAsync<SubmitResponse>(cancellationToken: ct);
        return body!.Handle;
    }

    public async Task<ZoningCheckStatus> GetCheckAsync(string handle, CancellationToken ct)
    {
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"checks/{handle}"), ct);
        var body = await response.Content.ReadFromJsonAsync<ZoningCheckStatus>(cancellationToken: ct);
        return body!;
    }

    // Zoning drops out for a few minutes most nights while it rebuilds its
    // parcel index, so calls keep trying until it is back.
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> request, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                var response = await _http.SendAsync(request(), ct);
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Zoning request failed on attempt {Attempt}, retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }

    private record SubmitResponse(string Handle);
}
