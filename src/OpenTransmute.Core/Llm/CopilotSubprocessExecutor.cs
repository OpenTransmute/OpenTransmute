using System.Runtime.CompilerServices;
using System.Threading.Channels;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using Rpc = GitHub.Copilot.SDK.Rpc;
using OpenTransmute.Models;

namespace OpenTransmute.Llm;

/// <summary>
/// Executes LLM calls via the GitHub Copilot SDK (<c>GitHub.Copilot.SDK</c>).
/// The SDK manages the Copilot CLI process lifecycle, authentication (GitHub OAuth or
/// <c>GH_TOKEN</c> / <c>GITHUB_TOKEN</c> env var), and JSON-RPC communication.
///
/// Each call creates a fresh <see cref="CopilotClient"/> scoped to the call's working
/// directory, then a streaming session within it. Assistant message deltas are yielded as
/// <see cref="LlmLine"/> events. A single <see cref="LlmCompleted"/> or <see cref="LlmFailed"/>
/// terminal event is emitted at the end.
/// Never throws on LLM failure — <see cref="OperationCanceledException"/> is rethrown unchanged.
/// </summary>
public sealed class CopilotSubprocessExecutor : ILlmExecutor
{
    #region Members

    private readonly ILogger<CopilotSubprocessExecutor> _logger;

    // Appended to the user prompt when the model must write output to stdout only.
    // Suppressed when the model has a direct file-write target (OutputFilePath) or Implement mode.
    private const string StdoutInstruction =
        "\n\nOutput your complete response as plain text to stdout. " +
        "Do NOT use file-write or file-edit tools — the orchestrator captures your stdout and handles all file saving.";

    // When the model has read-only file tools AND must produce text to stdout, it tends to
    // interleave file reads with partial text output, then restart from scratch each turn.
    // This variant forces a strict read-then-write ordering.
    private const string StdoutWithToolsInstruction =
        "\n\nIMPORTANT WORKFLOW RULES:\n" +
        "1. Complete ALL file reading FIRST. Do not output any text until you have read every file you need.\n" +
        "2. Once you have gathered all information, output your COMPLETE response as plain text to stdout " +
        "in a single final turn. Do NOT split output across multiple turns.\n" +
        "3. Do NOT use file-write or file-edit tools — the orchestrator captures your stdout and handles all file saving.\n" +
        "4. Do NOT use shell, powershell, or bash tools — they are blocked. " +
        "If a glob result is too large, use narrower glob patterns (e.g. \"src/**/*.java\") " +
        "or use grep to search for specific content instead.";

    // JSON-specific variant — replaces StdoutWithToolsInstruction when the caller
    // sets JsonOutputMode. Explicitly overrides "plain text" with "JSON object."
    private const string JsonWithToolsInstruction =
        "\n\nIMPORTANT WORKFLOW AND OUTPUT RULES:\n" +
        "1. Complete ALL file reading FIRST. Do not output any text until you have read every file you need.\n" +
        "2. Once you have gathered all information, output a SINGLE JSON OBJECT to stdout " +
        "in a single final turn. Do NOT split output across multiple turns.\n" +
        "3. Your ENTIRE stdout output must be valid JSON. Start with { and end with }. " +
        "No markdown. No code fences. No preamble. No explanation. No sign-off. ONLY the JSON object.\n" +
        "4. Do NOT use file-write or file-edit tools — the orchestrator captures your stdout and handles all file saving.\n" +
        "5. Do NOT use shell, powershell, or bash tools — they are blocked. " +
        "If a glob result is too large, use narrower glob patterns or grep instead.";

    #endregion

    #region Constructor

