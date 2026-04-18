using Microsoft.EntityFrameworkCore;
using OpenTransmute.Data;
using OpenTransmute.Inventory;
using OpenTransmute.Models;
using OpenTransmute.Orchestration;
using OpenTransmute.Source;

namespace OpenTransmute.Jobs;

/// <summary>
/// Background service that dequeues jobs and dispatches them to <see cref="JobOrchestrator"/>.
/// Handles source fetching (Decompose), event-to-state translation, persistence, and inventory import.
/// </summary>
public class JobRunner(
    JobQueue queue,
    JobOrchestrator orchestrator,
    IEnumerable<ISourceFetcher> sourceFetchers,
    InventoryParser inventoryParser,
    InventoryExporter inventoryExporter,
    JobPersistenceService decomposePersistence,
    ComposeJobPersistenceService composePersistence,
    ImplementJobPersistenceService implementPersistence,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<JobRunner> logger) : BackgroundService
{
    #region Members

    // Decompose phase 6 is "Composition Inventory" — its completion triggers the inventory import.
    private const int InventoryPhaseNumber = 6;

    #endregion

    #region Methods

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (object job in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                switch (job)
                {
                    case DecomposeJob dj:
                        await RunDecomposeJobAsync(dj, stoppingToken);
                        break;

                    case ComposeJob cj:
                        await RunComposeJobAsync(cj, stoppingToken);
                        break;

                    case ImplementJob ij:
                        await RunImplementJobAsync(ij, stoppingToken);
                        break;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Unhandled error in job runner");

                if (job is DecomposeJob dj2)
                {
                    dj2.Status = JobStatus.Failed;
                    dj2.ErrorMessage = ex.Message;
                    dj2.NotifyChanged();
                    await decomposePersistence.SaveAsync(dj2, stoppingToken);
                }
                else if (job is ComposeJob cj2)
                {
                    cj2.Status       = JobStatus.Failed;
                    cj2.CompletedAt  = DateTime.UtcNow;
                    cj2.ErrorMessage = ex.Message;
                    cj2.NotifyChanged();
                    await composePersistence.SaveAsync(cj2, stoppingToken);
                }
                else if (job is ImplementJob ij2)
                {
                    ij2.Status       = JobStatus.Failed;
                    ij2.CompletedAt  = DateTime.UtcNow;
                    ij2.ErrorMessage = ex.Message;
                    ij2.NotifyChanged();
                    await implementPersistence.SaveAsync(ij2, stoppingToken);
                }
            }
        }
    }

    private async Task RunDecomposeJobAsync(DecomposeJob job, CancellationToken ct)
    {
        DecomposeOptions options = job.Options;

        ISourceFetcher fetcher = sourceFetchers.FirstOrDefault(f => f.CanHandle(options.Source))
            ?? throw new InvalidOperationException($"No source fetcher can handle: {options.Source}");

        job.AppendLog($"Fetching source: {options.Source}");
        SourceResult sourceResult = await fetcher.FetchAsync(options.Source, options, ct);

        string projectName = string.IsNullOrWhiteSpace(options.ProjectName)
            ? sourceResult.InferredProjectName
            : options.ProjectName;
        options.ProjectName  = projectName;
        job.LocalSourcePath  = sourceResult.LocalPath;

        job.AppendLog($"Project: {projectName} | Source: {sourceResult.LocalPath}");

        job.Status = JobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.NotifyChanged();

        await ClearPhaseOutputsAsync(projectName, ct);

        await foreach (PhaseEvent evt in orchestrator.RunAsync(job, ct))
        {
            switch (evt)
            {
                case PhaseStarted s:
                    job.PhaseStarted(s.PhaseNumber);
                    job.AppendLog($"--- Phase {s.PhaseNumber}: {s.PhaseName} ---");
                    break;

                case PhaseCompleted c:
                    string preview = await ReadPreviewAsync(c.OutputPath, 500, logger);
                    job.PhaseCompleted(c.PhaseNumber, preview, c.Tokens);
                    job.AppendLog($"Phase {c.PhaseNumber} completed." +
                        (c.Tokens.Total > 0 ? $" [{c.Tokens.InputTokens:N0}→{c.Tokens.OutputTokens:N0} tokens]" : string.Empty));
                    await decomposePersistence.SaveAsync(job, ct);
                    await SavePhaseOutputAsync(projectName, c.PhaseNumber, c.OutputPath, ct);

                    if (c.PhaseNumber == InventoryPhaseNumber)
                        await ImportInventoryAsync(job, options, c.OutputPath, sourceResult.LocalPath, ct);
                    break;

                case PhaseFailed f:
                    job.PhaseFailed(f.PhaseNumber, f.Error);
                    job.AppendLog($"Phase {f.PhaseNumber} FAILED: {f.Error}");
                    logger.LogError("Phase {Phase} failed for {Project}: {Error}", f.PhaseNumber, projectName, f.Error);
                    await decomposePersistence.SaveAsync(job, ct);
                    break;

                case LogLine l:
                    job.AppendLog(l.Text);
                    break;

                case ExpansionItemStarted ei:
                    job.AppendLog($"  [{ei.PhaseNumber}] Group {ei.ItemIndex}: {ei.ItemName}");
                    break;

                case ExpansionItemCompleted ec:
                    job.AppendLog($"  [{ec.PhaseNumber}] Group {ec.ItemIndex} done: {Path.GetFileName(ec.OutputPath)}" +
                        (ec.Tokens.Total > 0 ? $" [{ec.Tokens.Total:N0} tokens]" : string.Empty));
                    await SavePhaseOutputAsync(projectName, ec.PhaseNumber, ec.OutputPath, ct);
                    break;
            }
        }

        job.Status = job.Phases.Any(p => p.Status == JobStatus.Failed)
            ? JobStatus.Failed : JobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        if (job.TotalTokens.Total > 0 || job.TotalTokens.CostUsd.HasValue)
            job.AppendLog($"Total tokens: {job.TotalTokens.InputTokens:N0} in / {job.TotalTokens.OutputTokens:N0} out" +
                (job.TotalTokens.CostUsd.HasValue ? $" | cost ~${job.TotalTokens.CostUsd:F4}" : string.Empty));
        job.NotifyChanged();
        await decomposePersistence.SaveAsync(job, ct);

        if (sourceResult.IsTemporary && !options.KeepClone)
        {
            try { Directory.Delete(sourceResult.LocalPath, recursive: true); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to delete temp clone at {Path}", sourceResult.LocalPath); }
        }
    }

    private async Task RunComposeJobAsync(ComposeJob job, CancellationToken ct)
    {
        job.Status = JobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.NotifyChanged();

        await foreach (PhaseEvent evt in orchestrator.RunAsync(job, ct))
        {
            switch (evt)
            {
                case PhaseCompleted c:
                    job.TotalTokens += c.Tokens;
                    job.NotifyChanged();
                    await composePersistence.SaveAsync(job, ct);
                    break;

                case PhaseFailed f:
                    job.Status = JobStatus.Failed;
                    job.ErrorMessage = f.Error;
                    job.NotifyChanged();
                    await composePersistence.SaveAsync(job, ct);
                    break;

                case LogLine l:
                    job.AppendLog(l.Text);
                    break;
            }
        }

        if (job.Status != JobStatus.Failed)
        {
            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
        }
        job.NotifyChanged();
        await composePersistence.SaveAsync(job, ct);
    }

    private async Task RunImplementJobAsync(ImplementJob job, CancellationToken ct)
    {
        job.Status = JobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.NotifyChanged();

        await foreach (PhaseEvent evt in orchestrator.RunAsync(job, ct))
        {
            switch (evt)
            {
                case PhaseCompleted c:
                    job.TotalTokens += c.Tokens;
                    job.NotifyChanged();
                    await implementPersistence.SaveAsync(job, ct);
                    break;

                case PhaseFailed f:
                    job.Status = JobStatus.Failed;
                    job.ErrorMessage = f.Error;
                    job.NotifyChanged();
                    await implementPersistence.SaveAsync(job, ct);
                    break;

                case LogLine l:
                    job.AppendLog(l.Text);
                    break;
            }
        }

        if (job.Status != JobStatus.Failed)
        {
            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
        }
        job.NotifyChanged();
        await implementPersistence.SaveAsync(job, ct);
    }

    private async Task ImportInventoryAsync(
        DecomposeJob job, DecomposeOptions options, string? outputPath, string sourcePath, CancellationToken ct)
    {
        if (outputPath is null) return;
        try
        {
            job.AppendLog("Importing composition inventory into database...");
            string projectDir = Path.GetDirectoryName(outputPath)!;
            await inventoryParser.ParseAndImportAsync(outputPath, options.ProjectName, sourcePath, ct);
            await inventoryExporter.ExportProjectAsync(options.ProjectName, projectDir, ct);
            job.AppendLog("Inventory import complete.");
        }
        catch (Exception ex)
        {
            job.AppendLog($"Inventory import failed (non-fatal): {ex.Message}");
            logger.LogWarning(ex, "Inventory import failed for {Project}", options.ProjectName);
        }
    }

    private static async Task<string> ReadPreviewAsync(string? path, int maxChars, ILogger logger)
    {
        if (path is null || !File.Exists(path)) return string.Empty;
        try
        {
            using StreamReader sr = new StreamReader(path);
            char[] buf = new char[maxChars];
            int read = await sr.ReadAsync(buf, 0, maxChars);
            string text = new string(buf, 0, read);
            return read == maxChars ? text + "…" : text;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ReadPreviewAsync: failed to read preview from {Path}", path);
            return string.Empty;
        }
    }

    private async Task ClearPhaseOutputsAsync(string projectName, CancellationToken ct)
    {
        try
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            DecomposedProject? project = await db.Projects
                .FirstOrDefaultAsync(p => p.Name == projectName, ct);
            if (project is null) return;

            db.PhaseOutputs.RemoveRange(db.PhaseOutputs.Where(o => o.ProjectId == project.Id));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clear phase outputs for {Project}", projectName);
        }
    }

    private async Task SavePhaseOutputAsync(
        string projectName, int phaseNumber, string? outputPath, CancellationToken ct)
    {
        if (outputPath is null || !File.Exists(outputPath)) return;
        try
        {
            string content = await File.ReadAllTextAsync(outputPath, ct);
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);

            DecomposedProject? project = await db.Projects
                .FirstOrDefaultAsync(p => p.Name == projectName, ct);
            if (project is null) return;

            db.PhaseOutputs.Add(new DecomposePhaseOutput
            {
                Id          = Guid.NewGuid(),
                ProjectId   = project.Id,
                PhaseNumber = phaseNumber,
                Filename    = Path.GetFileName(outputPath),
                Content     = content,
                CreatedAt   = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save phase {Phase} output for {Project}", phaseNumber, projectName);
        }
    }

    #endregion
}
