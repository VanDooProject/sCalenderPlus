namespace SCalenderPlus.Application.Persistence;

/// <summary>
/// The unit of work of a use case (implemented by the EF Core <c>AppDbContext</c>): changes staged by
/// services such as <c>IJobScheduler.Enqueue</c> or <c>IAuditLog.Record</c> are committed together, in one
/// transaction, by <see cref="SaveChangesAsync"/>. Entity sets are added here as the domain grows.
/// </summary>
public interface IAppDbContext
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
