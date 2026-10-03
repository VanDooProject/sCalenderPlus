using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

[assembly: AssemblyFixture(typeof(SCalenderPlus.IntegrationTests.Infrastructure.MailpitFixture))]

namespace SCalenderPlus.IntegrationTests.Infrastructure;

/// <summary>
/// Mailpit (SMTP sink with an HTTP API) as a Testcontainer, started lazily on first use like
/// <see cref="PostgresFixture"/>; same image as deploy/docker-compose.dev.yml uses for local development.
/// </summary>
public sealed class MailpitFixture : IAsyncDisposable
{
    public const string Image = "axllent/mailpit:v1.31.4";
    private const int SmtpPort = 1025;
    private const int HttpPort = 8025;

    private readonly SemaphoreSlim _startLock = new(1, 1);
    private IContainer? _container;
    private HttpClient? _http;

    public async Task<(string Host, int Port)> SmtpEndpointAsync()
    {
        var container = await StartAsync();
        return (container.Hostname, container.GetMappedPublicPort(SmtpPort));
    }

    /// <summary>Messages currently in the mailbox, newest first.</summary>
    public async Task<IReadOnlyList<MailpitMessage>> MessagesAsync(CancellationToken cancellationToken)
    {
        await StartAsync();
        var list = await _http!.GetFromJsonAsync<MailpitList>("api/v1/messages", cancellationToken);
        return list?.Messages ?? [];
    }

    public async Task<MailpitMessageDetail> MessageAsync(string id, CancellationToken cancellationToken)
    {
        await StartAsync();
        return (await _http!.GetFromJsonAsync<MailpitMessageDetail>($"api/v1/message/{id}", cancellationToken))!;
    }

    private async Task<IContainer> StartAsync()
    {
        Assert.SkipWhen(PostgresFixture.SkipRequested, $"Docker-based test skipped ({PostgresFixture.SkipEnvironmentVariable} is set).");

        await _startLock.WaitAsync();
        try
        {
            if (_container is null)
            {
                var container = new ContainerBuilder(Image)
                    .WithPortBinding(SmtpPort, assignRandomHostPort: true)
                    .WithPortBinding(HttpPort, assignRandomHostPort: true)
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(HttpPort).ForPath("/readyz")))
                    .Build();
                await container.StartAsync();
                _http = new HttpClient { BaseAddress = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(HttpPort)}/") };
                _container = container;
            }

            return _container;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        _startLock.Dispose();
    }
}

public sealed record MailpitList([property: JsonPropertyName("messages")] IReadOnlyList<MailpitMessage> Messages);

public sealed record MailpitAddress([property: JsonPropertyName("Name")] string Name, [property: JsonPropertyName("Address")] string Address);

public sealed record MailpitMessage(
    [property: JsonPropertyName("ID")] string Id,
    [property: JsonPropertyName("Subject")] string Subject,
    [property: JsonPropertyName("From")] MailpitAddress From,
    [property: JsonPropertyName("To")] IReadOnlyList<MailpitAddress> To);

public sealed record MailpitMessageDetail(
    [property: JsonPropertyName("Subject")] string Subject,
    [property: JsonPropertyName("Text")] string Text,
    [property: JsonPropertyName("HTML")] string Html);
