using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenTransmute.Jobs;
using OpenTransmute.Llm;
using OpenTransmute.Models;
using OpenTransmute.Orchestrator.Contracts;
using OpenTransmute.Orchestrator.Parsing;
using OpenTransmute.Orchestrator.Plugins;
using OpenTransmute.Retry;

namespace OpenTransmute.Phases;

/// <summary>
/// Takes a spec document (the output of a Compose run) and drives the selected
/// LLM backend to produce a complete implementation — actually writing files to disk.
///
/// Claude Code:   uses the claude CLI's native file tools (Read/Write/Edit/Glob etc.)
///                with the output directory as the working directory.
///
/// OpenAI/Ollama: uses an agentic tool-call loop (Microsoft.Extensions.AI) with a
///                FileSystemPlugin rooted at the output directory, so the model calls
///                WriteFile/ReadFile/ListFiles to build the implementation.
/// </summary>
public class ImplementOrchestrator(
    ClaudeAgentBackend claudeBackend,
    PromptTemplates promptTemplates,
    RetryPolicy retryPolicy,
    ILogger<ImplementOrchestrator> logger)
{
    #region Methods

    public async Task RunAsync(ImplementJob job, CancellationToken ct)
    {
        ImplementOptions options = job.Options;

        job.AppendLog($"Engine:  {options.Orchestrator}" +
            (options.Model is not null ? $" | Model: {options.Model}" : " | Default model"));
        job.AppendLog($"Output:  {options.OutputDirectory}");

        Directory.CreateDirectory(options.OutputDirectory);

        try
        {
            switch (options.Orchestrator)
            {
                case OrchestratorType.ClaudeCode:
                    await RunWithClaudeAsync(job, options, ct);
                    break;

                case OrchestratorType.OpenAI:
                case OrchestratorType.Ollama:
                    await RunWithOpenAiAgentAsync(job, options, ct);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported orchestrator: {options.Orchestrator}");
            }
        }
        catch (Exception ex) when (job.Status != JobStatus.Failed)
        {
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.AppendLog($"Implement FAILED: {ex.Message}");
            logger.LogError(ex, "Implement job {Id} failed", job.Id);
            return;
        }

        if (job.Status != JobStatus.Failed)
        {
            job.AppendLog("Implement complete.");
            job.Status = JobStatus.Completed;
        }
    }

    // ── Plan writer ───────────────────────────────────────────────────────────

    /// <summary>
    /// Assembles the complete implementation plan text (guards + spec + instructions)
    /// and writes it to compose-plan.md in the root of the output directory.
    /// Returns the assembled plan string so it can be used directly as the prompt.
    /// </summary>
    private async Task<string> WritePlanAsync(ImplementOptions options, CancellationToken ct)
    {
        string projectName = string.IsNullOrWhiteSpace(options.ProjectName)
            ? Path.GetFileName(options.OutputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : options.ProjectName;

        string outDir = options.OutputDirectory.Replace("\\", "/");

        string plan =
            $"""
            # Implementation Plan — {projectName}

            **Project name:** {projectName}
            **Output directory:** {outDir}

            ---

            ## Mandatory Guards

            {promptTemplates.TransmuteGuards}

            ---

            ## Implementation Specification

            {options.SpecContent}

            ---

            ## Instructions

            You are implementing the project "{projectName}".
            Name the project, its root namespace, package, module, or equivalent top-level identifier exactly "{projectName}" throughout.

            Write all source code and configuration files to: {outDir}

            - Apply every guard above without exception before writing any code.
            - Read the full specification before writing any file.
            - Create a complete, working implementation — not a skeleton or stub.
            - Implement every module, component, data model, and algorithm described.
            - Organise files logically for the target tech stack (e.g. src/, tests/, docs/).
            - When finished, write a brief DONE.md summarising what was implemented.
            """;

        string planPath = Path.Combine(options.OutputDirectory, "compose-plan.md");
        await File.WriteAllTextAsync(planPath, plan, ct);
        return plan;
    }

    // ── Claude Code ───────────────────────────────────────────────────────────

    private async Task RunWithClaudeAsync(ImplementJob job, ImplementOptions options, CancellationToken ct)
    {
        string specPath   = Path.Combine(options.OutputDirectory, "_spec.md");
        string guardsPath = Path.Combine(options.OutputDirectory, "_guards.md");
        await File.WriteAllTextAsync(specPath,   options.SpecContent,              ct);
        await File.WriteAllTextAsync(guardsPath, promptTemplates.TransmuteGuards,  ct);

        // Write compose-plan.md (full assembled plan for reference)
        await WritePlanAsync(options, ct);

        job.AppendLog($"Spec written to:        {specPath}");
        job.AppendLog($"Guards written to:      {guardsPath}");
        job.AppendLog($"Plan written to:        {Path.Combine(options.OutputDirectory, "compose-plan.md")}");
        job.AppendLog($"Max turns: {options.MaxTurns}");

        string projectName = string.IsNullOrWhiteSpace(options.ProjectName)
            ? Path.GetFileName(options.OutputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : options.ProjectName;

        string prompt =
            $"""
            Read _guards.md first. It contains mandatory rules that govern everything you write — apply them without exception throughout this task.

            Then read _spec.md. It contains the complete implementation specification.

            You are implementing the project "{projectName}".
            Name the project, its root namespace, package, module, or equivalent top-level identifier exactly "{projectName}" throughout.

            Write all source code and configuration files to: {options.OutputDirectory.Replace("\\", "/")}

            - Read _guards.md and _spec.md before writing any code.
            - Create a complete, working implementation — not a skeleton or stub.
            - Implement every module, component, data model, and algorithm described.
            - Organise files logically for the target tech stack (e.g. src/, tests/, docs/).
            - When finished, write a brief DONE.md summarising what was implemented.
            """;

        LlmRequest request = new LlmRequest(
            prompt: prompt,
            workingDirectory: options.OutputDirectory,
            allowFileWrite: true,
            model: options.Model,
            maxTurns: options.MaxTurns,
            onLogLine: line => job.AppendLog(line));

        await retryPolicy.ExecuteAsync(
            innerCt => claudeBackend.CompleteAsync(request, innerCt), ct);
    }

    // ── OpenAI / Ollama agentic loop ─────────────────────────────────────────

    private async Task RunWithOpenAiAgentAsync(ImplementJob job, ImplementOptions options, CancellationToken ct)
    {
        bool isOllama = options.Orchestrator == OrchestratorType.Ollama;
        TimeSpan timeout = TimeSpan.FromMinutes(options.TimeoutMinutes);

        OpenAIClientOptions clientOptions = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient { Timeout = timeout }),
            NetworkTimeout = timeout,
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        };

        OpenAIClient openAiClient;
        string model;

        if (isOllama)
        {
            clientOptions.Endpoint = new Uri("http://localhost:11434/v1");
            openAiClient = new OpenAIClient(new ApiKeyCredential("ollama"), clientOptions);
            model = options.Model ?? "llama3.1";
        }
        else if (!string.IsNullOrWhiteSpace(options.OpenAiEndpoint))
        {
            clientOptions.Endpoint = new Uri(options.OpenAiEndpoint);
            openAiClient = new OpenAIClient(new ApiKeyCredential(options.OpenAiApiKey!), clientOptions);
            model = options.Model ?? "gpt-4o";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.OpenAiApiKey))
                throw new InvalidOperationException("OpenAI API key is required.");
            openAiClient = new OpenAIClient(new ApiKeyCredential(options.OpenAiApiKey!), clientOptions);
            model = options.Model ?? "gpt-4o";
        }

        // Build and write the full plan; use it as the user message
        string plan = await WritePlanAsync(options, ct);
        job.AppendLog($"Plan written to: {Path.Combine(options.OutputDirectory, "compose-plan.md")}");

        // FileSystemPlugin rooted at the output directory
        FileSystemPlugin fs = new FileSystemPlugin(options.OutputDirectory, promptTemplates.TransmuteIgnore);
        IList<AITool> tools =
        [
            AIFunctionFactory.Create(fs.ListFiles),
            AIFunctionFactory.Create(fs.ReadFile),
            AIFunctionFactory.Create(fs.WriteFile),
        ];

        IChatClient client = openAiClient.GetChatClient(model)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        string projectName = string.IsNullOrWhiteSpace(options.ProjectName)
            ? Path.GetFileName(options.OutputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : options.ProjectName;

        string systemPrompt =
            $"""
            You are an expert software engineer implementing the project "{projectName}".
            You have file-system tools to read and write files in the output directory.
            Use WriteFile to create each source file. Use ListFiles and ReadFile to review what you have written.
            The output directory is: {options.OutputDirectory.Replace("\\", "/")}
            All relative paths you pass to the tools are relative to that directory.
            Name the project, its root namespace, package, module, or equivalent top-level identifier exactly "{projectName}" throughout.
            Be thorough: implement every module, class, function, and configuration described in the spec.
            When finished, write a brief DONE.md summarising what was implemented.
            """;

        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, plan)
        ];

        ChatOptions chatOptions = new ChatOptions
        {
            Tools = [.. tools],
            ToolMode = ChatToolMode.Auto,
            ModelId = model
        };

        job.AppendLog($"Starting agentic implementation loop (model: {model})…");

        ChatResponse result = await client.GetResponseAsync(messages, chatOptions, ct);

        string summary = result.Text ?? "(no summary)";
        job.AppendLog($"Model response: {summary[..Math.Min(300, summary.Length)]}");

        string[] written = Directory.GetFiles(options.OutputDirectory, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith("_") && Path.GetFileName(f) != "compose-plan.md")
            .Select(f => Path.GetRelativePath(options.OutputDirectory, f))
            .ToArray();

        job.AppendLog($"Files written ({written.Length}):");
        foreach (string f in written)
            job.AppendLog($"  {f}");
    }

    #endregion
}
