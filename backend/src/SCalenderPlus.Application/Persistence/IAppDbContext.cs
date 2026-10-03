namespace SCalenderPlus.Application.Persistence;

/// <summary>
/// The unit of work of a use case (implemented by the EF Core <c>AppDbContext</c>): changes staged by
/// services such as <c>IJobScheduler.Enqueue</c> or <c>IAuditLog.Record</c> are committed together, in one
/// transaction, by <see cref="SaveChangesAsync"/>. Entity sets are added here as the domain grows.
/// </summary>
public interface IAppDbContext
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> in one database transaction, for use cases whose collaborators save on
    /// their own (e.g. ASP.NET Core Identity's user manager): every <see cref="SaveChangesAsync"/> inside commits
    /// only when the operation completes; an exception rolls all of them back.
    /// </summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}
