using System.CommandLine;
using OpenTransmute.Models;

namespace OpenTransmute.Cli.Commands;

/// <summary>
/// <c>settings</c> command — shows or updates the persisted CLI settings (defaults applied to
/// subsequent commands).
/// </summary>
internal static class SettingsCommand
{
    /// <summary>Builds the <c>settings</c> command, wiring its options and run action.</summary>
    internal static Command Build(CliSettings settings, CliSettingsStore store)
    {
        var cmd = new Command("settings", "Show or update persisted CLI settings");

        var orchOpt      = new Option<OrchestratorType?>("--orchestrator") { Description = "Default engine (ClaudeCode | CopilotCli | OpenAI | Ollama)" };
        var endpointOpt  = new Option<string?>("--endpoint") { Description = "Default OpenAI-compatible endpoint URL" };
        var thickOpt     = new Option<string?>("--thick-model") { Description = "Default model for heavy phases" };
        var regularOpt   = new Option<string?>("--regular-model") { Description = "Default model for normal phases" };
        var thinOpt      = new Option<string?>("--thin-model") { Description = "Default model for light phases" };
        var maxTurnsOpt  = new Option<int?>("--max-turns") { Description = "Default max agent turns per phase" };
        var maxTokensOpt = new Option<int?>("--max-tokens") { Description = "Default max output tokens (0 = auto)" };
        var timeoutOpt   = new Option<int?>("--timeout") { Description = "Default HTTP timeout in minutes" };
        var ethosOpt     = new Option<string?>("--user-ethos") { Description = "Personal coding standards injected into every Compose run (use empty string to clear)" };

        cmd.Options.Add(orchOpt);
        cmd.Options.Add(endpointOpt);
        cmd.Options.Add(thickOpt);
        cmd.Options.Add(regularOpt);
        cmd.Options.Add(thinOpt);
        cmd.Options.Add(maxTurnsOpt);
        cmd.Options.Add(maxTokensOpt);
        cmd.Options.Add(timeoutOpt);
        cmd.Options.Add(ethosOpt);

        cmd.SetAction((parseResult) =>
        {
            OrchestratorType? orch = parseResult.GetValue(orchOpt);
            string? endpoint = parseResult.GetValue(endpointOpt);
            string? thick = parseResult.GetValue(thickOpt);
            string? regular = parseResult.GetValue(regularOpt);
            string? thin = parseResult.GetValue(thinOpt);
            int? maxTurns = parseResult.GetValue(maxTurnsOpt);
            int? maxTokens = parseResult.GetValue(maxTokensOpt);
            int? timeout = parseResult.GetValue(timeoutOpt);
            string? ethos = parseResult.GetValue(ethosOpt);

            bool anyChange = orch.HasValue || endpoint is not null || thick is not null
                             || regular is not null || thin is not null
                             || maxTurns.HasValue || maxTokens.HasValue || timeout.HasValue
                             || ethos is not null;

            if (anyChange)
            {
                if (orch.HasValue)        settings.Orchestrator    = orch.Value;
                if (endpoint is not null) settings.OpenAiEndpoint  = endpoint;
                if (thick is not null)    settings.ThickModel      = thick;
                if (regular is not null)  settings.RegularModel    = regular;
                if (thin is not null)     settings.ThinModel       = thin;
                if (maxTurns.HasValue)    settings.MaxTurns        = maxTurns.Value;
                if (maxTokens.HasValue)   settings.MaxOutputTokens = maxTokens.Value;
                if (timeout.HasValue)     settings.TimeoutMinutes  = timeout.Value;
                if (ethos is not null)    settings.UserEthos       = ethos.Length == 0 ? null : ethos;

                store.Save(settings);
                Console.WriteLine("Settings saved to database.");
                Console.WriteLine();
            }

            Console.WriteLine("Current settings:");
            Console.WriteLine($"  Orchestrator:      {settings.Orchestrator}");
            Console.WriteLine($"  Endpoint:          {settings.OpenAiEndpoint ?? "(default OpenAI)"}");
            Console.WriteLine($"  Thick model:       {settings.ThickModel ?? "(not set)"}");
            Console.WriteLine($"  Regular model:     {settings.RegularModel ?? "(not set)"}");
            Console.WriteLine($"  Thin model:        {settings.ThinModel ?? "(not set)"}");
            Console.WriteLine($"  Max turns:         {settings.MaxTurns}");
            Console.WriteLine($"  Max output tokens: {settings.MaxOutputTokens} (0 = auto)");
            Console.WriteLine($"  HTTP timeout:      {settings.TimeoutMinutes} min");
            Console.WriteLine($"  User ethos:        {(string.IsNullOrWhiteSpace(settings.UserEthos) ? "(not set)" : settings.UserEthos.Length > 80 ? settings.UserEthos[..80] + "…" : settings.UserEthos)}");
            Console.WriteLine();
            Console.WriteLine("Note: API keys are never saved. Pass --api-key on each command or set OPENAI_API_KEY.");
            Console.WriteLine("Settings are shared with the Blazor UI (stored in DB/opentransmute.db).");

            return 0;
        });

        return cmd;
    }
}
