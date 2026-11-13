using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Data;

namespace PermitDesk.Api.Ops;

public class ServiceMetrics
{
    private readonly ConcurrentDictionary<string, Counter> _outbound = new();

    public void Record(string service, double elapsedMs, bool failed)
    {
        var counter = _outbound.GetOrAdd(service, _ => new Counter());
        Interlocked.Increment(ref counter.Requests);
        if (failed) Interlocked.Increment(ref counter.Failures);
        lock (counter) counter.DurationMs += elapsedMs;
    }

    public async Task<string> RenderAsync(PermitDeskDbContext db, CancellationToken ct)
    {
        var text = new StringBuilder();

        var byState = await db.Applications.GroupBy(a => a.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        text.AppendLine("# TYPE permitdesk_applications gauge");
        foreach (var row in byState)
        {
            text.AppendLine($"permitdesk_applications{{state=\"{row.State}\"}} {row.Count}");
        }

        var pending = await db.Jobs.Where(j => j.CompletedAt == null).Select(j => j.CreatedAt).ToListAsync(ct);
        var oldest = pending.Count == 0 ? 0 : (DateTime.UtcNow - pending.Min()).TotalSeconds;
        text.AppendLine("# TYPE permitdesk_jobs_pending gauge");
        text.AppendLine($"permitdesk_jobs_pending {pending.Count}");
        text.AppendLine("# TYPE permitdesk_jobs_oldest_pending_seconds gauge");
        text.AppendLine($"permitdesk_jobs_oldest_pending_seconds {oldest.ToString("F0", CultureInfo.InvariantCulture)}");

        text.AppendLine("# TYPE permitdesk_outbound_requests_total counter");
        foreach (var (service, counter) in _outbound)
        {
            text.AppendLine($"permitdesk_outbound_requests_total{{service=\"{service}\"}} {counter.Requests}");
            text.AppendLine($"permitdesk_outbound_failures_total{{service=\"{service}\"}} {counter.Failures}");
            text.AppendLine($"permitdesk_outbound_duration_ms_sum{{service=\"{service}\"}} {counter.DurationMs.ToString("F0", CultureInfo.InvariantCulture)}");
        }

        return text.ToString();
    }

    private class Counter
    {
        public long Requests;
        public long Failures;
        public double DurationMs;
    }
}

public class OutboundMetricsHandler : DelegatingHandler
{
    private readonly string _service;
    private readonly ServiceMetrics _metrics;

    public OutboundMetricsHandler(string service, ServiceMetrics metrics)
    {
        _service = service;
        _metrics = metrics;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            _metrics.Record(_service, Stopwatch.GetElapsedTime(started).TotalMilliseconds, (int)response.StatusCode >= 500);
            return response;
        }
        catch
        {
            _metrics.Record(_service, Stopwatch.GetElapsedTime(started).TotalMilliseconds, failed: true);
            throw;
        }
    }
}
