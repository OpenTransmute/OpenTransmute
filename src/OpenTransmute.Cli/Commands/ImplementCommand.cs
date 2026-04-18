using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using OpenTransmute.Jobs;
using OpenTransmute.Models;

namespace OpenTransmute.Cli.Commands;

internal static class ImplementCommand
{
    internal static Command Build(IServiceProvider sp, CliSettings settings)
    {
        var cmd = new Command("implement", "Generate working code from a compose output spec file");

        var specArg      = new Argument<string>("spec") { Description = "Path to the compose output spec file (e.g. Output/Composition/my-design/compose-output.md)" };
        var outputOpt    = new Option<string?>("--output", "-o") { Description = "Output directory for generated code (default: ./Output/Implementation/<project>)" };
        var projectOpt   = new Option<string?>("--project", "-p") { Description = "Project name for the implementation (inferred from spec path if blank)" };
        var orchOpt      = new Option<OrchestratorType?>("--orchestrator") { Description = "Override engine (ClaudeCode | OpenAI | Ollama)" };
        var apiKeyOpt    = new Option<string?>("--api-key") { Description = "OpenAI API key (overrides OPENAI_API_KEY env var)" };
        var endpointOpt  = new Option<string?>("--endpoint") { Description = "Custom OpenAI-compatible endpoint (e.g. Azure)" };
        var modelOpt     = new Option<string?>("--model") { Description = "Model override (defaults to thick model for maximum capability)" };
        var maxTurnsOpt  = new Option<int?>("--max-turns") { Description = "Max agent turns (default: 200)" };
        var timeoutOpt   = new Option<int?>("--timeout") { Description = "HTTP timeout in minutes (default: 60)" };

        cmd.Arguments.Add(specArg);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(projectOpt);
        cmd.Options.Add(orchOpt);
        cmd.Options.Add(apiKeyOpt);
        cmd.Options.Add(endpointOpt);
        cmd.Options.Add(modelOpt);
        cmd.Options.Add(maxTurnsOpt);
        cmd.Options.Add(timeoutOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var specPath   = parseResult.GetValue(specArg)!;
            var outputDir  = parseResult.GetValue(outputOpt);
            var project    = parseResult.GetValue(projectOpt);
            var orch       = parseResult.GetValue(orchOpt);
            var apiKey     = parseResult.GetValue(apiKeyOpt)
                             ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var endpoint   = parseResult.GetValue(endpointOpt);
            var model      = parseResult.GetValue(modelOpt);
            var maxTurns   = parseResult.GetValue(maxTurnsOpt);
            var timeout    = parseResult.GetValue(timeoutOpt);

            if (!File.Exists(specPath))
            {
                Console.Error.WriteLine($"Error: spec file not found: {specPath}");
                return 1;
            }

            var orchestratorType = orch ?? settings.Orchestrator;

            if (orchestratorType == OrchestratorType.OpenAI && string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine("Error: OpenAI API key is required. Pass --api-key or set OPENAI_API_KEY.");
                return 1;
            }

            string specContent = await File.ReadAllTextAsync(specPath, ct);

            string projectName = project
                ?? Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(specPath)))
                ?? "implementation";

            string resolvedOutputDir = outputDir
                ?? Path.Combine(Directory.GetCurrentDirectory(), "Output", "Implementation", projectName);

            var options = new ImplementOptions
            {
                SpecContent      = specContent,
                OutputDirectory  = resolvedOutputDir,
                ProjectName      = projectName,
                Label            = Path.GetFileName(specPath),
                Orchestrator     = orchestratorType,
                Model            = model ?? settings.ThickModel,
                OpenAiApiKey     = apiKey,
                OpenAiEndpoint   = endpoint ?? settings.OpenAiEndpoint,
                MaxTurns         = maxTurns ?? 200,
                TimeoutMinutes   = timeout ?? 60,
            };

            var jobStore = sp.GetRequiredService<JobStore>();
            var jobQueue = sp.GetRequiredService<JobQueue>();

            var job = new ImplementJob { Options = options };
            jobStore.Add(job);

            Console.WriteLine($"Implementing: {specPath}");
            Console.WriteLine($"Project:      {projectName}");
            Console.WriteLine($"Output:       {resolvedOutputDir}");
            Console.WriteLine($"Engine:       {orchestratorType}");
            Console.WriteLine();

            int logCursor = 0;
            var done = new TaskCompletionSource();

            job.OnChanged += () =>
            {
                string[] snapshot = job.GetLogSnapshot();
                while (logCursor < snapshot.Length)
                    Console.WriteLine(snapshot[logCursor++]);
                if (job.Status is JobStatus.Completed or JobStatus.Failed)
                    done.TrySetResult();
            };

            await jobQueue.EnqueueAsync(job, ct);
            await done.Task;

            Console.WriteLine();
            if (job.Status == JobStatus.Completed)
            {
                Console.WriteLine($"Completed in {job.TotalElapsed?.ToString(@"mm\:ss") ?? "??:??"}.");
                Console.WriteLine($"Output: {resolvedOutputDir}");
                return 0;
            }

            Console.Error.WriteLine($"Failed: {job.ErrorMessage}");
            return 1;
        });

        return cmd;
    }
}
