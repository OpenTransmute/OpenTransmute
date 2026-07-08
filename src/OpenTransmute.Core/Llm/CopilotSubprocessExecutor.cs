using System.Runtime.CompilerServices;
using System.Threading.Channels;
using GitHub.Copilot;
using Microsoft.Extensions.AI;
using Rpc = GitHub.Copilot.Rpc;
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

    // Built-in Copilot CLI tools that turn a single-document decompose/compose/implement phase
    // into a fleet of background sub-agents. These come from BuiltInTools.Isolated and exist for
    // interactive multi-agent orchestration — exactly the wrong behavior here. Left enabled, the
    // model spawns `task` sub-agents, then burns the whole run polling them with `read_agent`
    // instead of reading files and emitting one document (observed: phase-03-01 spent 26 turns
    // orchestrating five background agents, produced ~1.5KB, then died on a server stream error).
    // There is no phase in which sub-agent orchestration is desirable, so the suite is excluded
    // unconditionally. `ask_user` is included because a non-interactive run has no one to answer
    // it — the model would stall until the idle timeout.
    private static readonly IList<string> ExcludedBuiltInTools = new List<string>
    {
        "task",          // spawn a background sub-agent — the direct culprit
        "read_agent",    // poll a sub-agent for results
        "write_agent",   // send input to a sub-agent
        "list_agents",   // enumerate running sub-agents
        "send_inbox",    // inter-agent messaging
        "context_board", // shared sub-agent context board
        "ask_user"       // interactive prompt — nothing answers it in a headless run
    };

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

            // SDK 1.0.4: UseStdio/AutoStart/Cwd were removed. Mode selects the Copilot CLI
            // transport, WorkingDirectory sets the file-tool root, and we start explicitly below.
            client = new CopilotClient(new CopilotClientOptions
            {
                Mode                      = CopilotClientMode.CopilotCli,
                WorkingDirectory          = cwd,
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

                config.Tools = new List<AIFunctionDeclaration> { appendTool };
                _logger.LogInformation("[COPILOT] AppendResults tool registered on config. Temp file: {Path}", appendResultsPath);
            }

            session = await client.CreateSessionAsync(config);
            _logger.LogDebug("[COPILOT] Session created: {SessionId}", session.SessionId);

            streamLog.WriteHeader(ctx,
                ("FileToolsRoot", ctx.FileToolsRoot),
                ("AppendResultsPath", appendResultsPath),
                ("IdleTimeoutSec", idleTimeoutSec.ToString()),
                ("ExcludedTools", string.Join(", ", ExcludedBuiltInTools)));

            // Track last assistant message content for the final output.
            string? lastMessageContent = null;
            // Accumulate delta text across all turns.
            System.Text.StringBuilder deltaAccumulator = new();
            // Chars received in the CURRENT turn — reset on each TurnStart.
            int turnDeltaLength = 0;

            // model_retry counter — logging only. The SDK owns retry; we don't intercept it.
            int modelRetryCount = 0;

            // SDK 1.0.4: On is generic with no non-generic overload, so the base event
            // type must be named explicitly to subscribe to every session event.
            subscription = session.On<SessionEvent>(evt =>
            {
                switch (evt)
                {
                    case AssistantMessageDeltaEvent delta:
                        string chunk = delta.Data.DeltaContent ?? string.Empty;
                        if (chunk.Length == 0)
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
                        // Separate this message from the previous one so narration never glues
                        // onto the final document title (which once truncated 40% of an output).
                        if (deltaAccumulator.Length > 0 && deltaAccumulator[^1] != '\n')
                            deltaAccumulator.Append('\n');
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
                        var usageData = usage.Data;
                        streamLog.WriteLine(
                            $"\n  [USAGE] model={usageData?.Model} in={usageData?.InputTokens} out={usageData?.OutputTokens} " +
                            $"cacheR={usageData?.CacheReadTokens} cacheW={usageData?.CacheWriteTokens} " +
                            $"duration={usageData?.Duration}ms latency={usageData?.InterTokenLatency}ms");
                        break;

                    case SessionUsageInfoEvent usageInfo:
                        var usageInfoData = usageInfo.Data;
                        streamLog.WriteLine(
                            $"\n  [USAGE INFO] tokens={usageInfoData?.CurrentTokens}/{usageInfoData?.TokenLimit} " +
                            $"system={usageInfoData?.SystemTokens} conversation={usageInfoData?.ConversationTokens} " +
                            $"toolDefs={usageInfoData?.ToolDefinitionsTokens} messages={usageInfoData?.MessagesLength} initial={usageInfoData?.IsInitial}");
                        break;

                    // ── Session lifecycle ─────────────────────────────────────────

                    case SessionStartEvent sessionStart:
                        var startData = sessionStart.Data;
                        streamLog.WriteLine(
                            $"\n  [SESSION START] id={startData?.SessionId} model={startData?.SelectedModel} " +
                            $"copilot={startData?.CopilotVersion} reasoning={startData?.ReasoningEffort} tier={startData?.ContextTier}");
                        break;

                    case SessionShutdownEvent shutdown:
                        var shutdownData = shutdown.Data;
                        streamLog.WriteLine(
                            $"\n  [SESSION SHUTDOWN] type={shutdownData?.ShutdownType} error={shutdownData?.ErrorReason} " +
                            $"model={shutdownData?.CurrentModel} tokens={shutdownData?.CurrentTokens} " +
                            $"system={shutdownData?.SystemTokens} conversation={shutdownData?.ConversationTokens} " +
                            $"toolDefs={shutdownData?.ToolDefinitionsTokens} apiDuration={shutdownData?.TotalApiDuration.TotalMilliseconds}ms");
                        break;

                    // SessionLifecycleEvent is not a SessionEvent subtype — cannot be matched here.

                    case SessionModelChangeEvent modelChange:
                        var modelChangeData = modelChange.Data;
                        streamLog.WriteLine(
                            $"\n  [MODEL CHANGE] {modelChangeData?.PreviousModel} → {modelChangeData?.NewModel} cause={modelChangeData?.Cause} " +
                            $"reasoning={modelChangeData?.PreviousReasoningEffort} → {modelChangeData?.ReasoningEffort} tier={modelChangeData?.ContextTier}");
                        break;

                    // ── Truncation / compaction ──────────────────────────────────

                    case SessionTruncationEvent truncation:
                        var truncationData = truncation.Data;
                        streamLog.WriteLine(
                            $"\n  [TRUNCATION] by={truncationData?.PerformedBy} limit={truncationData?.TokenLimit} " +
                            $"pre={truncationData?.PreTruncationTokensInMessages} post={truncationData?.PostTruncationTokensInMessages} " +
                            $"removed={truncationData?.TokensRemovedDuringTruncation} msgs={truncationData?.MessagesRemovedDuringTruncation} " +
                            $"preMsgs={truncationData?.PreTruncationMessagesLength} postMsgs={truncationData?.PostTruncationMessagesLength}");
                        break;

                    case SessionCompactionStartEvent compactStart:
                        var compactStartData = compactStart.Data;
                        streamLog.WriteLine(
                            $"\n  [COMPACTION START] system={compactStartData?.SystemTokens} conversation={compactStartData?.ConversationTokens} " +
                            $"toolDefs={compactStartData?.ToolDefinitionsTokens}");
                        break;

                    case SessionCompactionCompleteEvent compactDone:
                        var compactDoneData = compactDone.Data;
                        streamLog.WriteLine(
                            $"\n  [COMPACTION DONE] ok={compactDoneData?.Success} pre={compactDoneData?.PreCompactionTokens} post={compactDoneData?.PostCompactionTokens} " +
                            $"removed={compactDoneData?.MessagesRemoved} compactionTokens={compactDoneData?.CompactionTokensUsed} " +
                            $"error={compactDoneData?.Error}");
                        break;

                    // ── Model call failures ──────────────────────────────────────

                    case ModelCallFailureEvent callFail:
                        var callFailData = callFail.Data;
                        _logger.LogWarning("[COPILOT] ModelCallFailure: model={Model} status={Status} error={Error}",
                            callFailData?.Model, callFailData?.StatusCode, callFailData?.ErrorMessage);
                        streamLog.WriteLine(
                            $"\n  [MODEL CALL FAILURE] model={callFailData?.Model} status={callFailData?.StatusCode} " +
                            $"error={callFailData?.ErrorMessage} source={callFailData?.Source} duration={callFailData?.Duration}ms");
                        break;

                    // ── Abort ─────────────────────────────────────────────────────

                    case AbortEvent abort:
                        _logger.LogWarning("[COPILOT] Abort: {Reason}", abort.Data?.Reason);
                        streamLog.WriteLine($"\n  [ABORT] reason={abort.Data?.Reason}");
                        break;

                    // ── Info / warning / model retry ──────────────────────────────

                    case SessionInfoEvent info:
                        streamLog.WriteLine($"\n  [INFO] type={info.Data?.InfoType} msg={info.Data?.Message} tip={info.Data?.Tip}");

                        // model_retry: the SDK is retrying the request after a mid-stream server
                        // error. We defer entirely to its native retry — no interception.
                        if (string.Equals(info.Data?.InfoType, "model_retry", StringComparison.OrdinalIgnoreCase))
                        {
                            modelRetryCount++;
                            _logger.LogWarning("[COPILOT] model_retry #{RetryNum} — deferring to SDK native retry.",
                                modelRetryCount);
                            streamLog.WriteLine($"\n  [INFO] model_retry #{modelRetryCount} — deferring to SDK native retry");
                        }
                        break;

                    case SessionWarningEvent warning:
                        _logger.LogWarning("[COPILOT] SessionWarning: {Message}", warning.Data?.Message);
                        streamLog.WriteLine($"\n  [WARNING] type={warning.Data?.WarningType} msg={warning.Data?.Message}");
                        break;

                    // ── Error ─────────────────────────────────────────────────────

                    case SessionErrorEvent err:
                        HandleSessionError(
                            err.Data?.Message ?? "Unknown Copilot session error",
                            appendResultsPath, streamLog, channel.Writer);
                        break;

                    case SessionIdleEvent:
                        if (!channel.Reader.Completion.IsCompleted)
                        {
                            // AppendResults mode — read the temp file as the authoritative output.
                            // The model wrote content incrementally via tool calls; stdout is
                            // just a confirmation message and should be ignored.
                            if (appendResultsPath is not null)
                            {
                                CompleteFromAppendResults(appendResultsPath, streamLog, channel.Writer);
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

            // The long-context tier is applied at session-creation time via SessionConfig.ContextTier
            // in BuildSessionConfig — that is the SDK's documented create_session(context_tier=...)
            // path (microsoft/conductor#251). Do NOT re-apply it here with SetModelAsync: switching
            // the model / overriding capabilities on an already-created CLI session is rejected by the
            // backend ("Session was not created with authentication info or custom provider") and
            // kills the run with no output. Create-time ContextTier is the only supported mechanism.

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
            // Strip the multi-agent orchestration suite. These tools (task/read_agent/…) let the
            // model spawn background sub-agents instead of doing the single-document job, which is
            // how phase-03-01 thrashed itself to death. ExcludedTools is the SDK's supported gate.
            ExcludedTools = ExcludedBuiltInTools,
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
                Sections = new Dictionary<SystemMessageSection, SectionOverride>
                {
                    [SystemMessageSection.Identity] = new()
                    {
                        Action = SectionOverrideAction.Replace,
                        Content = "You are a code analysis and generation engine. " +
                                  "Follow the instructions in the custom instructions section exactly. " +
                                  "Use your file-reading tools to explore the codebase as needed."
                    },
                    [SystemMessageSection.Tone] = new()
                    {
                        Action = SectionOverrideAction.Replace,
                        Content = "Output only what is requested. No preamble, no sign-off, " +
                                  "no narration of your intent. Do not explain what you are about to do."
                    },
                    [SystemMessageSection.CodeChangeRules] = new()
                    {
                        Action = SectionOverrideAction.Remove
                    }
                }
            };
        }

        // Override max output tokens so the Copilot CLI knows the model's true ceiling.
        // Without this, the CLI uses its own default which may be much lower than what
        // the model supports, causing the agentic loop to re-prompt on truncation.
        // NOTE: deliberately NOT setting a numeric context-window override here. An explicit window
        // override is clamped to the model's default (~200k) and suppresses the long-context tier's
        // derived window. The context window is controlled exclusively by ContextTier (below).
        if (ctx.MaxOutputTokens > 0)
        {
            // GHCP001: ModelCapabilitiesOverride is a preview SDK surface. Deliberately opted in —
            // it's the only way to tell the CLI the model's true output ceiling.
#pragma warning disable GHCP001
            Rpc.ModelCapabilitiesOverrideLimits limits = new() { MaxOutputTokens = ctx.MaxOutputTokens };
            config.ModelCapabilities = new Rpc.ModelCapabilitiesOverride { Limits = limits };
#pragma warning restore GHCP001
        }

        // The large context window on tiered models (e.g. Claude Opus's 1M) is a separate
        // "long context" tier that defaults to ~200k. A numeric token override does NOT unlock it
        // — the CLI clamps to the model's default tier and ignores any higher number. Requesting
        // the long-context tier at session-creation time is what actually unlocks the capacity;
        // the session then derives its effective capability overrides (token display, compaction,
        // truncation, request limits) from the tier. This is the SDK's documented
        // create_session(context_tier=...) path (microsoft/conductor#251) and the ONLY supported
        // mechanism. (Non-default tiers may carry higher per-token pricing.)
        if (ctx.ContextTier == LlmContextTier.LongContext)
            config.ContextTier = ContextTier.LongContext;

        return config;
    }

    /// <summary>
    /// Creates a permission handler that denies shell execution unconditionally and
    /// controls file write access based on the execution context.
    /// </summary>
    // GHCP001: PermissionDecision is a preview SDK surface. Deliberately opted in — it's the
    // permission-callback return contract; there is no stable alternative in 1.0.4.
#pragma warning disable GHCP001
    private Func<PermissionRequest, PermissionInvocation, Task<Rpc.PermissionDecision>> BuildPermissionHandler(
        LlmExecutionContext ctx, Action? onShellRejected = null)
    {
        bool allowWrites = (ctx.EnableFileTools && !ctx.EnableReadOnlyFileTools) ||
                           ctx.OutputFilePath is not null;

        return (request, _) =>
        {
            if (string.Equals(request.Kind, "shell", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[COPILOT] Permission denied: shell — will inject recovery prompt on idle");
                onShellRejected?.Invoke();
                return Task.FromResult(Rpc.PermissionDecision.Reject("Shell tools are blocked in this environment."));
            }

            if (string.Equals(request.Kind, "write", StringComparison.OrdinalIgnoreCase) && !allowWrites)
            {
                _logger.LogDebug("[COPILOT] Permission denied: write");
                return Task.FromResult(Rpc.PermissionDecision.Reject("File writes are not permitted for this operation."));
            }

            return Task.FromResult(Rpc.PermissionDecision.ApproveOnce());
        };
    }
#pragma warning restore GHCP001

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
    /// <summary>
    /// Handles a Copilot <see cref="SessionErrorEvent"/>: classifies rate-limit signals, salvages any
    /// partial AppendResults content as a completed result, and otherwise fails the channel. Always
    /// completes the writer — a session error is terminal for the run.
    /// </summary>
    /// <param name="errorMessage">The error text reported by the session.</param>
    /// <param name="appendResultsPath">Path to the AppendResults temp file, or null when the tool is disabled.</param>
    /// <param name="streamLog">Diagnostic stream log for the run.</param>
    /// <param name="writer">Channel writer feeding the async-enumerable consumer.</param>
    private void HandleSessionError(
        string errorMessage, string? appendResultsPath,
        StreamDiagnosticLog streamLog, ChannelWriter<LlmOutputEvent> writer)
    {
        _logger.LogError("[COPILOT] SessionError: {Message}", errorMessage);
        streamLog.WriteLine($"\n\n=== SESSION ERROR ===\n{errorMessage}");

        if (LlmRateLimitException.IsRateLimitSignal(errorMessage))
        {
            writer.TryWrite(new LlmFailed("__RATE_LIMIT__"));
        }
        else
        {
            // Salvage AppendResults content — the whole point of the tool is to survive errors.
            // If the model wrote partial content before the error, return it as a completed result
            // instead of losing everything.
            string salvaged = ReadAppendResultsFile(appendResultsPath);
            if (salvaged.Length > 0)
            {
                _logger.LogWarning(
                    "[COPILOT] SessionError occurred but AppendResults file has {Chars} chars — salvaging.",
                    salvaged.Length);
                streamLog.WriteLine($"\n  [SALVAGE] AppendResults file has {salvaged.Length} chars — returning partial output");
                writer.TryWrite(new LlmCompleted(salvaged, TokenUsage.Zero));
            }
            else
            {
                writer.TryWrite(new LlmFailed(errorMessage));
            }
        }

        writer.TryComplete();
    }

    /// <summary>
    /// Resolves the final output for an idle session running in AppendResults mode. The model wrote
    /// content incrementally via tool calls, so the temp file — not stdout — is authoritative. Writes
    /// a completed result when the file has content, a failure when it is empty, then completes the channel.
    /// </summary>
    /// <param name="appendResultsPath">Path to the AppendResults temp file (non-null in this mode).</param>
    /// <param name="streamLog">Diagnostic stream log for the run.</param>
    /// <param name="writer">Channel writer feeding the async-enumerable consumer.</param>
    private void CompleteFromAppendResults(
        string appendResultsPath, StreamDiagnosticLog streamLog,
        ChannelWriter<LlmOutputEvent> writer)
    {
        string appendOutput = ReadAppendResultsFile(appendResultsPath);

        if (appendOutput.Length > 0)
        {
            _logger.LogInformation("[COPILOT] AppendResults output: {Chars} chars from {Path}",
                appendOutput.Length, appendResultsPath);
            streamLog.WriteLine($"\n\n=== FINAL OUTPUT (AppendResults: {appendOutput.Length} chars from {appendResultsPath}) ===");
            writer.TryWrite(new LlmCompleted(appendOutput, TokenUsage.Zero));
        }
        else
        {
            _logger.LogError("[COPILOT] AppendResults file is empty — model never called the tool");
            streamLog.WriteLine("\n\n=== SESSION IDLE — AppendResults file EMPTY ===");
            writer.TryWrite(new LlmFailed("AppendResults file is empty — model never called the tool."));
        }

        writer.TryComplete();
    }

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
