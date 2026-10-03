using Npgsql;
using SCalenderPlus.Infrastructure.Persistence;

namespace SCalenderPlus.IntegrationTests.Persistence;

/// <summary>Connection string defaults applied before Npgsql sees the configured value. No Docker needed.</summary>
public sealed class ConnectionStringDefaultsTests
{
    [Fact]
    public void Disables_gss_encryption_by_default_and_keeps_everything_else()
    {
        var result = new NpgsqlConnectionStringBuilder(
            DbContextOptionsConfiguration.WithDefaults("Host=db;Database=scal;Username=scal;Password=s3cret;Maximum Pool Size=50"));

        Assert.Equal(GssEncryptionMode.Disable, result.GssEncryptionMode);
        Assert.Equal("db", result.Host);
        Assert.Equal("s3cret", result.Password);
        Assert.Equal(50, result.MaxPoolSize);
    }

    [Theory]
    [InlineData("Host=db;Gss Encryption Mode=Require")]
    [InlineData("Host=db;GssEncryptionMode=Prefer")]
    public void Keeps_an_explicit_gss_encryption_mode(string connectionString) =>
        Assert.Equal(connectionString, DbContextOptionsConfiguration.WithDefaults(connectionString));

    [Theory]
    [InlineData("")]
    [InlineData("this is not a connection string")]
    public void Leaves_empty_or_malformed_values_to_validation_and_npgsql(string connectionString) =>
        Assert.Equal(connectionString, DbContextOptionsConfiguration.WithDefaults(connectionString));
}
