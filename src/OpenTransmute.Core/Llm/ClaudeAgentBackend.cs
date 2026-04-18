using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OpenTransmute.Llm;

/// <summary>
/// Invokes the claude CLI as a subprocess in print mode (-p).
/// The agent uses its own Read/Grep/Glob tools to explore the working directory.
/// The prompt is delivered as a CLI argument to avoid stdin/process complications.
///
/// External endpoints (e.g. Ollama) are supported by setting
/// ANTHROPIC_AUTH_TOKEN and ANTHROPIC_BASE_URL before launching the process.
/// </summary>
public class ClaudeAgentBackend(ILogger<ClaudeAgentBackend> logger) : ILlmBackend
{
    #region Members

    // Appended to whatever system prompt the caller supplies.
    // Instructs the agent to write its answer to stdout rather than saving to disk.
    private const string OutputInstruction =
        "\n\nOutput your complete response as plain text to stdout. " +
        "Do NOT use file-write or file-edit tools — the orchestrator captures your stdout and handles all file saving.";

    #endregion

    #region Methods

    /// <summary>
    /// Launches the claude CLI as a subprocess and returns its complete stdout output.
    /// Throws <see cref="LlmRateLimitException"/> on rate-limit errors and
    /// <see cref="LlmException"/> on any other non-zero exit code.
    /// </summary>
    /// <param name="request">The LLM request carrying the prompt and all backend configuration.</param>
    /// <param name="ct">Cancellation token propagated from the caller.</param>
    /// <returns>The full stdout output from the claude CLI, trimmed of trailing whitespace.</returns>
    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "claude",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = request.WorkingDirectory ?? Directory.GetCurrentDirectory(),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // External endpoint (e.g. Ollama, LM Studio, vLLM)
        if (request.UseExternal && request.ExternalEndpoint is not null)
        {
            psi.EnvironmentVariables["ANTHROPIC_AUTH_TOKEN"] = request.ExternalUser;
            psi.EnvironmentVariables["ANTHROPIC_BASE_URL"] = request.ExternalEndpoint;
            psi.EnvironmentVariables["ANTHROPIC_API_KEY"] = request.ExternalUser;
        }

        // Azure AI Foundry — uses a separate set of env vars from standard Anthropic
        if (request.UseAzureFoundry)
        {
            psi.EnvironmentVariables["CLAUDE_CODE_USE_FOUNDRY"] = "1";
            psi.EnvironmentVariables["ANTHROPIC_FOUNDRY_API_KEY"] = request.AzureApiKey ?? string.Empty;
            psi.EnvironmentVariables["ANTHROPIC_FOUNDRY_BASE_URL"] = request.AzureEndpoint ?? string.Empty;

            // Pin all model slots to the caller-specified deployment name so the CLI
            // doesn't try to resolve an Anthropic model name against the Foundry endpoint.
            if (request.Model is not null)
            {
                psi.EnvironmentVariables["ANTHROPIC_DEFAULT_OPUS_MODEL"] = request.Model;
                psi.EnvironmentVariables["ANTHROPIC_DEFAULT_SONNET_MODEL"] = request.Model;
                psi.EnvironmentVariables["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = request.Model;
            }
        }

        // When AllowFileWrite is set, Claude should use its file tools to write output directly.
        // In that mode we do NOT append the stdout-only instruction.
        string systemAppend = request.AllowFileWrite
            ? (request.SystemPrompt ?? string.Empty)
            : (request.SystemPrompt ?? string.Empty) + OutputInstruction;

        if (!string.IsNullOrEmpty(systemAppend))
        {
            psi.ArgumentList.Add("--system-prompt");
            // ArgumentList handles quoting/escaping — do not wrap manually.
            psi.ArgumentList.Add(systemAppend);
        }

        // Skip permission prompts — this is a non-interactive scripted call.
        psi.ArgumentList.Add("--dangerously-skip-permissions");
        //psi.ArgumentList.Add("--bare");
        psi.ArgumentList.Add("--disable-slash-commands");

        // Cap tool-use rounds so the agent doesn't explore indefinitely on large codebases.
        psi.ArgumentList.Add("--max-turns");
        psi.ArgumentList.Add(request.MaxTurns.ToString());

        if (request.Model is not null)
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(request.Model);
        }

        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("text");

        // Print mode — prompt arrives as CLI argument. ArgumentList handles escaping.
        psi.ArgumentList.Add("-p");
        //psi.ArgumentList.Add(request.Prompt);

        using Process process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var errors = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            output.AppendLine(e.Data);
            request.OnLogLine?.Invoke(e.Data);
            logger.LogDebug("[claude] {Line}", e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                errors.AppendLine(e.Data);
        };

        process.Start();

        await process.StandardInput.WriteAsync(request.Prompt);
        process.StandardInput.Close();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            string err = errors.ToString();

            if (err.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                err.Contains("429"))
                throw new LlmRateLimitException();

            throw new LlmException(
                process.ExitCode,
                err.Length > 0 ? err : "claude CLI exited with code " + process.ExitCode);
        }

        return output.ToString().TrimEnd();
    }

    #endregion
}
