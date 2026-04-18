using Microsoft.EntityFrameworkCore;
using OpenTransmute.Data;
using OpenTransmute.Jobs;
using OpenTransmute.Llm;
using OpenTransmute.Models;
using OpenTransmute.Orchestrator.Contracts;
using OpenTransmute.Orchestrator.Parsing;
using OpenTransmute.Retry;

namespace OpenTransmute.Phases;

/// <summary>
/// Runs a compose job by assembling an LLM prompt from selected inventory items
/// and dispatching to the appropriate backend (Claude CLI, OpenAI, or Ollama).
/// </summary>
public class ComposeOrchestrator(
    IDbContextFactory<AppDbContext> dbFactory,
    PromptBuilder promptBuilder,
    ClaudeAgentBackend claudeBackend,
    OpenAiCompletionBackend openAiBackend,
    RetryPolicy retryPolicy,
    ILogger<ComposeOrchestrator> logger)
{
    #region Methods

    /// <summary>
    /// Executes the compose run for the given job, writing output to disk on success.
    /// Updates job state throughout; never throws — failures are recorded on the job.
    /// </summary>
    /// <param name="job">The compose job carrying options and receiving status updates.</param>
    /// <param name="ct">Cancellation token propagated from the host.</param>
    public async Task RunAsync(ComposeJob job, CancellationToken ct)
    {
        ComposeOptions options = job.Options;

        string prompt;

        if (!string.IsNullOrWhiteSpace(options.PrebuiltPrompt))
        {
            prompt = options.PrebuiltPrompt;
            // Prepend user ethos when the prompt was pre-assembled (Transmute path bypasses the template).
            string ethosSection = PromptBuilder.FormatUserEthos(options.UserEthos);
            if (!string.IsNullOrEmpty(ethosSection))
                prompt = ethosSection + "\n\n---\n\n" + prompt;
            job.AssembledPrompt = prompt;
            job.AppendLog($"Engine: {options.Orchestrator}" +
                (options.Model is not null ? $" | Model: {options.Model}" : string.Empty));
            job.AppendLog($"Prompt length: {prompt.Length:N0} chars");
        }
        else
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            List<Models.InventoryItem> items = await db.InventoryItems
                .Where(i => options.SelectedItemIds.Contains(i.Id))
                .ToListAsync(ct);

            if (items.Count == 0)
            {
                job.ErrorMessage = "No inventory items found for the selected IDs.";
                job.Status = JobStatus.Failed;
                return;
            }

            job.AppendLog($"Composing from {items.Count} inventory items.");
            job.AppendLog($"Engine: {options.Orchestrator}" +
                (options.Model is not null ? $" | Model: {options.Model}" : string.Empty));

            IEnumerable<string> markdownBlocks = items.Select(i =>
            {
                string securityTag = i.SecurityScore >= 7
                    ? $" ⚠ SECURITY:{i.SecurityScore}/10"
                    : i.SecurityScore >= 4
                        ? $" [Security:{i.SecurityScore}/10]"
                        : string.Empty;
                return $"### {i.Category}: {i.Name}{securityTag}\n\n{i.RawMarkdown}";
            });
            prompt = promptBuilder.BuildComposePrompt(
                markdownBlocks,
                options.TargetDescription,
                options.TargetEnvironment,
                options.TargetTechnology,
                options.UserEthos);
            job.AssembledPrompt = prompt;
        }

        ILlmBackend backend;
        LlmRequest request;

        switch (options.Orchestrator)
        {
            case OrchestratorType.OpenAI:
                backend = openAiBackend;
                request = new LlmRequest(
                    prompt: prompt,
                    maxOutputTokens: options.MaxOutputTokens,
                    model: options.Model,
                    openAiApiKey: options.OpenAiApiKey,
                    openAiBaseUrl: options.OpenAiEndpoint,
                    httpTimeout: TimeSpan.FromMinutes(options.TimeoutMinutes));
                break;

            case OrchestratorType.Ollama:
                backend = claudeBackend;
                request = new LlmRequest(
                    prompt: prompt,
                    maxOutputTokens: options.MaxOutputTokens,
                    model: options.Model,
                    useExternal: true,
                    externalUser: "ollama",
                    externalEndpoint: "http://localhost:11434",
                    httpTimeout: TimeSpan.FromMinutes(options.TimeoutMinutes));
                break;

            default: // ClaudeCode
                backend = claudeBackend;
                request = new LlmRequest(
                    prompt: prompt,
                    maxOutputTokens: options.MaxOutputTokens,
                    model: options.Model);
                break;
        }

        try
        {
            string output = await retryPolicy.ExecuteAsync(
                innerCt => backend.CompleteAsync(request, innerCt), ct);

            job.Output = output;
            job.AppendLog("Compose complete.");

            string outputDir  = Path.Combine(options.OutputRoot, "Output", "Composition", options.OutputName);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "compose-output.md");
            await File.WriteAllTextAsync(outputPath, output, ct);
            job.AppendLog($"Output saved to: {outputPath}");
            job.Status = JobStatus.Completed;
        }
        catch (Exception ex)
        {
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.AppendLog($"Compose FAILED: {ex.Message}");
            logger.LogError(ex, "Compose job {Id} failed", job.Id);
        }
    }

    #endregion
}
