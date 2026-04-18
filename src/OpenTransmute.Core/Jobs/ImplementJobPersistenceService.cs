using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenTransmute.Data;
using OpenTransmute.Models;

namespace OpenTransmute.Jobs;

public class ImplementJobPersistenceService(IDbContextFactory<AppDbContext> dbFactory, ILogger<ImplementJobPersistenceService> logger)
    : IJobPersistenceService<ImplementJob>
{
    public async Task SaveAsync(ImplementJob job, CancellationToken ct = default)
    {
        try
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            SavedImplementJob? existing = await db.ImplementJobs.FindAsync([job.Id], ct);
            string logJson = JsonSerializer.Serialize(job.GetLogSnapshot());

            if (existing is null)
            {
                db.ImplementJobs.Add(new SavedImplementJob
                {
                    Id               = job.Id,
                    Label            = job.Options.Label ?? job.Options.ProjectName,
                    OutputDirectory  = job.Options.OutputDirectory,
                    Status           = (int)job.Status,
                    CreatedAt        = job.CreatedAt,
                    StartedAt        = job.StartedAt,
                    CompletedAt      = job.CompletedAt,
                    ErrorMessage     = job.ErrorMessage,
                    OrchestratorType = job.Options.Orchestrator.ToString(),
                    Model            = job.Options.Model,
                    LogLinesJson     = logJson,
                });
            }
            else
            {
                existing.Status       = (int)job.Status;
                existing.StartedAt    = job.StartedAt;
                existing.CompletedAt  = job.CompletedAt;
                existing.ErrorMessage = job.ErrorMessage;
                existing.LogLinesJson = logJson;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save implement job {Id}", job.Id);
        }
    }

    public async Task<List<ImplementJob>> LoadAllAsync(CancellationToken ct = default)
    {
        try
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            List<SavedImplementJob> records = await db.ImplementJobs
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync(ct);

            return records.Select(r =>
            {
                var options = new ImplementOptions
                {
                    Label           = r.Label,
                    OutputDirectory = r.OutputDirectory,
                    Model           = r.Model,
                };

                if (Enum.TryParse<OrchestratorType>(r.OrchestratorType, out OrchestratorType orch))
                    options.Orchestrator = orch;

                var job = new ImplementJob(r.Id)
                {
                    Options      = options,
                    Status       = (JobStatus)r.Status,
                    StartedAt    = r.StartedAt,
                    CompletedAt  = r.CompletedAt,
                    ErrorMessage = r.ErrorMessage,
                };

                List<string>? lines = JsonSerializer.Deserialize<List<string>>(r.LogLinesJson);
                if (lines is not null)
                    foreach (string line in lines)
                        job.AppendLog(line);

                return job;
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load implement jobs");
            return [];
        }
    }
}
