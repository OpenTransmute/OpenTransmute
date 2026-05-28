using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using OpenTransmute.Jobs;
using OpenTransmute.Models;

namespace OpenTransmute.Cli.Commands;

internal static class VerifyCommand
{
    internal static Command Build(IServiceProvider sp, CliSettings settings)
    {
        var cmd = new Command("verify", "Verify decomposition output accuracy against the original source code");

        var projectArg  = new Argument<string>("project") { Description = "Project name (must match decomposition output directory)" };
        var sourceArg   = new Argument<string>("source") { Description = "Path to the original source code directory" };
        var orchOpt     = new Option<OrchestratorType?>("--orchestrator") { Description = "Override engine (ClaudeCode | CopilotCli | OpenAI | Ollama)" };
        var modelOpt    = new Option<string?>("--model") { Description = "Model name (overrides default)" };
        var apiKeyOpt   = new Option<string?>("--api-key") { Description = "OpenAI API key (overrides OPENAI_API_KEY env var)" };
        var endpointOpt = new Option<string?>("--endpoint") { Description = "Custom OpenAI-compatible endpoint" };
        var maxTurnsOpt = new Option<int?>("--max-turns") { Description = "Max agent turns per document" };
        var maxTokensOpt = new Option<int?>("--max-tokens") { Description = "Max output tokens per call" };
        var hintsOpt    = new Option<string?>("--hints") { Description = "Free-text hints to focus the audit on specific concerns" };

        cmd.Arguments.Add(projectArg);
        cmd.Arguments.Add(sourceArg);
        cmd.Options.Add(orchOpt);
        cmd.Options.Add(modelOpt);
        cmd.Options.Add(apiKeyOpt);
        cmd.Options.Add(endpointOpt);
        cmd.Options.Add(maxTurnsOpt);
        cmd.Options.Add(maxTokensOpt);
        cmd.Options.Add(hintsOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            string project  = parseResult.GetValue(projectArg)!;
            string source   = parseResult.GetValue(sourceArg)!;
            OrchestratorType? orch = parseResult.GetValue(orchOpt);
            string? model   = parseResult.GetValue(modelOpt);
            string? apiKey  = parseResult.GetValue(apiKeyOpt)
                              ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            string? endpoint = parseResult.GetValue(endpointOpt);
            int? maxTurns   = parseResult.GetValue(maxTurnsOpt);
            int? maxTokens  = parseResult.GetValue(maxTokensOpt);
            string? hints   = parseResult.GetValue(hintsOpt);

            OrchestratorType orchestratorType = orch ?? settings.Orchestrator;

            if (orchestratorType == OrchestratorType.OpenAI && string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine("Error: OpenAI API key is required. Pass --api-key or set OPENAI_API_KEY.");
                return 1;
            }

            // Resolve source to an absolute path.
            string sourcePath = Path.GetFullPath(source);
            if (!Directory.Exists(sourcePath))
            {
                Console.Error.WriteLine($"Error: Source directory not found: {sourcePath}");
                return 1;
            }

            VerifyOptions options = new VerifyOptions
            {
                ProjectName    = project,
                SourcePath     = sourcePath,
                Orchestrator   = orchestratorType,
                Model          = model ?? settings.RegularModel,
                OpenAiApiKey   = apiKey,
                OpenAiEndpoint = endpoint ?? settings.OpenAiEndpoint,
                MaxTurns       = maxTurns  ?? settings.MaxTurns,
                MaxOutputTokens = maxTokens ?? 32768,
                OutputRoot     = Directory.GetCurrentDirectory(),
                Hints          = hints,
            };

            JobStore jobStore = sp.GetRequiredService<JobStore>();
            JobQueue jobQueue = sp.GetRequiredService<JobQueue>();

            VerifyJob job = new VerifyJob { Options = options };
            jobStore.Add(job);

            Console.WriteLine($"Verifying:   {project}");
            Console.WriteLine($"Source:      {sourcePath}");
            Console.WriteLine($"Engine:      {orchestratorType}");
            if (!string.IsNullOrWhiteSpace(options.Model))
                Console.WriteLine($"Model:       {options.Model}");
            Console.WriteLine();

            int logCursor = 0;
            TaskCompletionSource done = new TaskCompletionSource();

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

            // Print per-document summary.
            int passed = job.Documents.Count(d => d.Status == JobStatus.Completed);
            int failed = job.Documents.Count(d => d.Status == JobStatus.Failed);
            Console.WriteLine($"Documents verified: {passed}/{job.Documents.Count}" +
                (failed > 0 ? $" ({failed} failed)" : string.Empty));

            if (job.Status == JobStatus.Completed)
            {
                Console.WriteLine($"Completed in {job.TotalElapsed?.ToString(@"mm\:ss") ?? "??:??"}.");
                return 0;
            }

            Console.Error.WriteLine($"Failed: {job.ErrorMessage}");
            return 1;
        });

        return cmd;
    }
}
