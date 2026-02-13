using System.Threading;

namespace PackageUpdateSkill;

/// <summary>
/// Tracks actual token usage from AssistantUsageEvent data.
/// Formats counts with K/M suffixes (max 3 significant digits).
/// </summary>
public class TokenTracker
{
    private long _inputTokens;
    private long _outputTokens;
    private long _cacheReadTokens;
    private long _cacheWriteTokens;
    private long _costMicros; // cost in millionths of a dollar for precision
    private long _durationMs;
    private int _llmCalls;

    public long InputTokens => Interlocked.Read(ref _inputTokens);
    public long OutputTokens => Interlocked.Read(ref _outputTokens);
    public long CacheReadTokens => Interlocked.Read(ref _cacheReadTokens);
    public long CacheWriteTokens => Interlocked.Read(ref _cacheWriteTokens);
    public double Cost => Interlocked.Read(ref _costMicros) / 1_000_000.0;
    public long DurationMs => Interlocked.Read(ref _durationMs);
    public int LlmCalls => _llmCalls;

    public void AddInput(long tokens) => Interlocked.Add(ref _inputTokens, tokens);
    public void AddOutput(long tokens) => Interlocked.Add(ref _outputTokens, tokens);
    public void AddCacheRead(long tokens) => Interlocked.Add(ref _cacheReadTokens, tokens);
    public void AddCacheWrite(long tokens) => Interlocked.Add(ref _cacheWriteTokens, tokens);
    public void AddCost(double cost) => Interlocked.Add(ref _costMicros, (long)(cost * 1_000_000));
    public void AddDuration(double ms) => Interlocked.Add(ref _durationMs, (long)ms);
    public void IncrementCalls() => Interlocked.Increment(ref _llmCalls);

    /// <summary>
    /// Formats a token count: max 3 digits, using K/M suffixes for thousands/millions.
    /// </summary>
    public static string Format(long tokens) => tokens switch
    {
        < 1_000 => $"{tokens}",
        < 10_000 => $"{tokens / 1_000.0:0.#}K",
        < 1_000_000 => $"{tokens / 1_000:0}K",
        < 10_000_000 => $"{tokens / 1_000_000.0:0.#}M",
        _ => $"{tokens / 1_000_000:0}M",
    };

    public static string FormatCost(double cost) => cost switch
    {
        0 => "-",
        < 0.01 => $"${cost:0.000}",
        < 1.0 => $"${cost:0.00}",
        _ => $"${cost:0.00}",
    };

    public static string FormatDuration(long ms) => ms switch
    {
        < 1_000 => $"{ms}ms",
        < 60_000 => $"{ms / 1_000.0:0.#}s",
        _ => $"{ms / 60_000.0:0.#}m",
    };
}
