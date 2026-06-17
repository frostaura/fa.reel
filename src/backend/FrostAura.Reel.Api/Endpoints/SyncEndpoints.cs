using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Api.Endpoints;

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sync").RequireAccount();

        group.MapGet("/status", async (IReelDbContext db, IAccountContext accountContext, HttpContext http, CancellationToken ct) =>
        {
            var account = await http.GetCurrentAccountAsync(db, ct);
            if (account is null)
            {
                return Results.Unauthorized();
            }

            var connection = await db.TraktConnections.FirstOrDefaultAsync(c => c.AccountId == account.Id, ct);
            var jobs = await db.SyncJobs
                .Where(j => j.AccountId == account.Id)
                .OrderByDescending(j => j.EnqueuedAt)
                .Take(6)
                .ToListAsync(ct);

            var outboxPending = await db.TraktOutbox.CountAsync(o => o.Status == OutboxStatus.Pending || o.Status == OutboxStatus.Failed, ct);
            var outboxDead = await db.TraktOutbox.CountAsync(o => o.Status == OutboxStatus.DeadLetter, ct);

            static object ToJobSummary(SyncJob job) => new
            {
                kind = job.Kind.ToString(),
                status = job.Status.ToString(),
                progressPct = job.ProgressPct,
                progressMessage = job.ProgressMessage,
                enqueuedAt = job.EnqueuedAt,
                completedAt = job.CompletedAt,
            };

            // The pill should surface the job the user cares about. A primary pipeline job
            // (onboarding/enrichment/training) outranks a background Trakt poll (delta/reconcile)
            // that fires every few minutes — otherwise a transient DeltaSync, enqueued after a
            // long EnrichCatalog, would hijack the pill and read as a generic "Syncing".
            static bool IsBackground(JobKind k) =>
                k is JobKind.DeltaSync or JobKind.FullReconcile or JobKind.RefreshAvailability;

            var active = jobs.Where(j => j.Status == JobStatus.Running)
                    .OrderBy(j => IsBackground(j.Kind) ? 1 : 0)
                    .ThenByDescending(j => j.EnqueuedAt)
                    .FirstOrDefault()
                ?? jobs.Where(j => j.Status == JobStatus.Pending)
                    .OrderBy(j => IsBackground(j.Kind) ? 1 : 0)
                    .ThenByDescending(j => j.EnqueuedAt)
                    .FirstOrDefault();

            return Results.Ok(new
            {
                pipelineStage = account.PipelineStage.ToString(),
                connectionStatus = connection?.Status.ToString() ?? "Revoked",
                lastDeltaSyncAt = connection?.LastDeltaSyncAt,
                lastFullReconcileAt = connection?.LastFullReconcileAt,
                activeJob = active is null ? null : ToJobSummary(active),
                recentJobs = jobs.Select(ToJobSummary),
                outboxPending,
                outboxDeadLetters = outboxDead,
            });
        });

        group.MapPost("/now", async (IReelDbContext db, IAccountContext accountContext, CancellationToken ct) =>
        {
            var accountId = accountContext.AccountId!.Value;
            var inFlight = await db.SyncJobs.AnyAsync(
                j => j.AccountId == accountId
                    && (j.Kind == JobKind.DeltaSync || j.Kind == JobKind.FullIngest)
                    && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running), ct);

            if (!inFlight)
            {
                db.SyncJobs.Add(new SyncJob
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    Kind = JobKind.DeltaSync,
                    Priority = 0, // user asked — interactive priority
                    EnqueuedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(ct);
            }

            return Results.Accepted();
        });
    }
}
