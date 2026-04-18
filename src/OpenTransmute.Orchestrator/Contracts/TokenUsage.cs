namespace OpenTransmute.Orchestrator.Contracts;

/// <summary>
/// Token counts and optional cost for a single LLM call.
/// For Claude CLI, InputTokens/OutputTokens are 0 — only CostUsd is available.
/// For OpenAI via SK, all fields are populated from the response metadata.
/// </summary>
public record TokenUsage(int InputTokens, int OutputTokens, double? CostUsd = null)
{
    public int Total => InputTokens + OutputTokens;

    public static readonly TokenUsage Zero = new(0, 0);

    public static TokenUsage operator +(TokenUsage a, TokenUsage b) =>
        new(a.InputTokens + b.InputTokens, a.OutputTokens + b.OutputTokens,
            a.CostUsd.HasValue || b.CostUsd.HasValue
                ? (a.CostUsd ?? 0) + (b.CostUsd ?? 0)
                : null);
}

/// <summary>Accumulated token usage across all phases of a run.</summary>
public sealed class TokenAccumulator
{
    private int _inputTokens;
    private int _outputTokens;
    private double _costUsd;

    public void Add(TokenUsage usage)
    {
        _inputTokens += usage.InputTokens;
        _outputTokens += usage.OutputTokens;
        _costUsd += usage.CostUsd ?? 0;
    }

    public TokenUsage Total => new(_inputTokens, _outputTokens, _costUsd > 0 ? _costUsd : null);
}
