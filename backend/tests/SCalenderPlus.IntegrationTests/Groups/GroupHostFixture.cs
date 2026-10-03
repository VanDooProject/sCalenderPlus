using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SCalenderPlus.Application.Groups;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Infrastructure;

namespace SCalenderPlus.IntegrationTests.Groups;

/// <summary>
/// One api host per group test class (tests of a class run one after another and create their own users and
/// groups), with a membership observer that records every change.
/// </summary>
public sealed class GroupHostFixture(PostgresFixture postgres) : IAsyncLifetime
{
    internal ApiTestHost Host { get; private set; } = null!;

    public RecordingObserver Observer { get; } = new();

    public async ValueTask InitializeAsync() =>
        Host = await ApiTestHost.StartAsync(postgres, services: s => s.AddSingleton<IGroupMembershipObserver>(Observer));

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();

    public sealed class RecordingObserver : IGroupMembershipObserver
    {
        public ConcurrentQueue<MembershipChange> Changes { get; } = new();

        public Task OnMembershipChangedAsync(MembershipChange change, CancellationToken cancellationToken = default)
        {
            Changes.Enqueue(change);
            return Task.CompletedTask;
        }
    }
}
