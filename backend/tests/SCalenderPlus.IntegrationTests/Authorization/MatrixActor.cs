namespace SCalenderPlus.IntegrationTests.Authorization;

/// <summary>
/// Who sends a matrix request. An actor knows how to build an <see cref="HttpClient"/> for its identity in a
/// seeded <see cref="MatrixScenario"/> (e.g. sign in and keep the cookie). Actors are the rows of the
/// permission vocabulary (docs/architecture/permissions.md): anonymous, a signed-in user, group roles,
/// calendar/event levels, and a user from another tenant.
/// </summary>
public sealed class MatrixActor
{
    private readonly Func<MatrixScenario, Task<HttpClient>> _createClient;

    public MatrixActor(string name, Func<MatrixScenario, Task<HttpClient>> createClient, bool isCrossTenant = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        _createClient = createClient;
        IsCrossTenant = isCrossTenant;
    }

    public string Name { get; }

    /// <summary>
    /// A user who is valid but has no relation to the resources the route values point to: protected
    /// operations with path parameters must answer them 404 (tenant isolation, workflow.md §6).
    /// </summary>
    public bool IsCrossTenant { get; }

    public Task<HttpClient> CreateClientAsync(MatrixScenario scenario) => _createClient(scenario);

    public override string ToString() => Name;
}

/// <summary>
/// The actors known to the matrix. Add new ones here as features land (M1-B: signed-in and unverified users,
/// cross-tenant user; M1-C: group owner/admin/member/non-member; M2: calendar and event levels).
/// </summary>
public static class Actors
{
    public static readonly MatrixActor Anonymous = new("anonymous", scenario => Task.FromResult(scenario.CreateAnonymousClient()));
}
