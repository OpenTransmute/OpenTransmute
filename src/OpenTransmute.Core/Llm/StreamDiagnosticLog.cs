using System.Text;

namespace OpenTransmute.Llm;

/// <summary>
/// Non-fatal diagnostic log for LLM executor sessions. Wraps a nullable <see cref="StreamWriter"/>
/// and swallows all I/O errors — stream log failures must never disrupt the LLM session.
/// <para>
/// Consolidates the identical resolve-path / create-directory / open-writer / try-catch-write /
/// flush-close logic that was duplicated across all three executors.
/// </para>
/// </summary>
public sealed class StreamDiagnosticLog : IDisposable
{
    #region Members

    private readonly StreamWriter? _writer;
    private readonly ILogger _logger;
    private readonly string _label;

    #endregion

    #region Constructor

    /// <summary>
    /// Private — use <see cref="Create"/> factory method.
    /// </summary>
    private StreamDiagnosticLog(StreamWriter? writer, string? filePath, ILogger logger, string label)
    {
        _writer = writer;
        FilePath = filePath;
        _logger = logger;
        _label = label;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Full path to the log file, or <c>null</c> if logging is disabled.
    /// </summary>
    public string? FilePath { get; }

    #endregion

    #region Methods

    /// <summary>
    /// Creates a diagnostic log for the given execution context. If no log directory can be
    /// determined, or the file cannot be created, returns a no-op instance that silently
    /// discards all writes.
    /// </summary>
    /// <param name="ctx">Execution context — provides log directory, output file path, and working directory.</param>
    /// <param name="executorLabel">Short executor identifier for the filename (e.g. "copilot", "claude", "openai").</param>
    /// <param name="logger">ILogger for reporting creation failures and close errors.</param>
    public static StreamDiagnosticLog Create(LlmExecutionContext ctx, string executorLabel, ILogger logger)
    {
        string? dir = ctx.LogDirectory;

        if (dir is null)
        {
            if (ctx.OutputFilePath is not null)
                dir = Path.GetDirectoryName(ctx.OutputFilePath);
            else if (ctx.WorkingDirectory is not null)
                dir = ctx.WorkingDirectory;
        }

        if (dir is null)
            return new StreamDiagnosticLog(null, null, logger, executorLabel);

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string label = ctx.PhaseLabel ?? executorLabel;
        string path = Path.Combine(dir, $"{label}-{executorLabel}-{timestamp}.log");

        try
        {
            Directory.CreateDirectory(dir);
            StreamWriter writer = new StreamWriter(path, append: false, encoding: Encoding.UTF8) { AutoFlush = true };
            logger.LogInformation("[{Label}] Stream log: {Path}", executorLabel.ToUpperInvariant(), path);
            return new StreamDiagnosticLog(writer, path, logger, executorLabel);
        }
        catch (Exception ex)
        {
            // Non-fatal — executor continues without a stream log.
            logger.LogWarning("[{Label}] Failed to create stream log at {Path}: {Message}",
                executorLabel.ToUpperInvariant(), path, ex.Message);
            return new StreamDiagnosticLog(null, null, logger, executorLabel);
        }
    }

    /// <summary>
    /// Writes a line to the log. No-op if logging is disabled.
    /// </summary>
    public void WriteLine(string line)
    {
        try { _writer?.WriteLine(line); }
        catch { /* non-fatal */ }
    }

    /// <summary>
    /// Writes raw text without a trailing newline — used for streaming delta chunks
    /// that should appear inline in the log.
    /// </summary>
    public void Write(string text)
    {
        try { _writer?.Write(text); }
        catch { /* non-fatal */ }
    }

    /// <summary>
    /// Writes the standard session configuration header. Common fields are written automatically;
    /// executor-specific fields are passed as extras.
    /// </summary>
    /// <param name="ctx">Execution context with the common configuration fields.</param>
    /// <param name="extras">Additional key-value pairs specific to the executor (e.g. "Args", "IdleTimeoutSec").</param>
    public void WriteHeader(LlmExecutionContext ctx, params (string Key, string? Value)[] extras)
    {
        if (_writer is null) return;

        try
        {
            _writer.WriteLine("=== SESSION CONFIG ===");
            _writer.WriteLine($"  Model: {ctx.Model ?? "(default)"}");
            _writer.WriteLine($"  Cwd: {ctx.WorkingDirectory ?? "(null)"}");
            _writer.WriteLine($"  MaxOutputTokens: {ctx.MaxOutputTokens}");
            _writer.WriteLine($"  Timeout: {ctx.Timeout}");
            _writer.WriteLine($"  EnableFileTools: {ctx.EnableFileTools}");
            _writer.WriteLine($"  EnableReadOnlyFileTools: {ctx.EnableReadOnlyFileTools}");
            _writer.WriteLine($"  OutputFilePath: {ctx.OutputFilePath ?? "(null)"}");
            _writer.WriteLine($"  EnableAppendResultsTool: {ctx.EnableAppendResultsTool}");
            _writer.WriteLine($"  PhaseLabel: {ctx.PhaseLabel ?? "(null)"}");

            foreach ((string key, string? value) in extras)
                _writer.WriteLine($"  {key}: {value ?? "(null)"}");

            _writer.WriteLine();
        }
        catch { /* non-fatal */ }
    }

    /// <summary>
    /// Truncates a string for diagnostic logging, appending an ellipsis when cut.
    /// </summary>
    public static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return value[..maxLength] + "…";
    }

    /// <summary>
    /// Flushes and closes the underlying writer.
    /// </summary>
    public void Dispose()
    {
        if (_writer is null) return;
        try
        {
            _writer.Flush();
            _writer.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[{Label}] Stream log close error: {Message}",
                _label.ToUpperInvariant(), ex.Message);
        }
    }

    #endregion
}
