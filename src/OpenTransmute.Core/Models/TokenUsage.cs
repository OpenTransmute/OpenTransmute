namespace OpenTransmute.Models;

/// <summary>
/// Token counts and optional cost for a single LLM call.
/// For Claude CLI, InputTokens/OutputTokens are 0 — the subprocess does not report token counts.
/// For OpenAI-compatible backends, all fields are populated from the response metadata.
/// </summary>
public record TokenUsage(int InputTokens, int OutputTokens, double? CostUsd = null)
{
    /// <summary>Sum of input and output tokens.</summary>
    public int Total => InputTokens + OutputTokens;

    /// <summary>An empty usage total — the additive identity.</summary>
    public static readonly TokenUsage Zero = new(0, 0);

    /// <summary>
    /// Combines two usage totals. Cost is summed only when at least one operand
    /// carries a cost; otherwise the result's cost stays null (unknown).
    /// </summary>
    public static TokenUsage operator +(TokenUsage a, TokenUsage b) =>
        new(a.InputTokens + b.InputTokens, a.OutputTokens + b.OutputTokens,
            a.CostUsd.HasValue || b.CostUsd.HasValue
                ? (a.CostUsd ?? 0) + (b.CostUsd ?? 0)
                : null);
}

/// <summary>Thread-safe accumulator for token usage across multiple LLM calls.</summary>
public sealed class TokenAccumulator
{
    #region Members

    private int _inputTokens;
    private int _outputTokens;
    private double _costUsd;

    #endregion

    #region Methods

    /// <summary>Adds a single call's token usage to the running total.</summary>
    /// <param name="usage">Token usage from one LLM call.</param>
    public void Add(TokenUsage usage)
    {
        _inputTokens  += usage.InputTokens;
        _outputTokens += usage.OutputTokens;
        _costUsd      += usage.CostUsd ?? 0;
    }

    /// <summary>Returns the accumulated total as a <see cref="TokenUsage"/> record.</summary>
    public TokenUsage Total => new(_inputTokens, _outputTokens, _costUsd > 0 ? _costUsd : null);

    #endregion
}
