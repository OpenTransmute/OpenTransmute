using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenTransmute.Data;
using OpenTransmute.Models;

namespace OpenTransmute.Jobs;

/// <summary>
/// Saves and loads ComposeJob state from the database so history survives restarts.
/// </summary>
public class ComposeJobPersistenceService(IDbContextFactory<AppDbContext> dbFactory, ILogger<ComposeJobPersistenceService> logger)
{
    public async Task SaveAsync(ComposeJob job, CancellationToken ct = default)
    {
        try
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);

            SavedComposeJob? existing = await db.ComposeJobs.FindAsync([job.Id], ct);

            string logJson = JsonSerializer.Serialize(job.LogLines);

            if (existing is null)
            {
                db.ComposeJobs.Add(new SavedComposeJob
                {
                    Id               = job.Id,
                    OutputName       = job.Options.OutputName,
                    SourceLabel      = job.Options.SourceLabel,
                    Status           = (int)job.Status,
                    CreatedAt        = job.CreatedAt,
                    StartedAt        = job.StartedAt,
                    CompletedAt      = job.CompletedAt,
                    ErrorMessage     = job.ErrorMessage,
                    Output           = job.Output,
                    OrchestratorType = job.Options.Orchestrator.ToString(),
                    Model            = job.Options.Model,
                    LogLinesJson     = logJson,
                });
            }
            else
            {
                existing.Status          = (int)job.Status;
                existing.StartedAt       = job.StartedAt;
                existing.CompletedAt     = job.CompletedAt;
                existing.ErrorMessage    = job.ErrorMessage;
                existing.Output          = job.Output;
                existing.LogLinesJson    = logJson;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save compose job {Id}", job.Id);
        }
    }

    public async Task UpdateOutputAsync(Guid id, string output, CancellationToken ct = default)
    {
        try
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            SavedComposeJob? record = await db.ComposeJobs.FindAsync([id], ct);
            if (record is null) return;
            record.Output = output;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update output for compose job {Id}", id);
        }
    }

    public async Task<List<ComposeJob>> LoadAllAsync(CancellationToken ct = default)
    {
        try
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            List<SavedComposeJob> records = await db.ComposeJobs
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync(ct);

            return records.Select(r =>
            {
                var options = new Models.ComposeOptions
                {
                    OutputName  = r.OutputName,
                    SourceLabel = r.SourceLabel,
                    Model       = r.Model,
                };

                if (Enum.TryParse<OpenTransmute.Orchestrator.Contracts.OrchestratorType>(r.OrchestratorType, out var orch))
                    options.Orchestrator = orch;

                var job = new ComposeJob(r.Id)
                {
                    Options      = options,
                    Status       = (JobStatus)r.Status,
                    StartedAt    = r.StartedAt,
                    CompletedAt  = r.CompletedAt,
                    ErrorMessage = r.ErrorMessage,
                    Output       = r.Output,
                };

                List<string>? lines = JsonSerializer.Deserialize<List<string>>(r.LogLinesJson);
                if (lines is not null)
                    foreach (string line in lines)
                        job.LogLines.Add(line);

                return job;
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load compose jobs");
            return [];
        }
    }
}
