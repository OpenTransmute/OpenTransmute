namespace OpenTransmute.Jobs;

public interface IJobPersistenceService<TJob> where TJob : JobBase
{
    Task SaveAsync(TJob job, CancellationToken ct = default);
    Task<List<TJob>> LoadAllAsync(CancellationToken ct = default);
}
