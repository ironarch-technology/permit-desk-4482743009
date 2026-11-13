using System.Net.Http.Json;

namespace PermitDesk.Api.Integrations;

public record InspectionSlot(string SlotId, DateTimeOffset Start, string InspectionType, string District, int Remaining);

public record InspectionBooking(string BookingId, string Status, string SlotId, DateTimeOffset Start);

public class SchedulingResult<T>
{
    public bool Ok { get; private init; }
    public T? Value { get; private init; }
    public string? Error { get; private init; }

    public static SchedulingResult<T> Success(T value) => new() { Ok = true, Value = value };

    public static SchedulingResult<T> Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// Inspection Scheduler. Slot search is immediate; a booking is accepted first
/// and confirmed a little later.
/// </summary>
public class SchedulingClient
{
    private readonly HttpClient _http;

    public SchedulingClient(HttpClient http)
    {
        _http = http;
    }

    public Task<SchedulingResult<List<InspectionSlot>>> SearchSlotsAsync(string inspectionType, string district, CancellationToken ct) =>
        SendAsync<List<InspectionSlot>>(
            new HttpRequestMessage(HttpMethod.Get,
                $"slots?type={Uri.EscapeDataString(inspectionType)}&district={Uri.EscapeDataString(district)}"), ct);

    public Task<SchedulingResult<InspectionBooking>> BookAsync(string slotId, string applicationRef, string inspectionType, CancellationToken ct) =>
        SendAsync<InspectionBooking>(
            new HttpRequestMessage(HttpMethod.Post, "bookings")
            {
                Content = JsonContent.Create(new { slotId, applicationRef, inspectionType })
            }, ct);

    public Task<SchedulingResult<InspectionBooking>> GetBookingAsync(string bookingId, CancellationToken ct) =>
        SendAsync<InspectionBooking>(new HttpRequestMessage(HttpMethod.Get, $"bookings/{bookingId}"), ct);

    private async Task<SchedulingResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return SchedulingResult<T>.Fail($"Scheduler responded {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            return body == null
                ? SchedulingResult<T>.Fail("Scheduler returned an empty response")
                : SchedulingResult<T>.Success(body);
        }
        catch (HttpRequestException ex)
        {
            return SchedulingResult<T>.Fail($"Scheduler unreachable: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return SchedulingResult<T>.Fail("Scheduler timed out");
        }
    }
}
