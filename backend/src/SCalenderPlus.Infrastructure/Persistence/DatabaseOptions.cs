using System.ComponentModel.DataAnnotations;

namespace SCalenderPlus.Infrastructure.Persistence;

/// <summary>
/// Database settings: <c>ConnectionStrings__Default</c> plus <c>Database__*</c>.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public const string ConnectionStringName = "Default";

    /// <summary>Npgsql connection string (bound from <c>ConnectionStrings:Default</c>).</summary>
    [Required(ErrorMessage = "ConnectionStrings:Default is required.")]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Apply pending migrations when the api starts. Convenience for development only; production runs the
    /// one-shot <c>migrate</c> command before api/worker start.
    /// </summary>
    public bool AutoMigrate { get; set; }
}
