using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTransmute.Jobs;

/// <summary>
/// Saves DecomposeJob state to DB/Jobs/&lt;project&gt;/job.json after each phase
/// and loads all persisted jobs at startup so history survives app restarts.
/// Uses an atomic tmp + rename pattern so partial writes are never visible to readers.
/// </summary>
public class JobPersistenceService(ILogger<JobPersistenceService> logger)
{
    #region Members

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    #endregion

    #region Methods

    /// <summary>
    /// Writes a snapshot of the job to DB/Jobs/&lt;project&gt;/job.json.
    /// Safe to call after every phase — uses atomic tmp + move.
    /// Silently skips if the job has no project name or output root configured.
    /// </summary>
    /// <param name="job">The job to persist.</param>
    /// <param name="ct">Cancellation token propagated from the caller.</param>
    public async Task SaveAsync(DecomposeJob job, CancellationToken ct = default)
    {
        string? projectName = job.Options?.ProjectName;
        string? outputRoot = job.Options?.OutputRoot;

        if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(outputRoot))
            return;

        string dir = Path.Combine(outputRoot, "DB", "Jobs", projectName);
        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, "job.json");
        string tmp = path + ".tmp";

        try
        {
            JobRecord record = JobRecord.From(job);
            string json = JsonSerializer.Serialize(record, JsonOptions);
            await File.WriteAllTextAsync(tmp, json, ct);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save job {Id} to {Path}", job.Id, path);
        }
    }

    /// <summary>
    /// Deletes the old job.json file when a job's project name changes.
    /// Safe to call even if the old file doesn't exist. Removes the containing
    /// directory if it becomes empty.
    /// </summary>
    /// <param name="outputRoot">The root directory where DB/Jobs/ lives.</param>
    /// <param name="oldProjectName">The previous project name before the rename.</param>
    public void DeleteJobFile(string outputRoot, string oldProjectName)
    {
        if (string.IsNullOrWhiteSpace(outputRoot) || string.IsNullOrWhiteSpace(oldProjectName))
            return;

        string dir = Path.Combine(outputRoot, "DB", "Jobs", oldProjectName);
        string path = Path.Combine(dir, "job.json");

        try
        {
            if (File.Exists(path))
                File.Delete(path);

            if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                Directory.Delete(dir);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete old job file at {Path}", path);
        }
    }

    /// <summary>
    /// Scans DB/Jobs/*/job.json under <paramref name="outputRoot"/> and returns
    /// hydrated DecomposeJob instances (all in a terminal state — never running).
    /// </summary>
    /// <param name="outputRoot">The root directory where DB/Jobs/ lives.</param>
    /// <returns>All persisted jobs, with Running status downgraded to Failed.</returns>
    public IEnumerable<DecomposeJob> LoadAll(string outputRoot)
    {
        string outputDir = Path.Combine(outputRoot, "DB", "Jobs");
        if (!Directory.Exists(outputDir))
            yield break;

        foreach (string dir in Directory.GetDirectories(outputDir))
        {
            string path = Path.Combine(dir, "job.json");
            if (!File.Exists(path)) continue;

            DecomposeJob? job = null;
            try
            {
                string json = File.ReadAllText(path);
                JobRecord? record = JsonSerializer.Deserialize<JobRecord>(json, JsonOptions);
                if (record is not null)
                {
                    job = record.ToJob();
                    // Jobs loaded from disk are never in a mid-run state
                    if (job.Status is JobStatus.Running or JobStatus.Pending)
                        job.Status = JobStatus.Failed;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load job from {Path}", path);
            }

            if (job is not null) yield return job;
        }
    }

    #endregion
}
