using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;

namespace SCalenderPlus.Infrastructure.Auditing;

/// <summary>A row of <c>audit_events</c> (docs/architecture/data-model.md §8). Append-only.</summary>
internal sealed class AuditEvent
{
    public Guid Id { get; set; }

    public Instant At { get; set; }

    /// <summary><c>user</c>, <c>anonymous</c> or <c>system</c>.</summary>
    public required string ActorKind { get; set; }

    public Guid? ActorUserId { get; set; }

    /// <summary>Billing subject (user or group) for plan-based retention.</summary>
    public Guid? SubjectId { get; set; }

    public required string ResourceType { get; set; }

    public required string ResourceId { get; set; }

    public required string Action { get; set; }

    /// <summary>JSON (<c>jsonb</c>), secrets redacted.</summary>
    public string? Before { get; set; }

    public string? After { get; set; }

    /// <summary><c>inet</c>.</summary>
    public IPAddress? Ip { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>W3C trace id of the request or job.</summary>
    public string? CorrelationId { get; set; }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public const int UserAgentMaxLength = 512;

    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.ActorKind).HasMaxLength(20);
        builder.Property(e => e.ResourceType).HasMaxLength(50);
        builder.Property(e => e.ResourceId).HasMaxLength(100);
        builder.Property(e => e.Action).HasMaxLength(100);
        builder.Property(e => e.Before).HasColumnType("jsonb");
        builder.Property(e => e.After).HasColumnType("jsonb");
        builder.Property(e => e.Ip).HasColumnType("inet");
        builder.Property(e => e.UserAgent).HasMaxLength(UserAgentMaxLength);
        builder.Property(e => e.CorrelationId).HasMaxLength(64);

        builder.HasIndex(e => new { e.ResourceType, e.ResourceId, e.At });
        builder.HasIndex(e => new { e.SubjectId, e.At });
        builder.HasIndex(e => new { e.ActorUserId, e.At });
    }
}
