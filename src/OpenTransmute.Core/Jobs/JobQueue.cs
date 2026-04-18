using System.Threading.Channels;

namespace OpenTransmute.Jobs;

/// <summary>Shared unbounded channel carrying both decompose and compose jobs.</summary>
public class JobQueue
{
    #region Members

    private readonly Channel<object> _channel = Channel.CreateUnbounded<object>(
        new UnboundedChannelOptions { SingleReader = true });

    #endregion

    #region Methods

    /// <summary>Enqueues a decompose job for processing.</summary>
    public ValueTask EnqueueAsync(DecomposeJob job, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(job, ct);

    /// <summary>Enqueues a compose job for processing.</summary>
    public ValueTask EnqueueAsync(ComposeJob job, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(job, ct);

    /// <summary>Enqueues an implement job for processing.</summary>
    public ValueTask EnqueueAsync(ImplementJob job, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(job, ct);

    /// <summary>Reads all jobs from the channel as an async enumerable.</summary>
    public IAsyncEnumerable<object> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);

    #endregion
}
