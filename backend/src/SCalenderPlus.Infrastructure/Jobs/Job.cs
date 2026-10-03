using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;

namespace SCalenderPlus.Infrastructure.Jobs;

/// <summary>
/// A row of the <c>jobs</c> table (docs/architecture/data-model.md §8). Life cycle: pending
/// (<see cref="LockedUntil"/> null or expired) → running (leased by <see cref="LockedBy"/> until
/// <see cref="LockedUntil"/>) → deleted on success, or back to pending with a later <see cref="RunAt"/>
/// (retry), or dead-lettered (<see cref="DeadAt"/> set) after <see cref="MaxAttempts"/>.
/// </summary>
internal sealed class Job
{
    public Guid Id { get; set; }

    public required string Type { get; set; }

    /// <summary>JSON (<c>jsonb</c>).</summary>
    public required string Payload { get; set; }

    public Instant RunAt { get; set; }

    /// <summary>Attempts started so far (incremented when claimed, so crashed attempts count too).</summary>
    public int Attempts { get; set; }

    public int MaxAttempts { get; set; }

    public string? LockedBy { get; set; }

    public Instant? LockedUntil { get; set; }

    public string? LastError { get; set; }

    public string? DedupeKey { get; set; }

    public Instant CreatedAt { get; set; }

    /// <summary>Dead-letter marker: the job failed permanently or ran out of attempts. Kept for inspection.</summary>
    public Instant? DeadAt { get; set; }
}

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.Type).HasMaxLength(100);
        builder.Property(j => j.Payload).HasColumnType("jsonb");
        builder.Property(j => j.LockedBy).HasMaxLength(200);
        builder.Property(j => j.DedupeKey).HasMaxLength(200);

        // Claim query: due, not dead jobs ordered by run_at.
        builder.HasIndex(j => j.RunAt).HasFilter("dead_at IS NULL").HasDatabaseName("ix_jobs_due");

        // At most one pending/running job per dedupe key; dead jobs don't block new ones.
        builder.HasIndex(j => j.DedupeKey).IsUnique().HasFilter("dedupe_key IS NOT NULL AND dead_at IS NULL");
    }
}
