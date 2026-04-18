using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using OpenTransmute.Jobs;
using OpenTransmute.Models;

namespace OpenTransmute.Cli.Commands;

internal static class DecomposeCommand
{
    internal static Command Build(IServiceProvider sp, CliSettings settings)
    {
        var cmd = new Command("decompose", "Decompose a codebase into a language-agnostic specification");

        var sourceArg    = new Argument<string>("source") { Description = "Git URL or local folder path" };
        var projectOpt   = new Option<string?>("--project", "-p") { Description = "Project name (auto-detected from source if blank)" };
        var startOpt     = new Option<int>("--start-phase") { Description = "Phase to start from (0 = fresh run)", DefaultValueFactory = _ => 0 };
        var endOpt       = new Option<int?>("--end-phase") { Description = "Last phase to run inclusive (default = run all)" };
        var orchOpt      = new Option<OrchestratorType?>("--orchestrator") { Description = "Override engine (ClaudeCode | OpenAI | Ollama)" };
        var apiKeyOpt    = new Option<string?>("--api-key") { Description = "OpenAI API key (overrides OPENAI_API_KEY env var)" };
        var endpointOpt  = new Option<string?>("--endpoint") { Description = "Custom OpenAI-compatible endpoint (e.g. Azure)" };
        var thickOpt     = new Option<string?>("--thick-model") { Description = "Model for heavy phases" };
        var regularOpt   = new Option<string?>("--regular-model") { Description = "Model for normal phases" };
        var thinOpt      = new Option<string?>("--thin-model") { Description = "Model for light phases" };
        var maxTurnsOpt  = new Option<int?>("--max-turns") { Description = "Max agent turns per phase" };
        var maxTokensOpt = new Option<int?>("--max-tokens") { Description = "Max output tokens per call (0 = auto per phase)" };
        var keepCloneOpt = new Option<bool>("--keep-clone") { Description = "Keep the cloned repo directory after completion" };
        var hintsOpt     = new Option<string?>("--hints") { Description = "Free-text domain hints injected into every phase prompt" };

        cmd.Arguments.Add(sourceArg);
        cmd.Options.Add(projectOpt);
        cmd.Options.Add(startOpt);
        cmd.Options.Add(endOpt);
        cmd.Options.Add(orchOpt);
        cmd.Options.Add(apiKeyOpt);
        cmd.Options.Add(endpointOpt);
        cmd.Options.Add(thickOpt);
        cmd.Options.Add(regularOpt);
        cmd.Options.Add(thinOpt);
        cmd.Options.Add(maxTurnsOpt);
        cmd.Options.Add(maxTokensOpt);
        cmd.Options.Add(keepCloneOpt);
        cmd.Options.Add(hintsOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var source    = parseResult.GetValue(sourceArg)!;
            var project   = parseResult.GetValue(projectOpt);
            var start     = parseResult.GetValue(startOpt);
            var end       = parseResult.GetValue(endOpt);
            var orch      = parseResult.GetValue(orchOpt);
            var apiKey    = parseResult.GetValue(apiKeyOpt)
                            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var endpoint  = parseResult.GetValue(endpointOpt);
            var thick     = parseResult.GetValue(thickOpt);
            var regular   = parseResult.GetValue(regularOpt);
            var thin      = parseResult.GetValue(thinOpt);
            var maxTurns  = parseResult.GetValue(maxTurnsOpt);
            var maxTokens = parseResult.GetValue(maxTokensOpt);
            var keepClone = parseResult.GetValue(keepCloneOpt);
            var hints     = parseResult.GetValue(hintsOpt);

            var orchestratorType = orch ?? settings.Orchestrator;

            if (orchestratorType == OrchestratorType.OpenAI && string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine("Error: OpenAI API key is required. Pass --api-key or set OPENAI_API_KEY.");
                return 1;
            }

            var options = new DecomposeOptions
            {
                Source          = source,
                ProjectName     = project ?? string.Empty,
                Orchestrator    = orchestratorType,
                ThickModel      = thick    ?? settings.ThickModel,
                RegularModel    = regular  ?? settings.RegularModel,
                ThinModel       = thin     ?? settings.ThinModel,
                OpenAiApiKey    = apiKey,
                OpenAiEndpoint  = endpoint ?? settings.OpenAiEndpoint,
                StartPhase      = start,
                EndPhase        = end,
                MaxTurns        = maxTurns   ?? settings.MaxTurns,
                MaxOutputTokens = maxTokens  ?? settings.MaxOutputTokens,
                OutputRoot      = Directory.GetCurrentDirectory(),
                KeepClone       = keepClone,
                Hints           = hints,
            };

            var jobStore = sp.GetRequiredService<JobStore>();
            var jobQueue = sp.GetRequiredService<JobQueue>();

            var job = new DecomposeJob { Options = options };
            jobStore.Add(job);

            Console.WriteLine($"Decomposing: {source}");
            Console.WriteLine($"Engine:      {orchestratorType}");
            if (orchestratorType != OrchestratorType.ClaudeCode && !string.IsNullOrWhiteSpace(options.RegularModel))
                Console.WriteLine($"Model:       {options.RegularModel}");
            Console.WriteLine();

            int logCursor = 0;
            var done = new TaskCompletionSource();

            job.OnChanged += () =>
            {
                var snapshot = job.GetLogSnapshot();
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
                return 0;
            }

            Console.Error.WriteLine($"Failed: {job.ErrorMessage}");
            return 1;
        });

        return cmd;
    }
}
