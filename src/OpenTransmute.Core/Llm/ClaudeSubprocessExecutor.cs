using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using OpenTransmute.Models;

namespace OpenTransmute.Llm;

/// <summary>
/// Executes LLM calls by invoking the claude CLI as a subprocess.
/// Supports the standard Anthropic API, external endpoints (Ollama, LM Studio, vLLM),
/// and Azure AI Foundry via environment variable configuration.
///
/// The prompt is delivered via stdin. Each stdout line is yielded as an <see cref="LlmLine"/>
/// event in real time. A single <see cref="LlmCompleted"/> or <see cref="LlmFailed"/> event
/// is emitted as the final event. Never throws on LLM failure —
/// <see cref="OperationCanceledException"/> is rethrown unchanged.
/// </summary>
public sealed class ClaudeSubprocessExecutor(ILogger<ClaudeSubprocessExecutor> logger) : ILlmExecutor
{
    #region Members

    // Appended to the system prompt when the model must write output to stdout only.
    // Suppressed when EnableFileTools is true — in that mode the model uses file tools directly.
    private const string StdoutInstruction =
        "\n\nOutput your complete response as plain text to stdout. " +
        "Do NOT use file-write or file-edit tools — the orchestrator captures your stdout and handles all file saving.";

    #endregion

    #region Properties

    public OrchestratorType BackendType => OrchestratorType.ClaudeCode;

    #endregion

    #region Methods

    /// <inheritdoc/>
    public async IAsyncEnumerable<LlmOutputEvent> ExecuteAsync(
        LlmExecutionContext ctx,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ProcessStartInfo psi = BuildProcessStartInfo(ctx);
        using Process process = new Process { StartInfo = psi };
        StringBuilder errorsBuilder = new StringBuilder();
        StringBuilder outputBuilder = new StringBuilder();

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) errorsBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();

        // Deliver prompt via stdin — more robust than a CLI argument for large prompts.
        await process.StandardInput.WriteAsync(ctx.UserPrompt);
        process.StandardInput.Close();

        while (await process.StandardOutput.ReadLineAsync(ct) is string line)
        {
            outputBuilder.AppendLine(line);
            yield return new LlmLine(line);
        }

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            string err = errorsBuilder.ToString().Trim();
            logger.LogError("claude exited {Code}: {Err}", process.ExitCode, err);

            // Throw so JobOrchestrator.RunLlmCallWithRetryAsync can apply backoff retry,
            // matching the behaviour of OpenAiChatExecutor on rate-limit responses.
            if (err.Contains("rate limit", StringComparison.OrdinalIgnoreCase) || err.Contains("429"))
                throw new LlmRateLimitException();

            yield return new LlmFailed($"claude exited {process.ExitCode}: {(err.Length > 0 ? err : "no stderr")}");
            yield break;
        }

        yield return new LlmCompleted(outputBuilder.ToString().TrimEnd(), TokenUsage.Zero);
    }

    #endregion

    #region Initialize

    private static ProcessStartInfo BuildProcessStartInfo(LlmExecutionContext ctx)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName               = "claude",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            RedirectStandardInput  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            WorkingDirectory       = ctx.WorkingDirectory ?? Directory.GetCurrentDirectory(),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };

        // Append stdout-only instruction unless the model is writing files directly.
        string systemPrompt = (ctx.SystemPrompt ?? string.Empty) +
            (ctx.EnableFileTools ? string.Empty : StdoutInstruction);

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            psi.ArgumentList.Add("--system-prompt");
            psi.ArgumentList.Add(systemPrompt);
        }

        psi.ArgumentList.Add("--dangerously-skip-permissions");
        psi.ArgumentList.Add("--disable-slash-commands");
        psi.ArgumentList.Add("--max-turns");
        psi.ArgumentList.Add(ctx.MaxTurns.ToString());

        if (ctx.Model is not null)
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(ctx.Model);
        }

        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("text");

        // Whitelist the tools the model may use.
        // Decompose: read-only file access so the model can browse the source repository.
        // Implement: file manipulation (read + write) to produce code output.
        // Compose and other stdout-only phases: no tools.
        // Bash and network tools are never permitted regardless of mode.
        psi.ArgumentList.Add("--allowedTools");
        psi.ArgumentList.Add(ctx.EnableFileTools
            ? "Read,Write,Edit,MultiEdit,Glob,Grep,LS"
            : ctx.EnableReadOnlyFileTools
                ? "Read,Glob,Grep,LS"
                : string.Empty);

        // -p without a following argument tells the CLI to read the prompt from stdin.
        psi.ArgumentList.Add("-p");

        return psi;
    }

    #endregion
}
