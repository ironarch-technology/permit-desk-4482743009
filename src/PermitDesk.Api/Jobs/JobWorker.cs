using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;

namespace PermitDesk.Api.Jobs;

/// <summary>
/// Runs queued jobs. Several loops share the table; each claims one row at a
/// time with SKIP LOCKED and a lease so a crashed worker's job is picked up again.
/// </summary>
public class JobWorker : BackgroundService
{
    private const string ClaimSql = @"
        UPDATE jobs
           SET locked_until = now() + interval '10 minutes',
               attempts = attempts + 1
         WHERE id = (
                SELECT id
                  FROM jobs
                 WHERE completed_at IS NULL
                   AND run_at <= now()
                   AND (locked_until IS NULL OR locked_until < now())
                 ORDER BY run_at
                 LIMIT 1
                   FOR UPDATE SKIP LOCKED)
        RETURNING *";

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<JobWorker> _log;
    private readonly int _workers;

    public JobWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<JobWorker> log)
    {
        _scopes = scopes;
        _log = log;
        _workers = configuration.GetValue("Jobs:Workers", 4);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = Enumerable.Range(1, _workers).Select(n => RunLoopAsync(n, stoppingToken));
        return Task.WhenAll(loops);
    }

    private async Task RunLoopAsync(int worker, CancellationToken ct)
    {
        await Task.Yield();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ran = await RunNextAsync(worker, ct);
                if (!ran)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Worker {Worker} could not poll the job table", worker);
                await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);
            }
        }
    }

    private async Task<bool> RunNextAsync(int worker, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PermitDeskDbContext>();

        var claimed = await db.Jobs.FromSqlRaw(ClaimSql).AsNoTracking().ToListAsync(ct);
        var job = claimed.FirstOrDefault();
        if (job == null) return false;

        try
        {
            var handler = scope.ServiceProvider.GetServices<IJobHandler>().FirstOrDefault(h => h.Kind == job.Kind)
                ?? throw new InvalidOperationException($"No handler registered for job kind '{job.Kind}'");

            await handler.HandleAsync(job.Payload, ct);

            await db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.CompletedAt, DateTime.UtcNow)
                .SetProperty(j => j.LockedUntil, (DateTime?)null)
                .SetProperty(j => j.LastError, (string?)null), CancellationToken.None);

            _log.LogInformation("Job {JobId} ({Kind}) completed on attempt {Attempt}", job.Id, job.Kind, job.Attempts);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down: hand the job back so another instance takes it straight away.
            await db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.LockedUntil, (DateTime?)null)
                .SetProperty(j => j.Attempts, j => j.Attempts - 1), CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            var retryAt = DateTime.UtcNow.Add(Backoff(job.Attempts));

            await db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.RunAt, retryAt)
                .SetProperty(j => j.LockedUntil, (DateTime?)null)
                .SetProperty(j => j.LastError, ex.Message), CancellationToken.None);

            _log.LogWarning(ex, "Job {JobId} ({Kind}) failed on attempt {Attempt}; next run at {RetryAt}",
                job.Id, job.Kind, job.Attempts, retryAt);
        }

        return true;
    }

    internal static TimeSpan Backoff(int attempts)
    {
        var seconds = Math.Min(Math.Pow(2, Math.Min(attempts, 10)), 300);
        return TimeSpan.FromSeconds(seconds);
    }
}
