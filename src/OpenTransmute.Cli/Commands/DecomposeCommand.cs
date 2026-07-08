using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using OpenTransmute.Jobs;
using OpenTransmute.Models;

namespace OpenTransmute.Cli.Commands;

/// <summary>
/// <c>decompose</c> command — reduces a codebase to a language-agnostic specification by running a
/// <see cref="OpenTransmute.Jobs.DecomposeJob"/> through the multi-phase orchestrator.
/// </summary>
internal static class DecomposeCommand
{
    /// <summary>Builds the <c>decompose</c> command, wiring its options and run action.</summary>
    internal static Command Build(IServiceProvider sp, CliSettings settings)
    {
        var cmd = new Command("decompose", "Decompose a codebase into a language-agnostic specification");

        var sourceArg    = new Argument<string>("source") { Description = "Git URL or local folder path" };
        var projectOpt   = new Option<string?>("--project", "-p") { Description = "Project name (auto-detected from source if blank)" };
        var startOpt     = new Option<int>("--start-phase") { Description = "Phase to start from (0 = fresh run)", DefaultValueFactory = _ => 0 };
        var startItemOpt = new Option<int?>("--start-item") { Description = "1-based expansion item to resume from within an expansion phase (e.g. 5 = skip items 1-4)" };
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
        var hintsOpt          = new Option<string?>("--hints") { Description = "Free-text domain hints injected into every phase prompt" };
        var appendResultsOpt  = new Option<bool?>("--append-results") { Description = "Override AppendResults output mode (true = force on, false = force off, omit = use phase default)" };

        cmd.Arguments.Add(sourceArg);
        cmd.Options.Add(projectOpt);
        cmd.Options.Add(startOpt);
        cmd.Options.Add(startItemOpt);
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
        cmd.Options.Add(appendResultsOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            string source = parseResult.GetValue(sourceArg)!;
            string? project = parseResult.GetValue(projectOpt);
            int start = parseResult.GetValue(startOpt);
            int? startItem = parseResult.GetValue(startItemOpt);
            int? end = parseResult.GetValue(endOpt);
            OrchestratorType? orch = parseResult.GetValue(orchOpt);
            string? apiKey = parseResult.GetValue(apiKeyOpt)
                            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            string? endpoint = parseResult.GetValue(endpointOpt);
            string? thick = parseResult.GetValue(thickOpt);
            string? regular = parseResult.GetValue(regularOpt);
            string? thin = parseResult.GetValue(thinOpt);
            int? maxTurns = parseResult.GetValue(maxTurnsOpt);
            int? maxTokens = parseResult.GetValue(maxTokensOpt);
            bool keepClone = parseResult.GetValue(keepCloneOpt);
            string? hints = parseResult.GetValue(hintsOpt);
            bool? appendResults = parseResult.GetValue(appendResultsOpt);

            OrchestratorType orchestratorType = orch ?? settings.Orchestrator;

            if (!JobConsole.ValidateOpenAiKey(orchestratorType, apiKey))
                return 1;

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
                StartItem       = startItem,
                EndPhase        = end,
                MaxTurns        = maxTurns   ?? settings.MaxTurns,
                MaxOutputTokens = maxTokens  ?? settings.MaxOutputTokens,
                OutputRoot      = Directory.GetCurrentDirectory(),
                KeepClone       = keepClone,
                Hints           = hints,
            };

            // --append-results applies as a blanket override to all phases.
            if (appendResults.HasValue)
            {
                options.PhaseOverrides = new Dictionary<int, PhaseOverride>();
                for (int phase = 0; phase <= 7; phase++)
                    options.PhaseOverrides[phase] = new PhaseOverride { UseAppendResults = appendResults.Value };
            }

            var jobStore = sp.GetRequiredService<JobStore>();
            var jobQueue = sp.GetRequiredService<JobQueue>();

            var job = new DecomposeJob { Options = options };
            jobStore.Add(job);

            Console.WriteLine($"Decomposing: {source}");
            Console.WriteLine($"Engine:      {orchestratorType}");
            if (orchestratorType != OrchestratorType.ClaudeCode && !string.IsNullOrWhiteSpace(options.RegularModel))
                Console.WriteLine($"Model:       {options.RegularModel}");
            Console.WriteLine();

            await JobConsole.RunToCompletionAsync(job, t => jobQueue.EnqueueAsync(job, t), ct);
            return JobConsole.WriteVerdict(job);
        });

        return cmd;
    }
}
