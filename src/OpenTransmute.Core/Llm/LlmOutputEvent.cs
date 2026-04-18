using OpenTransmute.Models;

namespace OpenTransmute.Llm;

/// <summary>
/// Discriminated union of events produced by an <see cref="ILlmExecutor"/> during a single LLM call.
/// Consumers iterate the event stream to drive logging, progress tracking, and output capture.
/// </summary>
public abstract record LlmOutputEvent;

/// <summary>A single line of streaming output from the LLM.</summary>
/// <param name="Text">The output line text.</param>
public record LlmLine(string Text) : LlmOutputEvent;

/// <summary>
/// The LLM call completed successfully. Carries the full accumulated output and token usage.
/// For the Claude subprocess backend, <see cref="Tokens"/> is <see cref="TokenUsage.Zero"/> —
/// the CLI does not report token counts.
/// </summary>
/// <param name="Output">The full text output from the LLM.</param>
/// <param name="Tokens">Token usage for this call.</param>
public record LlmCompleted(string Output, TokenUsage Tokens) : LlmOutputEvent;

/// <summary>
/// The LLM call failed. The executor does not throw — all failure information is carried here.
/// </summary>
/// <param name="Error">The error message describing the failure.</param>
public record LlmFailed(string Error) : LlmOutputEvent;
