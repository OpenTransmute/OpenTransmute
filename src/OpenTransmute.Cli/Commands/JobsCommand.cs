using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using OpenTransmute.Jobs;

namespace OpenTransmute.Cli.Commands;

/// <summary>
/// <c>jobs</c> command — lists and inspects persisted decompose and compose jobs, including their
/// phase/document progress.
/// </summary>
internal static class JobsCommand
{
    /// <summary>Builds the <c>jobs</c> command, wiring its subcommands and run action.</summary>
    internal static Command Build(IServiceProvider sp)
    {
        var cmd = new Command("jobs", "List and inspect decompose and compose jobs");

        var idOpt     = new Option<Guid?>("--id") { Description = "Show full detail for a specific job ID" };
        var typeOpt   = new Option<string?>("--type") { Description = "Filter by type: decompose or compose" };

        cmd.Options.Add(idOpt);
        cmd.Options.Add(typeOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            Guid? id = parseResult.GetValue(idOpt);
            string? typeFilter = parseResult.GetValue(typeOpt)?.ToLowerInvariant();

            var jobStore = sp.GetRequiredService<JobStore>();

            if (id.HasValue)
            {
                DecomposeJob? dj = jobStore.GetDecompose(id.Value);
                if (dj is not null) { PrintDecomposeDetail(dj); return 0; }

                ComposeJob? cj = jobStore.GetCompose(id.Value);
                if (cj is not null) { PrintComposeDetail(cj); return 0; }

                Console.Error.WriteLine($"Job {id} not found.");
                return 1;
            }

            bool showDecompose = typeFilter is null or "decompose";
            bool showCompose   = typeFilter is null or "compose";

            if (showDecompose)
                PrintDecomposeList(jobStore.AllDecomposeJobs.ToList());

            if (showDecompose && showCompose)
                Console.WriteLine();

            if (showCompose)
                PrintComposeList(jobStore.AllComposeJobs.ToList());

            return await Task.FromResult(0);
        });

        return cmd;
    }

    /// <summary>Prints a tabular summary of all decompose jobs (id, project, status, timing).</summary>
    private static void PrintDecomposeList(List<DecomposeJob> jobs)
    {
        Console.WriteLine($"Decompose  ({jobs.Count})");
        Console.WriteLine(new string('-', 100));
        if (!jobs.Any())
        {
            Console.WriteLine("  (none)");
            return;
        }
        Console.WriteLine($"  {"ID",-36}  {"Project",-22}  {"Status",-10}  {"Started",-17}  Elapsed");
        foreach (var j in jobs)
            Console.WriteLine($"  {j.Id,-36}  {j.Options.ProjectName,-22}  {j.Status,-10}  {j.CreatedAt:yyyy-MM-dd HH:mm}  {j.TotalElapsed?.ToString(@"mm\:ss") ?? "—"}");
    }

    /// <summary>Prints a tabular summary of all compose jobs (id, output, status, timing).</summary>
    private static void PrintComposeList(List<ComposeJob> jobs)
    {
        Console.WriteLine($"Compose  ({jobs.Count})");
        Console.WriteLine(new string('-', 100));
        if (!jobs.Any())
        {
            Console.WriteLine("  (none)");
            return;
        }
        Console.WriteLine($"  {"ID",-36}  {"Output",-22}  {"Status",-10}  {"Started",-17}  Elapsed");
        foreach (var j in jobs)
            Console.WriteLine($"  {j.Id,-36}  {j.Options.OutputName,-22}  {j.Status,-10}  {j.CreatedAt:yyyy-MM-dd HH:mm}  {j.TotalElapsed?.ToString(@"mm\:ss") ?? "—"}");
    }

    /// <summary>Prints the full detail of a single decompose job: metadata, per-phase status, and log.</summary>
    private static void PrintDecomposeDetail(DecomposeJob job)
    {
        Console.WriteLine($"ID:       {job.Id}");
        Console.WriteLine($"Project:  {job.Options.ProjectName}");
        Console.WriteLine($"Source:   {job.Options.Source}");
        Console.WriteLine($"Engine:   {job.Options.Orchestrator}");
        Console.WriteLine($"Status:   {job.Status}");
        Console.WriteLine($"Created:  {job.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        if (job.TotalElapsed.HasValue)
            Console.WriteLine($"Elapsed:  {job.TotalElapsed.Value:mm\\:ss}");
        if (!string.IsNullOrWhiteSpace(job.ErrorMessage))
            Console.WriteLine($"Error:    {job.ErrorMessage}");
        if (job.TotalTokens.Total > 0)
            Console.WriteLine($"Tokens:   {job.TotalTokens.InputTokens:N0} in / {job.TotalTokens.OutputTokens:N0} out");

        Console.WriteLine();
        Console.WriteLine("Phases:");
        foreach (var phase in job.Phases)
        {
            string elapsed = phase.Elapsed?.ToString(@"mm\:ss") ?? "—";
            Console.WriteLine($"  {phase.PhaseNumber}  {phase.PhaseName,-30}  {phase.Status,-10}  {elapsed}");
            if (!string.IsNullOrWhiteSpace(phase.ErrorMessage))
                Console.WriteLine($"     Error: {phase.ErrorMessage}");
        }

        string[] logSnapshot = job.GetLogSnapshot();
        if (logSnapshot.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Log:");
            foreach (var line in logSnapshot)
                Console.WriteLine($"  {line}");
        }
    }

    /// <summary>Prints the full detail of a single compose job: metadata, status, and log.</summary>
    private static void PrintComposeDetail(ComposeJob job)
    {
        Console.WriteLine($"ID:       {job.Id}");
        Console.WriteLine($"Output:   {job.Options.OutputName}");
        Console.WriteLine($"Engine:   {job.Options.Orchestrator}");
        Console.WriteLine($"Status:   {job.Status}");
        Console.WriteLine($"Created:  {job.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        if (job.TotalElapsed.HasValue)
            Console.WriteLine($"Elapsed:  {job.TotalElapsed.Value:mm\\:ss}");
        if (!string.IsNullOrWhiteSpace(job.ErrorMessage))
            Console.WriteLine($"Error:    {job.ErrorMessage}");

        string[] logSnapshot = job.GetLogSnapshot();
        if (logSnapshot.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Log:");
            foreach (string line in logSnapshot)
                Console.WriteLine($"  {line}");
        }

        if (!string.IsNullOrWhiteSpace(job.Output))
        {
            Console.WriteLine();
            Console.WriteLine("Output:");
            Console.WriteLine(job.Output);
        }
    }
}
