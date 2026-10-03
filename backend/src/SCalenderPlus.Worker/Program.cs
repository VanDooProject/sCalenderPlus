using SCalenderPlus.Application;
using SCalenderPlus.Infrastructure;
using SCalenderPlus.Infrastructure.Hosting;

namespace SCalenderPlus.Worker;

public sealed class Program
{
    private Program()
    {
    }

    public static async Task<int> Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services
            .AddApplication(builder.Configuration)
            .AddInfrastructure(builder.Configuration);

        builder.Services.AddHostedService<JobLoopService>();

        var app = builder.Build();

        return await HostRunner.RunAsync(app).ConfigureAwait(false);
    }
}