    /// <summary>
    /// Creates the executor. A fresh <see cref="CopilotClient"/> is spawned per LLM call
    /// so that each session gets the correct <c>Cwd</c> for file-tool access.
    /// </summary>
    public CopilotSubprocessExecutor(ILogger<CopilotSubprocessExecutor> logger)
    {
        _logger = logger;
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public OrchestratorType BackendType => OrchestratorType.CopilotCli;

    #endregion

    #region Methods

    /// <inheritdoc/>
    public async IAsyncEnumerable<LlmOutputEvent> ExecuteAsync(
        LlmExecutionContext ctx,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ctx.Timeout = TimeSpan.FromMinutes(120);

        _logger.LogInformation("[COPILOT] ExecuteAsync: Model={Model}, Cwd={Cwd}, Timeout={Timeout}, OutputFilePath={OutputPath}",
            ctx.Model ?? "(default)", ctx.WorkingDirectory ?? "(null)", ctx.Timeout, ctx.OutputFilePath ?? "(null)");

        // Enforce the configured timeout by linking a deadline CTS with the caller's token.
        // When the timeout fires, the linked token cancels — we catch it below and yield
        // LlmFailed instead of propagating OperationCanceledException.
        using CancellationTokenSource timeoutCts = new(ctx.Timeout);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        CancellationToken linked = linkedCts.Token;

        // Channel bridges the SDK event callbacks to the async-enumerable consumer.
        Channel<LlmOutputEvent> channel = Channel.CreateUnbounded<LlmOutputEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        CopilotClient? client = null;
        CopilotSession? session = null;
        IDisposable? subscription = null;
        string? setupFailure = null;

        // AppendResults temp file path — hoisted to outer scope so timeout/exception
        // salvage paths can read the file even when the try block's inner variables are gone.
        string? appendResultsPath = null;

        // Stream log — write deltas to a temp file so we can see exactly what the model produced.
        using StreamDiagnosticLog streamLog = StreamDiagnosticLog.Create(ctx, "copilot", _logger);

        try
        {
            string cwd = ctx.WorkingDirectory ?? Directory.GetCurrentDirectory();
            int idleTimeoutSec = Math.Max(30, (int)ctx.Timeout.TotalSeconds);

            client = new CopilotClient(new CopilotClientOptions
            {
                UseStdio  = true,
                AutoStart = true,
                Cwd       = cwd,
                SessionIdleTimeoutSeconds = idleTimeoutSec,
            });

            await client.StartAsync();

            // AppendResults tool — model writes output incrementally to a temp file.
            // Each tool call atomically persists content to disk, surviving server errors.
            if (ctx.EnableAppendResultsTool)
            {
                appendResultsPath = Path.Combine(cwd, $"_append-results-{DateTime.UtcNow:yyyyMMdd-HHmmss}.md");
                // Ensure the file exists and is empty before the model starts writing.
                File.WriteAllText(appendResultsPath, string.Empty, System.Text.Encoding.UTF8);
            }

            // Shell rejection recovery — when the model tries powershell/bash and gets
            // denied, it often stalls. Track the rejection so SessionIdleEvent can inject
            // a recovery prompt telling the model to use view/glob/grep instead.
            bool shellRejected = false;
            int shellRecoveryAttempts = 0;
            const int MaxShellRecoveryAttempts = 1;

            SessionConfig config = BuildSessionConfig(ctx, () => shellRejected = true);

            // Register the AppendResults tool on the session config BEFORE session creation.
            if (appendResultsPath is not null)
            {
                // Serialize concurrent tool calls to prevent File.AppendAllText interleaving.
                object appendLock = new();

                AIFunction appendTool = AIFunctionFactory.Create(
                    (string content) =>
                    {
                        lock (appendLock)
                        {
                            File.AppendAllText(appendResultsPath, content, System.Text.Encoding.UTF8);
                        }

                        long totalBytes = new FileInfo(appendResultsPath).Length;
                        _logger.LogInformation("[COPILOT] AppendResults: +{Chars} chars ({Total} bytes total)",
                            content.Length, totalBytes);
                        streamLog.WriteLine($"\n  [APPEND RESULTS] +{content.Length} chars ({totalBytes} bytes total)");
                        return $"OK — appended {content.Length} chars ({totalBytes} bytes total in file)";
                    },
                    new AIFunctionFactoryOptions
                    {
                        Name = "AppendResults",
                        Description = "Appends content to the output file. Call this tool incrementally " +
                            "as you produce each section of your output. Each call atomically persists " +
                            "content to disk. Pass the raw Markdown content — no escaping or wrapping needed."
                    });

                config.Tools = new List<AIFunction> { appendTool };
                _logger.LogInformation("[COPILOT] AppendResults tool registered on config. Temp file: {Path}", appendResultsPath);
            }

            session = await client.CreateSessionAsync(config);
            _logger.LogDebug("[COPILOT] Session created: {SessionId}", session.SessionId);

            streamLog.WriteHeader(ctx,
                ("FileToolsRoot", ctx.FileToolsRoot),
                ("AppendResultsPath", appendResultsPath),
                ("IdleTimeoutSec", idleTimeoutSec.ToString()));

            // Track last assistant message content for the final output.
            string? lastMessageContent = null;
            // Accumulate delta text across all turns.
            System.Text.StringBuilder deltaAccumulator = new();
            // Chars received in the CURRENT turn — reset on each TurnStart.
            int turnDeltaLength = 0;

            // Model retry detection — abort the CLI's internal retry (which restarts from
            // scratch) and send a continuation prompt with the accumulated partial output.
            bool modelRetryDetected = false;
            string? partialBeforeRetry = null;
            int modelRetryCount = 0;
            int continuationAttempts = 0;
            const int MaxContinuationAttempts = 2;

            subscription = session.On(evt =>
            {
                switch (evt)
                {
                    case AssistantMessageDeltaEvent delta:
                        string chunk = delta.Data.DeltaContent ?? string.Empty;
                        if (chunk.Length == 0 || modelRetryDetected)
                            break;

                        deltaAccumulator.Append(chunk);
                        turnDeltaLength += chunk.Length;
                        channel.Writer.TryWrite(new LlmLine(chunk));
                        streamLog.Write(chunk);
                        break;

                    case AssistantStreamingDeltaEvent:
                        // Progress-only event (TotalResponseSizeBytes) — no text content.
                        break;

                    case AssistantMessageEvent msg:
                        string? content = msg.Data?.Content;
                        long outputTokens = (long)(msg.Data?.OutputTokens ?? 0);
                        streamLog.WriteLine($"\n\n=== ASSISTANT MESSAGE (content={content?.Length ?? 0} chars, deltas={deltaAccumulator.Length}, outputTokens={outputTokens}) ===");

                        if (!string.IsNullOrWhiteSpace(content))
                            lastMessageContent = content;
                        break;

                    case AssistantReasoningDeltaEvent:
                    case AssistantReasoningEvent:
                        break;

                    case AssistantTurnStartEvent turnStart:
                        streamLog.WriteLine($"\n\n--- TURN {turnStart.Data?.TurnId ?? "?"} (prev turn deltas={turnDeltaLength}) ---");
                        turnDeltaLength = 0;
                        break;

                    case ToolExecutionStartEvent toolStart:
                        string toolName = toolStart.Data?.ToolName ?? "(unknown)";
                        string toolArgs = StreamDiagnosticLog.Truncate(toolStart.Data?.Arguments?.ToString() ?? "", 300);
                        _logger.LogDebug("[COPILOT] ToolStart: {Tool} args={Args}", toolName, toolArgs);
                        streamLog.WriteLine($"\n  [TOOL START] {toolName}: {toolArgs}");
                        break;

                    case ToolExecutionCompleteEvent toolDone:
                        string completeTool = toolDone.Data?.ToolCallId ?? "(unknown)";
                        bool? toolSuccess = toolDone.Data?.Success;
                        string toolError = toolDone.Data?.Error?.Message ?? toolDone.Data?.Error?.Code ?? string.Empty;
                        string toolResult = StreamDiagnosticLog.Truncate(toolDone.Data?.Result?.Content ?? "", 200);
                        _logger.LogDebug("[COPILOT] ToolComplete: id={Id} success={Success}", completeTool, toolSuccess);
                        streamLog.WriteLine(toolSuccess == true
                            ? $"  [TOOL DONE] ok — {toolResult}"
                            : $"  [TOOL DONE] FAILED — {toolError}");
                        break;

                    // ── Usage / token events ─────────────────────────────────────

                    case AssistantUsageEvent usage:
                        var u = usage.Data;
                        streamLog.WriteLine(
                            $"\n  [USAGE] model={u?.Model} in={u?.InputTokens} out={u?.OutputTokens} " +
                            $"cacheR={u?.CacheReadTokens} cacheW={u?.CacheWriteTokens} " +
                            $"cost={u?.Cost} duration={u?.Duration}ms latency={u?.InterTokenLatencyMs}ms");
                        break;

                    case SessionUsageInfoEvent usageInfo:
                        var ui = usageInfo.Data;
                        streamLog.WriteLine(
                            $"\n  [USAGE INFO] tokens={ui?.CurrentTokens}/{ui?.TokenLimit} " +
                            $"system={ui?.SystemTokens} conversation={ui?.ConversationTokens} " +
                            $"toolDefs={ui?.ToolDefinitionsTokens} messages={ui?.MessagesLength} initial={ui?.IsInitial}");
                        break;

                    // ── Session lifecycle ─────────────────────────────────────────

                    case SessionStartEvent sessionStart:
                        var ss = sessionStart.Data;
                        streamLog.WriteLine(
                            $"\n  [SESSION START] id={ss?.SessionId} model={ss?.SelectedModel} " +
                            $"copilot={ss?.CopilotVersion} reasoning={ss?.ReasoningEffort}");
                        break;

                    case SessionShutdownEvent shutdown:
                        var sd = shutdown.Data;
                        streamLog.WriteLine(
                            $"\n  [SESSION SHUTDOWN] type={sd?.ShutdownType} error={sd?.ErrorReason} " +
                            $"model={sd?.CurrentModel} tokens={sd?.CurrentTokens} " +
                            $"system={sd?.SystemTokens} conversation={sd?.ConversationTokens} " +
                            $"toolDefs={sd?.ToolDefinitionsTokens} apiDuration={sd?.TotalApiDurationMs}ms");
                        break;

                    // SessionLifecycleEvent is not a SessionEvent subtype — cannot be matched here.

                    case SessionModelChangeEvent modelChange:
                        var mc = modelChange.Data;
                        streamLog.WriteLine(
                            $"\n  [MODEL CHANGE] {mc?.PreviousModel} → {mc?.NewModel} cause={mc?.Cause} " +
                            $"reasoning={mc?.PreviousReasoningEffort} → {mc?.ReasoningEffort}");
                        break;

                    // ── Truncation / compaction ──────────────────────────────────

                    case SessionTruncationEvent truncation:
                        var tr = truncation.Data;
                        streamLog.WriteLine(
                            $"\n  [TRUNCATION] by={tr?.PerformedBy} limit={tr?.TokenLimit} " +
                            $"pre={tr?.PreTruncationTokensInMessages} post={tr?.PostTruncationTokensInMessages} " +
                            $"removed={tr?.TokensRemovedDuringTruncation} msgs={tr?.MessagesRemovedDuringTruncation} " +
                            $"preMsgs={tr?.PreTruncationMessagesLength} postMsgs={tr?.PostTruncationMessagesLength}");
                        break;

                    case SessionCompactionStartEvent compactStart:
                        var cs = compactStart.Data;
                        streamLog.WriteLine(
                            $"\n  [COMPACTION START] system={cs?.SystemTokens} conversation={cs?.ConversationTokens} " +
                            $"toolDefs={cs?.ToolDefinitionsTokens}");
                        break;

                    case SessionCompactionCompleteEvent compactDone:
                        var cc = compactDone.Data;
                        streamLog.WriteLine(
                            $"\n  [COMPACTION DONE] ok={cc?.Success} pre={cc?.PreCompactionTokens} post={cc?.PostCompactionTokens} " +
                            $"removed={cc?.MessagesRemoved} compactionTokens={cc?.CompactionTokensUsed} " +
                            $"error={cc?.Error}");
                        break;

                    // ── Model call failures ──────────────────────────────────────

                    case ModelCallFailureEvent callFail:
                        var cf = callFail.Data;
                        _logger.LogWarning("[COPILOT] ModelCallFailure: model={Model} status={Status} error={Error}",
                            cf?.Model, cf?.StatusCode, cf?.ErrorMessage);
                        streamLog.WriteLine(
                            $"\n  [MODEL CALL FAILURE] model={cf?.Model} status={cf?.StatusCode} " +
                            $"error={cf?.ErrorMessage} source={cf?.Source} duration={cf?.DurationMs}ms");
                        break;

                    // ── Abort ─────────────────────────────────────────────────────

                    case AbortEvent abort:
                        _logger.LogWarning("[COPILOT] Abort: {Reason}", abort.Data?.Reason);
                        streamLog.WriteLine($"\n  [ABORT] reason={abort.Data?.Reason}");
                        break;

                    // ── Info / warning / model retry ──────────────────────────────

                    case SessionInfoEvent info:
                        streamLog.WriteLine($"\n  [INFO] type={info.Data?.InfoType} msg={info.Data?.Message} tip={info.Data?.Tip}");

                        // Detect model_retry — the CLI is about to retry the request from scratch,
                        // which discards all accumulated output. Abort and send a continuation instead.
                        if (string.Equals(info.Data?.InfoType, "model_retry", StringComparison.OrdinalIgnoreCase))
                        {
                            modelRetryCount++;
                            _logger.LogWarning(
                                "[COPILOT] *** MODEL RETRY #{RetryNum} DETECTED — server error mid-stream. " +
                                "{Accumulated} chars accumulated so far. ***",
                                modelRetryCount, deltaAccumulator.Length);

                            if (!modelRetryDetected && continuationAttempts < MaxContinuationAttempts)
                            {
                                partialBeforeRetry = deltaAccumulator.ToString();
                                modelRetryDetected = true;

                                _logger.LogWarning(
                                    "[COPILOT] *** Aborting CLI retry. Will send continuation prompt " +
                                    "(attempt {Attempt}/{Max}, {Chars} chars to preserve). ***",
                                    continuationAttempts + 1, MaxContinuationAttempts, partialBeforeRetry.Length);
                                streamLog.WriteLine($"\n\n=== MODEL RETRY — ABORTING ({partialBeforeRetry.Length} chars to preserve, attempt {continuationAttempts + 1}/{MaxContinuationAttempts}) ===");

                                FireAndForgetAbort(session!);
                            }
                            else if (continuationAttempts >= MaxContinuationAttempts)
                            {
                                _logger.LogError(
                                    "[COPILOT] *** Max continuation attempts ({Max}) exhausted. " +
                                    "Completing with {Chars} chars of partial output. ***",
                                    MaxContinuationAttempts, deltaAccumulator.Length);
                                streamLog.WriteLine($"\n\n=== MAX CONTINUATIONS EXHAUSTED — completing with {deltaAccumulator.Length} chars ===");

                                // Salvage whatever we have — partial output beats nothing.
                                // AppendResults content takes priority over stdout deltas.
                                string salvaged = ReadAppendResultsFile(appendResultsPath);
                                string partialOutput = salvaged.Length > 0
                                    ? salvaged
                                    : deltaAccumulator.ToString().TrimEnd();
                                if (partialOutput.Length > 0)
                                {
                                    _logger.LogWarning(
                                        "[COPILOT] Max continuations exhausted — salvaging {Chars} chars from {Source}.",
                                        partialOutput.Length, salvaged.Length > 0 ? "AppendResults file" : "stdout accumulator");
                                    channel.Writer.TryWrite(new LlmCompleted(partialOutput, TokenUsage.Zero));
                                }
                                else
                                    channel.Writer.TryWrite(new LlmFailed("Server error during LLM response — all continuation attempts exhausted."));
                                channel.Writer.TryComplete();

                                FireAndForgetAbort(session!);
                            }
                        }
                        break;

                    case SessionWarningEvent warning:
                        _logger.LogWarning("[COPILOT] SessionWarning: {Message}", warning.Data?.Message);
                        streamLog.WriteLine($"\n  [WARNING] type={warning.Data?.WarningType} msg={warning.Data?.Message}");
                        break;

                    // ── Error ─────────────────────────────────────────────────────

                    case SessionErrorEvent err:
                        string errorMessage = err.Data?.Message ?? "Unknown Copilot session error";
                        _logger.LogError("[COPILOT] SessionError: {Message}", errorMessage);
                        streamLog.WriteLine($"\n\n=== SESSION ERROR ===\n{errorMessage}");

                        if (LlmRateLimitException.IsRateLimitSignal(errorMessage))
                        {
                            channel.Writer.TryWrite(new LlmFailed("__RATE_LIMIT__"));
                        }
                        else
                        {
                            // Salvage AppendResults content — the whole point of the tool is to
                            // survive errors. If the model wrote partial content before the error,
                            // return it as a completed result instead of losing everything.
                            string salvaged = ReadAppendResultsFile(appendResultsPath);
                            if (salvaged.Length > 0)
                            {
                                _logger.LogWarning(
                                    "[COPILOT] SessionError occurred but AppendResults file has {Chars} chars — salvaging.",
                                    salvaged.Length);
                                streamLog.WriteLine($"\n  [SALVAGE] AppendResults file has {salvaged.Length} chars — returning partial output");
                                channel.Writer.TryWrite(new LlmCompleted(salvaged, TokenUsage.Zero));
                            }
                            else
                            {
                                channel.Writer.TryWrite(new LlmFailed(errorMessage));
                            }
                        }
                        channel.Writer.TryComplete();
                        break;

                    case SessionIdleEvent:
                        if (!channel.Reader.Completion.IsCompleted)
                        {
                            // After aborting a model_retry, the session goes idle. Instead of
                            // completing, send a continuation prompt so the model picks up
                            // where the server error cut it off.
                            if (modelRetryDetected && partialBeforeRetry is not null
                                && continuationAttempts < MaxContinuationAttempts)
                            {
                                modelRetryDetected = false;
                                continuationAttempts++;

                                // Reset accumulator to the clean pre-retry snapshot.
                                deltaAccumulator.Clear();
                                deltaAccumulator.Append(partialBeforeRetry);
                                turnDeltaLength = 0;

                                // Build continuation prompt with trailing context for the model.
                                // In AppendResults mode, use the temp file content (the real output)
                                // instead of stdout deltas (which are just confirmation noise).
                                string contextSource;
                                if (appendResultsPath is not null)
                                {
                                    string appendContent = ReadAppendResultsFile(appendResultsPath);
                                    contextSource = appendContent.Length > 0 ? appendContent : partialBeforeRetry;
                                }
                                else
                                {
                                    contextSource = partialBeforeRetry;
                                }

                                int tailLength = Math.Min(contextSource.Length, 2000);
                                string tail = contextSource[^tailLength..];
                                string continuationPrompt =
                                    $"Your previous response was interrupted by a server error after producing " +
                                    $"{contextSource.Length} characters. Here is the end of what you produced:\n\n" +
                                    $"```\n{tail}\n```\n\n" +
                                    "Continue EXACTLY from where you left off. Do not repeat any content already shown above. " +
                                    "Do not add any preamble or explanation — just continue the output." +
                                    (appendResultsPath is not null
                                        ? " Continue writing via the AppendResults tool."
                                        : string.Empty);

                                _logger.LogWarning(
                                    "[COPILOT] *** Sending continuation prompt (attempt {Attempt}/{Max}, " +
                                    "{PartialChars} chars preserved, {TailChars} chars of trailing context). ***",
                                    continuationAttempts, MaxContinuationAttempts,
                                    partialBeforeRetry.Length, tailLength);
                                streamLog.WriteLine($"\n\n=== CONTINUATION PROMPT (attempt {continuationAttempts}/{MaxContinuationAttempts}, {tailLength} chars of context) ===");

                                _ = session!.SendAsync(new MessageOptions { Prompt = continuationPrompt }, linked)
                                    .ContinueWith(t =>
                                    {
                                        if (t.IsFaulted)
                                        {
                                            _logger.LogError("[COPILOT] Continuation SendAsync failed: {Error}",
                                                t.Exception?.InnerException?.Message);
                                            channel.Writer.TryWrite(new LlmFailed(
                                                $"Continuation prompt failed: {t.Exception?.InnerException?.Message}"));
                                            channel.Writer.TryComplete();
                                        }
                                    }, TaskScheduler.Default);
                                break;
                            }

                            // AppendResults mode — read the temp file as the authoritative output.
                            // The model wrote content incrementally via tool calls; stdout is
                            // just a confirmation message and should be ignored.
                            if (appendResultsPath is not null)
                            {
                                string appendOutput = ReadAppendResultsFile(appendResultsPath);

                                if (appendOutput.Length > 0)
                                {
                                    _logger.LogInformation("[COPILOT] AppendResults output: {Chars} chars from {Path}",
                                        appendOutput.Length, appendResultsPath);
                                    streamLog.WriteLine($"\n\n=== FINAL OUTPUT (AppendResults: {appendOutput.Length} chars from {appendResultsPath}) ===");
                                    channel.Writer.TryWrite(new LlmCompleted(appendOutput, TokenUsage.Zero));
                                }
                                else
                                {
                                    _logger.LogError("[COPILOT] AppendResults file is empty — model never called the tool");
                                    streamLog.WriteLine("\n\n=== SESSION IDLE — AppendResults file EMPTY ===");
                                    channel.Writer.TryWrite(new LlmFailed("AppendResults file is empty — model never called the tool."));
                                }

                                channel.Writer.TryComplete();
                                break;
                            }

                            string finalOutput = (deltaAccumulator.Length > 0
                                ? deltaAccumulator.ToString()
                                : lastMessageContent ?? string.Empty).TrimEnd();

                            if (finalOutput.Length > 0)
                            {
                                streamLog.WriteLine($"\n\n=== FINAL OUTPUT ({finalOutput.Length} chars) ===");
                                channel.Writer.TryWrite(new LlmCompleted(finalOutput, TokenUsage.Zero));
                                channel.Writer.TryComplete();
                            }
                            else if (ctx.OutputFilePath is not null)
                            {
                                streamLog.WriteLine("\n\n=== SESSION IDLE — direct file write mode, no stdout expected ===");
                                channel.Writer.TryWrite(new LlmCompleted(finalOutput, TokenUsage.Zero));
                                channel.Writer.TryComplete();
                            }
                            else if (shellRejected && shellRecoveryAttempts < MaxShellRecoveryAttempts)
                            {
                                // The model tried a shell tool, got rejected, and stalled.
                                // Send a recovery prompt to redirect it to file-reading tools.
                                shellRejected = false;
                                shellRecoveryAttempts++;

                                string recoveryPrompt =
                                    "Your shell/powershell tool call was blocked — shell tools are not available in this environment. " +
                                    "To read file contents, use the `view` tool with the file path. " +
                                    "To list files, use `glob` with a specific pattern (e.g. `src/**/*.java` instead of `**/*`). " +
                                    "To search file contents, use `grep`. " +
                                    "Now continue with your original task using only these tools.";

                                _logger.LogWarning(
                                    "[COPILOT] Shell rejected recovery — sending redirect prompt (attempt {Attempt}/{Max})",
                                    shellRecoveryAttempts, MaxShellRecoveryAttempts);
                                streamLog.WriteLine($"\n\n=== SHELL RECOVERY PROMPT (attempt {shellRecoveryAttempts}/{MaxShellRecoveryAttempts}) ===");

                                _ = session!.SendAsync(new MessageOptions { Prompt = recoveryPrompt }, linked)
                                    .ContinueWith(t =>
                                    {
                                        if (t.IsFaulted)
                                        {
                                            _logger.LogError("[COPILOT] Shell recovery SendAsync failed: {Error}",
                                                t.Exception?.InnerException?.Message);
                                            channel.Writer.TryWrite(new LlmFailed(
                                                $"Shell recovery prompt failed: {t.Exception?.InnerException?.Message}"));
                                            channel.Writer.TryComplete();
                                        }
                                    }, TaskScheduler.Default);
                                // Do NOT complete the channel — the session will continue
                                // processing after the recovery prompt is received.
                                break;
                            }
                            else
                            {
                                _logger.LogError("[COPILOT] Session completed with no output (AssistantMessageEvent never fired with content)");
                                streamLog.WriteLine("\n\n=== SESSION IDLE — NO OUTPUT (AssistantMessageEvent never fired with content) ===");
                                channel.Writer.TryWrite(new LlmFailed("Copilot session completed with no output."));
                                channel.Writer.TryComplete();
                            }
                        }
                        break;

                    default:
                        // All other SDK events (subagent, skills, hooks, MCP, permissions,
                        // commands, plan mode, elicitation, sampling, etc.) are logged by
                        // type name only. Individual case statements are unnecessary —
                        // these events carry no data we act on.
                        streamLog.WriteLine($"\n  [{evt.GetType().Name}]");
                        break;
                }
            });

            // Ensure the output directory exists before telling the model to write there.
            if (ctx.OutputFilePath is not null)
            {
                string? outputDir = Path.GetDirectoryName(ctx.OutputFilePath);
                if (outputDir is not null)
                    Directory.CreateDirectory(outputDir);
            }

            string prompt = ctx.UserPrompt + BuildPromptSuffix(ctx);

            // SendAsync queues the message and returns immediately. The session runs
            // autonomously — events fire via the On() handler above. SessionIdleEvent
            // signals completion and writes the final LlmCompleted/LlmFailed to the channel.
            await session.SendAsync(new MessageOptions { Prompt = prompt }, linked);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("[COPILOT] Cancelled by caller");
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("[COPILOT] Timed out after {Timeout}", ctx.Timeout);

            if (!TrySalvageAndComplete(appendResultsPath, streamLog, channel.Writer, "TIMEOUT"))
                setupFailure = $"Copilot session timed out after {ctx.Timeout.TotalMinutes:F0} minutes.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[COPILOT] SDK error: {Type}: {Message}", ex.GetType().Name, ex.Message);

            if (!TrySalvageAndComplete(appendResultsPath, streamLog, channel.Writer, "SDK ERROR"))
                setupFailure = $"Copilot SDK error: {ex.Message}";
        }

        if (setupFailure is not null)
        {
            yield return new LlmFailed(setupFailure);
            DisposeSubscription(subscription);
            await DisposeClientAndSession(client, session);
            yield break;
        }

        bool timedOut = false;

        // Buffer events inside the try-catch — C# forbids yield inside try-catch.
        List<LlmOutputEvent> buffered = new();
        try
        {
            await foreach (LlmOutputEvent evt in channel.Reader.ReadAllAsync(linked))
            {
                if (evt is LlmFailed failed && failed.Error == "__RATE_LIMIT__")
                {
                    DisposeSubscription(subscription);
                    await DisposeClientAndSession(client, session);
                    throw new LlmRateLimitException();
                }

                buffered.Add(evt);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("[COPILOT] Timed out after {Timeout} during event drain", ctx.Timeout);
            timedOut = true;
        }

        // Yield buffered events outside the try-catch.
        foreach (LlmOutputEvent evt in buffered)
            yield return evt;

        if (timedOut)
            yield return new LlmFailed($"Copilot session timed out after {ctx.Timeout.TotalMinutes:F0} minutes.");

        DisposeSubscription(subscription);
        await DisposeClientAndSession(client, session);
    }

    #endregion

    #region Initialize

    /// <summary>
    /// Builds the <see cref="SessionConfig"/> from the <see cref="LlmExecutionContext"/>,
    /// mapping system prompt, model selection, and tool permissions.
    /// </summary>
    private SessionConfig BuildSessionConfig(LlmExecutionContext ctx, Action? onShellRejected = null)
    {
        SessionConfig config = new SessionConfig
        {
            Model     = ctx.Model,
            Streaming = true,
            OnPermissionRequest = BuildPermissionHandler(ctx, onShellRejected),
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            Hooks = new SessionHooks
            {
                OnErrorOccurred = (input, _) =>
                {
                    // Log the real error the SDK saw — this is the detail that was getting swallowed.
                    _logger.LogError("[COPILOT] SDK error hook: Recoverable={Recoverable}, Context={Context}, Error={Error}",
                        input.Recoverable, input.ErrorContext ?? "(none)", input.Error);
                    // Disable the CLI's internal retry loop (defaults to 5 retries).
                    // Our orchestrator handles retries at a higher level.
                    return Task.FromResult<ErrorOccurredHookOutput?>(new ErrorOccurredHookOutput
                    {
                        RetryCount    = 0,
                        ErrorHandling = "abort"
                    });
                }
            }
        };

        if (!string.IsNullOrEmpty(ctx.SystemPrompt))
        {
            config.SystemMessage = new SystemMessageConfig
            {
                Mode    = SystemMessageMode.Customize,
                Content = ctx.SystemPrompt,
                Sections = new Dictionary<string, SectionOverride>
                {
                    [SystemPromptSections.Identity] = new()
                    {
                        Action = SectionOverrideAction.Replace,
                        Content = "You are a code analysis and generation engine. " +
                                  "Follow the instructions in the custom instructions section exactly. " +
                                  "Use your file-reading tools to explore the codebase as needed."
                    },
                    [SystemPromptSections.Tone] = new()
                    {
                        Action = SectionOverrideAction.Replace,
                        Content = "Output only what is requested. No preamble, no sign-off, " +
                                  "no narration of your intent. Do not explain what you are about to do."
                    },
                    [SystemPromptSections.CodeChangeRules] = new()
                    {
                        Action = SectionOverrideAction.Remove
                    }
                }
            };
        }

        // Override max output tokens so the Copilot CLI knows the model's true ceiling.
        // Without this, the CLI uses its own default which may be much lower than what
        // the model supports, causing the agentic loop to re-prompt on truncation.
        if (ctx.MaxOutputTokens > 0)
        {
            config.ModelCapabilities = new Rpc.ModelCapabilitiesOverride
            {
                Limits = new Rpc.ModelCapabilitiesOverrideLimits
                {
                    MaxOutputTokens = ctx.MaxOutputTokens
                }
            };
        }

        return config;
    }

    /// <summary>
    /// Creates a permission handler that denies shell execution unconditionally and
    /// controls file write access based on the execution context.
    /// </summary>
    private PermissionRequestHandler BuildPermissionHandler(LlmExecutionContext ctx, Action? onShellRejected = null)
    {
        bool allowWrites = (ctx.EnableFileTools && !ctx.EnableReadOnlyFileTools) ||
                           ctx.OutputFilePath is not null;

        return (request, _) =>
        {
            if (string.Equals(request.Kind, "shell", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[COPILOT] Permission denied: shell — will inject recovery prompt on idle");
                onShellRejected?.Invoke();
                return Task.FromResult(new PermissionRequestResult
                {
                    Kind = PermissionRequestResultKind.Rejected
                });
            }

            if (string.Equals(request.Kind, "write", StringComparison.OrdinalIgnoreCase) && !allowWrites)
            {
                _logger.LogDebug("[COPILOT] Permission denied: write");
                return Task.FromResult(new PermissionRequestResult
                {
                    Kind = PermissionRequestResultKind.Rejected
                });
            }

            return Task.FromResult(new PermissionRequestResult
            {
                Kind = PermissionRequestResultKind.Approved
            });
        };
    }

    /// <summary>
    /// Determines the prompt suffix that tells the model HOW to emit output.
    /// Each output mode (AppendResults, file write, implement, stdout with/without tools)
    /// needs different instructions appended to the user prompt.
    /// </summary>
    private static string BuildPromptSuffix(LlmExecutionContext ctx)
    {
        if (ctx.EnableAppendResultsTool)
        {
            return "\n\nCRITICAL OUTPUT RULE: Do NOT write your output to stdout. " +
                "Instead, use the AppendResults tool to write ALL output incrementally. " +
                "Call AppendResults after EVERY 1–3 items — do NOT accumulate large blocks. " +
                "Each call is a checkpoint that atomically persists content to disk, protecting " +
                "against connection loss or server errors. If you wait too long between calls, " +
                "a server error will destroy all unpersisted work. Frequent small calls are MUCH " +
                "better than infrequent large calls. Pass raw Markdown content directly. " +
                "After all content is written, respond with a brief confirmation to stdout " +
                "(e.g. \"Done — all sections written via AppendResults.\").\n" +
                "Do NOT use shell, powershell, or bash tools — they are blocked. " +
                "If a glob result is too large, use narrower glob patterns or grep instead.";
        }

        if (ctx.OutputFilePath is not null)
        {
            return $"\n\nWrite your complete output to this file using your file-write tool: {ctx.OutputFilePath}\n" +
                   "Create the directory if it does not exist. Do NOT output the content to stdout — write it to the file only.\n" +
                   "Do NOT use shell, powershell, or bash tools — they are blocked. " +
                   "If a glob result is too large, use narrower glob patterns or grep instead.";
        }

        // Implement mode — model has write tools and targets files directly.
        if (ctx.EnableFileTools && !ctx.EnableReadOnlyFileTools)
            return "\n\nDo NOT use shell, powershell, or bash tools — they are blocked. " +
                   "If a glob result is too large, use narrower glob patterns or grep instead.";

        // Read-only file tools — must read first, then produce output to stdout in one shot.
        if (ctx.EnableFileTools || ctx.EnableReadOnlyFileTools)
            return ctx.JsonOutputMode ? JsonWithToolsInstruction : StdoutWithToolsInstruction;

        return StdoutInstruction;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Attempts to salvage AppendResults content when the session dies (timeout, SDK error).
    /// If the temp file has content, writes <see cref="LlmCompleted"/> to the channel and
    /// completes it. Returns <c>true</c> if content was salvaged; <c>false</c> if the caller
    /// should fall back to its own error handling.
    /// </summary>
    private bool TrySalvageAndComplete(
        string? appendResultsPath, StreamDiagnosticLog streamLog,
        ChannelWriter<LlmOutputEvent> writer, string logLabel)
    {
        string salvaged = ReadAppendResultsFile(appendResultsPath);
        if (salvaged.Length > 0)
        {
            _logger.LogWarning("[COPILOT] {Label} but AppendResults file has {Chars} chars — salvaging.",
                logLabel, salvaged.Length);
            streamLog.WriteLine($"\n\n=== {logLabel} SALVAGE — AppendResults: {salvaged.Length} chars ===");
            writer.TryWrite(new LlmCompleted(salvaged, TokenUsage.Zero));
        }

        writer.TryComplete();
        return salvaged.Length > 0;
    }

    /// <summary>
    /// Aborts the session without awaiting — used when the CLI starts an unwanted retry
    /// and we need to kill it before sending our own continuation prompt.
    /// </summary>
    private void FireAndForgetAbort(CopilotSession session)
    {
        _ = session.AbortAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                _logger.LogWarning("[COPILOT] AbortAsync failed: {Error}", t.Exception?.InnerException?.Message);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Safely reads the AppendResults temp file, returning its content or empty string
    /// on any failure. Used by error/timeout salvage paths to recover partial output that
    /// the model persisted via tool calls before the session died.
    /// </summary>
    private string ReadAppendResultsFile(string? path)
    {
        if (path is null) return string.Empty;
        try
        {
            return File.Exists(path)
                ? File.ReadAllText(path, System.Text.Encoding.UTF8).TrimEnd()
                : string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[COPILOT] Failed to read AppendResults file {Path}: {Error}", path, ex.Message);
            return string.Empty;
        }
    }

    /// <summary>
    /// Safely disposes the event subscription.
    /// </summary>
    private void DisposeSubscription(IDisposable? subscription)
    {
        try { subscription?.Dispose(); }
        catch (Exception ex) { _logger.LogDebug("[COPILOT] Subscription dispose error: {Message}", ex.Message); }
    }

    /// <summary>
    /// Safely disposes the session and stops the client, logging any errors.
    /// </summary>
    private async Task DisposeClientAndSession(CopilotClient? client, CopilotSession? session)
    {
        if (session is not null)
        {
            try { await session.DisposeAsync(); }
            catch (Exception ex) { _logger.LogDebug("[COPILOT] Session dispose error: {Message}", ex.Message); }
        }

        if (client is not null)
        {
            try { await client.StopAsync(); }
            catch (Exception ex) { _logger.LogDebug("[COPILOT] Client stop error: {Message}", ex.Message); }
        }
    }

    #endregion

}
