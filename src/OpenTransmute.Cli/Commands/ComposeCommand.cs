using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTransmute.Data;
using OpenTransmute.Jobs;
using OpenTransmute.Models;
using OpenTransmute.Orchestrator.Contracts;
using OpenTransmute.Orchestrator.Parsing;

namespace OpenTransmute.Cli.Commands;

internal static class ComposeCommand
{
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
        var ethosOpt     = new Option<string?>("--ethos") { Description = "Coding style / standards injected as a top-level instruction (overrides saved user-ethos)" };

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
        cmd.Options.Add(ethosOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var outputName  = parseResult.GetValue(outputOpt);
            if (string.IsNullOrWhiteSpace(outputName))
            {
                Console.Error.WriteLine("Error: --output is required.");
                return 1;
            }
            var itemsRaw    = parseResult.GetValue(itemsOpt);
            var category    = parseResult.GetValue(categoryOpt);
            var projectName = parseResult.GetValue(projectOpt);
            var description = parseResult.GetValue(descOpt);
            var environment = parseResult.GetValue(envOpt);
            var technology  = parseResult.GetValue(techOpt);
            var orch        = parseResult.GetValue(orchOpt);
            var apiKey      = parseResult.GetValue(apiKeyOpt)
                              ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var endpoint    = parseResult.GetValue(endpointOpt);
            var model       = parseResult.GetValue(modelOpt);
            var maxTokens   = parseResult.GetValue(maxTokensOpt);
            var timeout     = parseResult.GetValue(timeoutOpt);
            var ethos       = parseResult.GetValue(ethosOpt) ?? settings.UserEthos;

            var orchestratorType = orch ?? settings.Orchestrator;

            if (orchestratorType == OrchestratorType.OpenAI && string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine("Error: OpenAI API key is required. Pass --api-key or set OPENAI_API_KEY.");
                return 1;
            }

            // Resolve inventory items from DB
            var dbFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var allItems = await db.InventoryItems.Include(i => i.Project).ToListAsync(ct);

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
                UserEthos         = ethos,
            };

            var jobStore = sp.GetRequiredService<JobStore>();
            var jobQueue = sp.GetRequiredService<JobQueue>();

            var job = new ComposeJob { Options = options, AssembledPrompt = assembledPrompt };
            jobStore.Add(job);

            Console.WriteLine($"Running compose: {outputName}");
            Console.WriteLine($"Engine: {orchestratorType}");
            Console.WriteLine();

            int logCursor = 0;
            var done = new TaskCompletionSource();

            job.OnChanged += () =>
            {
                while (logCursor < job.LogLines.Count)
                    Console.WriteLine(job.LogLines[logCursor++]);
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
