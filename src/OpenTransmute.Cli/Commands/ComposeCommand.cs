using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTransmute.Data;
using OpenTransmute.Jobs;
using OpenTransmute.Models;
using OpenTransmute.Parsing;

namespace OpenTransmute.Cli.Commands;

/// <summary>
/// <c>compose</c> command — builds a new system design from selected inventory items, driving a
/// <see cref="OpenTransmute.Jobs.ComposeJob"/> through the orchestrator to completion.
/// </summary>
internal static class ComposeCommand
{
    /// <summary>Builds the <c>compose</c> command, wiring its options and run action.</summary>
    internal static Command Build(IServiceProvider sp, CliSettings settings)
    {
        var cmd = new Command("compose", "Compose a new system design from inventory items");

        var outputOpt    = new Option<string>("--output", "-o") { Description = "Output name (required)" };
        var itemsOpt     = new Option<string?>("--items", "-i") { Description = "Comma-separated item IDs (GUID) or exact names" };
        var categoryOpt  = new Option<InventoryCategory?>("--category", "-c") { Description = "Include all items in this category" };
        var projectOpt   = new Option<string?>("--project", "-p") { Description = "Include all items from this project name" };
        var descOpt      = new Option<string?>("--description") { Description = "What should the target system do?" };
        var envOpt       = new Option<string?>("--environment") { Description = "Where does it run? (deployment environment)" };
        var techOpt      = new Option<string?>("--technology") { Description = "What tech stack / language / framework?" };
        var orchOpt      = new Option<OrchestratorType?>("--orchestrator") { Description = "Override engine (ClaudeCode | OpenAI | Ollama)" };
        var apiKeyOpt    = new Option<string?>("--api-key") { Description = "OpenAI API key (overrides OPENAI_API_KEY env var)" };
        var endpointOpt  = new Option<string?>("--endpoint") { Description = "Custom OpenAI-compatible endpoint" };
        var modelOpt     = new Option<string?>("--model") { Description = "Model override" };
        var maxTokensOpt = new Option<int?>("--max-tokens") { Description = "Max output tokens" };
        var timeoutOpt   = new Option<int?>("--timeout") { Description = "HTTP timeout in minutes" };
        var hintsOpt     = new Option<string?>("--hints") { Description = "Coding style / standards injected as a top-level instruction (overrides saved user hints)" };

        cmd.Options.Add(outputOpt);
        cmd.Options.Add(itemsOpt);
        cmd.Options.Add(categoryOpt);
        cmd.Options.Add(projectOpt);
        cmd.Options.Add(descOpt);
        cmd.Options.Add(envOpt);
        cmd.Options.Add(techOpt);
        cmd.Options.Add(orchOpt);
        cmd.Options.Add(apiKeyOpt);
        cmd.Options.Add(endpointOpt);
        cmd.Options.Add(modelOpt);
        cmd.Options.Add(maxTokensOpt);
        cmd.Options.Add(timeoutOpt);
        cmd.Options.Add(hintsOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            string? outputName = parseResult.GetValue(outputOpt);
            if (string.IsNullOrWhiteSpace(outputName))
            {
                Console.Error.WriteLine("Error: --output is required.");
                return 1;
            }
            string? itemsRaw = parseResult.GetValue(itemsOpt);
            InventoryCategory? category = parseResult.GetValue(categoryOpt);
            string? projectName = parseResult.GetValue(projectOpt);
            string? description = parseResult.GetValue(descOpt);
            string? environment = parseResult.GetValue(envOpt);
            string? technology = parseResult.GetValue(techOpt);
            OrchestratorType? orch = parseResult.GetValue(orchOpt);
            string? apiKey = parseResult.GetValue(apiKeyOpt)
                              ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            string? endpoint = parseResult.GetValue(endpointOpt);
            string? model = parseResult.GetValue(modelOpt);
            int? maxTokens = parseResult.GetValue(maxTokensOpt);
            int? timeout = parseResult.GetValue(timeoutOpt);
            string? hints = parseResult.GetValue(hintsOpt) ?? settings.UserEthos;

            OrchestratorType orchestratorType = orch ?? settings.Orchestrator;

            if (!JobConsole.ValidateOpenAiKey(orchestratorType, apiKey))
                return 1;

            // Resolve inventory items from DB
            var dbFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            List<InventoryItem> allItems = await db.InventoryItems.Include(i => i.Project).ToListAsync(ct);

            var selected = new List<InventoryItem>();

            if (!string.IsNullOrWhiteSpace(itemsRaw))
            {
                foreach (var token in itemsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    InventoryItem? item = Guid.TryParse(token, out var id)
                        ? allItems.FirstOrDefault(i => i.Id == id)
                        : allItems.FirstOrDefault(i => i.Name.Equals(token, StringComparison.OrdinalIgnoreCase));

                    if (item is null)
                    {
                        Console.Error.WriteLine($"Item not found: '{token}'");
                        return 1;
                    }

                    if (!selected.Any(s => s.Id == item.Id))
                        selected.Add(item);
                }
            }

            if (category.HasValue)
            {
                foreach (var item in allItems.Where(i => i.Category == category.Value))
                    if (!selected.Any(s => s.Id == item.Id)) selected.Add(item);
            }

            if (!string.IsNullOrWhiteSpace(projectName))
            {
                foreach (var item in allItems.Where(i => i.Project.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase)))
                    if (!selected.Any(s => s.Id == item.Id)) selected.Add(item);
            }

            if (!selected.Any())
            {
                Console.Error.WriteLine("No items selected. Use --items, --category, or --project to specify items.");
                return 1;
            }

            Console.WriteLine($"Selected {selected.Count} item(s): {string.Join(", ", selected.Take(5).Select(i => i.Name))}{(selected.Count > 5 ? "…" : "")}");

            var promptBuilder = sp.GetRequiredService<PromptBuilder>();
            var blocks = selected.Select(i => $"### {i.Category}: {i.Name}\n\n{i.RawMarkdown}");
            string assembledPrompt = promptBuilder.BuildComposePrompt(blocks, description, environment, technology);

            var options = new ComposeOptions
            {
                OutputName        = outputName,
                SelectedItemIds   = selected.Select(i => i.Id).ToList(),
                TargetDescription = description,
                TargetEnvironment = environment,
                TargetTechnology  = technology,
                Orchestrator      = orchestratorType,
                Model             = model ?? settings.RegularModel,
                OpenAiApiKey      = apiKey,
                OpenAiEndpoint    = endpoint ?? settings.OpenAiEndpoint,
                OutputRoot        = Directory.GetCurrentDirectory(),
                MaxOutputTokens   = maxTokens ?? (settings.MaxOutputTokens > 0 ? settings.MaxOutputTokens : 8192),
                TimeoutMinutes    = timeout ?? settings.TimeoutMinutes,
                Hints             = hints,
            };

            var jobStore = sp.GetRequiredService<JobStore>();
            var jobQueue = sp.GetRequiredService<JobQueue>();

            var job = new ComposeJob { Options = options, AssembledPrompt = assembledPrompt };
            jobStore.Add(job);

            Console.WriteLine($"Running compose: {outputName}");
            Console.WriteLine($"Engine: {orchestratorType}");
            Console.WriteLine();

            await JobConsole.RunToCompletionAsync(job, t => jobQueue.EnqueueAsync(job, t), ct);
            return JobConsole.WriteVerdict(job);
        });

        return cmd;
    }
}
