using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SCalenderPlus.Application.Email;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.IntegrationTests.Infrastructure;
using ApiProgram = SCalenderPlus.Api.Program;
using WorkerProgram = SCalenderPlus.Worker.Program;

namespace SCalenderPlus.IntegrationTests.Email;

/// <summary>Issue #27: an email queued by the api is delivered by the worker over SMTP (Mailpit).</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class EmailDeliveryTests(PostgresFixture postgres, MailpitFixture mailpit)
{
    [Fact]
    public async Task Email_queued_in_the_api_arrives_in_mailpit()
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = TestSettings.For(await postgres.CreateDatabaseAsync(), autoMigrate: true);
        var (host, port) = await mailpit.SmtpEndpointAsync();
        settings["Smtp:Host"] = host;
        settings["Smtp:Port"] = port.ToString(CultureInfo.InvariantCulture);
        settings["Jobs:PollInterval"] = "00:00:00.050";
        var recipient = $"mia-{Guid.NewGuid():N}@example.test";

        await using var api = new HostFactory<ApiProgram>(settings);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var message = new EmailTemplate(
                "Welcome to sCalenderPlus",
                ["Hi Mia,", "please confirm your <email> address."],
                new EmailAction("Confirm email", new Uri("https://app.example.test/confirm?token=abc&x=1"))).Render(recipient, "Mia");
            scope.ServiceProvider.GetRequiredService<IEmailOutbox>().Queue(message);
            await scope.ServiceProvider.GetRequiredService<IAppDbContext>().SaveChangesAsync(ct);
        }

        await using var worker = new HostFactory<WorkerProgram>(settings);
        _ = worker.Services;

        var delivered = await WaitForMessageAsync(recipient, ct);
        Assert.Equal("Welcome to sCalenderPlus", delivered.Subject);
        Assert.Equal("noreply@scalenderplus.test", delivered.From.Address);
        Assert.Equal("Mia", delivered.To.Single().Name);

        var detail = await mailpit.MessageAsync(delivered.Id, ct);
        Assert.Contains("please confirm your <email> address.", detail.Text, StringComparison.Ordinal);
        Assert.Contains("Confirm email: https://app.example.test/confirm?token=abc&x=1", detail.Text, StringComparison.Ordinal);
        Assert.Contains("please confirm your &lt;email&gt; address.", detail.Html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://app.example.test/confirm?token=abc&amp;x=1\"", detail.Html, StringComparison.Ordinal);
    }

    private async Task<MailpitMessage> WaitForMessageAsync(string recipient, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var message = (await mailpit.MessagesAsync(ct)).FirstOrDefault(m => m.To.Any(t => t.Address == recipient));
            if (message is not null)
            {
                return message;
            }

            await Task.Delay(200, ct);
        }

        throw new TimeoutException($"No email to {recipient} arrived in Mailpit.");
    }
}
