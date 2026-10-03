using SCalenderPlus.Application;
using SCalenderPlus.Infrastructure;
using SCalenderPlus.Infrastructure.Hosting;

namespace SCalenderPlus.Api;

public sealed class Program
{
    private Program()
    {
    }

    public static async Task<int> Main(string[] args)
    {
        if (args is [MigrateCommand.Name, .. var migrateArgs])
        {
            return await MigrateCommand.RunAsync(migrateArgs).ConfigureAwait(false);
        }

        var builder = WebApplication.CreateBuilder(args);

        builder.Services
            .AddApplication(builder.Configuration)
            .AddInfrastructure(builder.Configuration)
            .AddDatabaseAutoMigration();

        var app = builder.Build();

        return await HostRunner.RunAsync(app).ConfigureAwait(false);
    }
}
