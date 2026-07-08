using OpenTransmute.Jobs;
using OpenTransmute.Models;

namespace OpenTransmute.Cli.Commands;

/// <summary>
/// Shared console plumbing for the run-a-job CLI commands (compose, decompose, implement, verify).
/// Centralizes the OpenAI key guard and the log-pump / completion-wait loop that every job command
/// would otherwise copy verbatim — the closure over <c>logCursor</c> and the <see cref="TaskCompletionSource"/>
/// is the kind of boilerplate that drifts out of sync the moment it lives in four places.
/// </summary>
internal static class JobConsole
{
    #region Methods

    /// <summary>
    /// Validates that an OpenAI API key is present when the OpenAI orchestrator is selected.
    /// Writes an error to stderr and returns false when the key is required but missing.
    /// </summary>
    /// <param name="orchestrator">The resolved orchestrator engine for this run.</param>
    /// <param name="apiKey">The API key resolved from a CLI option or environment variable; may be null.</param>
    /// <returns><c>true</c> when the configuration is usable; <c>false</c> when the key is required but absent.</returns>
    internal static bool ValidateOpenAiKey(OrchestratorType orchestrator, string? apiKey)
    {
        if (orchestrator == OrchestratorType.OpenAI && string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("Error: OpenAI API key is required. Pass --api-key or set OPENAI_API_KEY.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Subscribes to the job's change events to stream new log lines to the console, enqueues the job
    /// via the supplied typed overload, and waits until it reaches a terminal state. Prints a trailing
    /// blank line once the job finishes so callers can append a verdict or summary.
    /// </summary>
    /// <param name="job">The job to monitor; its <see cref="JobBase.OnChanged"/> event drives log output.</param>
    /// <param name="enqueue">Callback that enqueues <paramref name="job"/> on the correct typed queue overload.</param>
    /// <param name="ct">Cancellation token forwarded to the enqueue operation.</param>
    internal static async Task RunToCompletionAsync(JobBase job, Func<CancellationToken, ValueTask> enqueue, CancellationToken ct)
    {
        int logCursor = 0;
        TaskCompletionSource done = new();

        // Subscribe before enqueueing so no log lines or the terminal transition can be missed.
        job.OnChanged += () =>
        {
            string[] snapshot = job.GetLogSnapshot();
            while (logCursor < snapshot.Length)
                Console.WriteLine(snapshot[logCursor++]);
            if (job.Status is JobStatus.Completed or JobStatus.Failed)
                done.TrySetResult();
        };

        await enqueue(ct);
        await done.Task;

        Console.WriteLine();
    }

    /// <summary>
    /// Writes the standard completion verdict for a finished job and maps it to a process exit code.
    /// </summary>
    /// <param name="job">The completed or failed job.</param>
    /// <returns><c>0</c> when the job completed successfully; <c>1</c> when it failed.</returns>
    internal static int WriteVerdict(JobBase job)
    {
        if (job.Status == JobStatus.Completed)
        {
            Console.WriteLine($"Completed in {job.TotalElapsed?.ToString(@"mm\:ss") ?? "??:??"}.");
            return 0;
        }

        Console.Error.WriteLine($"Failed: {job.ErrorMessage}");
        return 1;
    }

    #endregion
}
