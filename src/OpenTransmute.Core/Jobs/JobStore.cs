using System.Collections.Concurrent;

namespace OpenTransmute.Jobs;

/// <summary>
/// In-memory store for active and recently-completed jobs.
/// Thread-safe — backed by ConcurrentDictionary.
/// </summary>
public class JobStore
{
    #region Members

    private readonly ConcurrentDictionary<Guid, DecomposeJob> _decompose = new();
    private readonly ConcurrentDictionary<Guid, ComposeJob> _compose = new();
    private readonly ConcurrentDictionary<Guid, ImplementJob> _implement = new();
    private readonly ConcurrentDictionary<Guid, VerifyJob> _verify = new();

    #endregion

    #region Properties

    /// <summary>All decompose jobs ordered by creation time, newest first.</summary>
    public IEnumerable<DecomposeJob> AllDecomposeJobs =>
        _decompose.Values.OrderByDescending(j => j.CreatedAt);

    /// <summary>All compose jobs ordered by creation time, newest first.</summary>
    public IEnumerable<ComposeJob> AllComposeJobs =>
        _compose.Values.OrderByDescending(j => j.CreatedAt);

    /// <summary>All implement jobs ordered by creation time, newest first.</summary>
    public IEnumerable<ImplementJob> AllImplementJobs =>
        _implement.Values.OrderByDescending(j => j.CreatedAt);

    /// <summary>All verify jobs ordered by creation time, newest first.</summary>
    public IEnumerable<VerifyJob> AllVerifyJobs =>
        _verify.Values.OrderByDescending(j => j.CreatedAt);

    #endregion

    #region Methods

    /// <summary>Registers a decompose job in the store.</summary>
    public void Add(DecomposeJob job) => _decompose[job.Id] = job;

    /// <summary>Registers a compose job in the store.</summary>
    public void Add(ComposeJob job) => _compose[job.Id] = job;

    /// <summary>Registers a implement job in the store.</summary>
    public void Add(ImplementJob job) => _implement[job.Id] = job;

    /// <summary>Registers a verify job in the store.</summary>
    public void Add(VerifyJob job) => _verify[job.Id] = job;

    /// <summary>Returns the decompose job with the given ID, or null if not found.</summary>
    public DecomposeJob? GetDecompose(Guid id) => _decompose.GetValueOrDefault(id);

    /// <summary>Returns the compose job with the given ID, or null if not found.</summary>
    public ComposeJob? GetCompose(Guid id) => _compose.GetValueOrDefault(id);

    /// <summary>Returns the implement job with the given ID, or null if not found.</summary>
    public ImplementJob? GetImplement(Guid id) => _implement.GetValueOrDefault(id);

    /// <summary>Returns the verify job with the given ID, or null if not found.</summary>
    public VerifyJob? GetVerify(Guid id) => _verify.GetValueOrDefault(id);

    #endregion
}
