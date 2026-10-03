using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Jobs;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Infrastructure.Auditing;
using SCalenderPlus.Infrastructure.Jobs;
using SCalenderPlus.Infrastructure.Persistence;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Jobs;
using ApiProgram = SCalenderPlus.Api.Program;

namespace SCalenderPlus.IntegrationTests.Auditing;

/// <summary>Issue #32: audit events are written in the same transaction as the mutation they describe.</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class AuditLogTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record GroupState(string Name, string InviteLinkToken);

    [Fact]
    public async Task Sample_mutation_produces_an_audit_row_with_before_and_after()
    {
        await using var host = await JobTestHost.StartAsync(await postgres.CreateDatabaseAsync(), SystemClock.Instance);
        var groupId = Guid.CreateVersion7().ToString();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            // The "mutation": a change that also enqueues a notification job, plus its audit event, in one unit of work.
            scope.ServiceProvider.GetRequiredService<IJobScheduler>().Enqueue("group.renamed.notify", new { groupId });
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
                "group.renamed", "group", groupId, new GroupState("Lions", "inv_1"), new GroupState("Lions FC", "inv_1"));
            await scope.ServiceProvider.GetRequiredService<IAppDbContext>().SaveChangesAsync(Ct);
        }

        var row = await SingleAuditEventAsync(host.Services);
        Assert.Equal("group.renamed", row.Action);
        Assert.Equal("group", row.ResourceType);
        Assert.Equal(groupId, row.ResourceId);
        Assert.Equal("system", row.ActorKind);
        Assert.Null(row.ActorUserId);
        Assert.InRange(SystemClock.Instance.GetCurrentInstant() - row.At, Duration.Zero, Duration.FromMinutes(1));
        Assert.Equal("Lions", (string?)JsonNode.Parse(row.Before!)!["name"]);
        Assert.Equal("Lions FC", (string?)JsonNode.Parse(row.After!)!["name"]);
        Assert.Equal(AuditSnapshot.Redacted, (string?)JsonNode.Parse(row.After!)!["inviteLinkToken"]);
        Assert.Equal(1, await host.CountAsync());
    }

    [Fact]
    public async Task Audit_event_is_not_written_when_the_mutation_fails()
    {
        await using var host = await JobTestHost.StartAsync(await postgres.CreateDatabaseAsync(), SystemClock.Instance);
        var existing = await host.EnqueueAsync("test.job", new { });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record("group.deleted", "group", "g1", new { Name = "Lions" }, null);
            db.Set<Job>().Add(new Job { Id = existing, Type = "duplicate", Payload = "{}" }); // violates the primary key

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }

        await using var verify = host.Services.CreateAsyncScope();
        Assert.Equal(0, await verify.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditEvent>().CountAsync(Ct));
    }

    [Fact]
    public async Task Audit_event_of_an_api_request_records_client_address_user_agent_and_trace_id()
    {
        var handlers = new Dictionary<string, RequestDelegate>(StringComparer.Ordinal)
        {
            ["/mutate"] = async context =>
            {
                context.RequestServices.GetRequiredService<IAuditLog>().Record("group.created", "group", "g2", null, new { Name = "Lions" });
                await context.RequestServices.GetRequiredService<IAppDbContext>().SaveChangesAsync(context.RequestAborted);
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            },
        };
        var settings = TestSettings.For(await postgres.CreateDatabaseAsync(), autoMigrate: true);
        await using var api = new HostFactory<ApiProgram>(settings, services => TestPipeline.Add(services, handlers));
        using var client = api.CreateClient();
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/__test/mutate", UriKind.Relative));
        request.Headers.Add(TestPipeline.RemoteIpHeader, "127.0.0.1");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        request.Headers.Add("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");
        request.Headers.UserAgent.ParseAdd("sCalenderPlusTest/1.0");
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var row = await SingleAuditEventAsync(api.Services);
        Assert.Equal("anonymous", row.ActorKind);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), row.Ip);
        Assert.Equal("sCalenderPlusTest/1.0", row.UserAgent);
        Assert.Equal(traceId, row.CorrelationId);
        Assert.Null(row.Before);
        Assert.Equal("Lions", (string?)JsonNode.Parse(row.After!)!["name"]);
    }

    private static async Task<AuditEvent> SingleAuditEventAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditEvent>().AsNoTracking().SingleAsync(Ct);
    }
}
