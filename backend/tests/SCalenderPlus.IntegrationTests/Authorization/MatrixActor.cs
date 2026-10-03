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
/// The actors known to the matrix. On the group calendar "lions" (default role defaults) the group roles are the
/// calendar levels: <see cref="GroupOwner"/> owner, <see cref="GroupAdmin"/> manage, <see cref="GroupMember"/>
/// contribute, <see cref="GroupViewer"/> read; <see cref="CalendarEditor"/> edit and <see cref="CalendarFreeBusy"/>
/// free_busy through user grants; <see cref="NonMember"/> and <see cref="OtherTenant"/> none. Each signed-in actor is a seeded user (email <c>{name}@matrix.example.test</c>) with its own session
/// (<see cref="MatrixScenario"/>); add new ones here as features land (M2: calendar and event levels).
/// </summary>
public static class Actors
{
    public static readonly MatrixActor Anonymous = new("anonymous", scenario => Task.FromResult(scenario.CreateAnonymousClient()));

    /// <summary>A signed-in user with a confirmed email address.</summary>
    public static readonly MatrixActor User = new("user", scenario => Task.FromResult(scenario.SessionClient("user")));

    /// <summary>A signed-in user who has not confirmed the email address (api.md §3 restrictions).</summary>
    public static readonly MatrixActor UnverifiedUser = new("unverified-user", scenario => Task.FromResult(scenario.SessionClient("unverified-user")));

    /// <summary>A verified user of another tenant: shares nothing with the seeded resources (expects 404 on them).</summary>
    public static readonly MatrixActor OtherUser = new("other-user", scenario => Task.FromResult(scenario.SessionClient("other-user")), isCrossTenant: true);

    /// <summary>A verified user signed in anew for every case: for operations that end or replace the session (logout).</summary>
    public static readonly MatrixActor FreshSession = new("fresh-session", scenario => scenario.NewSessionClientAsync("fresh-session"));

    /// <summary>A verified user with two-factor authentication enabled, fully signed in.</summary>
    public static readonly MatrixActor TwoFactorUser = new("two-factor-user", scenario => Task.FromResult(scenario.SessionClient("two-factor-user")));

    /// <summary>A 2FA user who passed the password step only: holds the pending-login cookie, not a session.</summary>
    public static readonly MatrixActor TwoFactorPending = new("two-factor-pending", scenario => scenario.PendingSecondFactorClientAsync("two-factor-pending"));

    /// <summary>Owner (and billing owner) of the seeded group "lions" (and of "doomed", which a case deletes).</summary>
    public static readonly MatrixActor GroupOwner = new("group-owner", scenario => Task.FromResult(scenario.SessionClient("group-owner")));

    /// <summary>Admin of "lions".</summary>
    public static readonly MatrixActor GroupAdmin = new("group-admin", scenario => Task.FromResult(scenario.SessionClient("group-admin")));

    /// <summary>Member (role <c>member</c>) of "lions".</summary>
    public static readonly MatrixActor GroupMember = new("group-member", scenario => Task.FromResult(scenario.SessionClient("group-member")));

    /// <summary>Viewer of "lions".</summary>
    public static readonly MatrixActor GroupViewer = new("group-viewer", scenario => Task.FromResult(scenario.SessionClient("group-viewer")));

    /// <summary>Holds a user grant <c>edit</c> on the calendar "lions" (in no group).</summary>
    public static readonly MatrixActor CalendarEditor = new("calendar-editor", scenario => Task.FromResult(scenario.SessionClient("calendar-editor")));

    /// <summary>Holds a user grant <c>free_busy</c> on the calendar "lions" (in no group): sees it, nothing more.</summary>
    public static readonly MatrixActor CalendarFreeBusy = new("calendar-free-busy", scenario => Task.FromResult(scenario.SessionClient("calendar-free-busy")));

    /// <summary>A verified user in no group: group routes must answer 404 (not 403) — no existence leaks.</summary>
    public static readonly MatrixActor NonMember = new("non-member", scenario => Task.FromResult(scenario.SessionClient("non-member")));

    /// <summary>Owner of another tenant's group ("other"): owning a group elsewhere grants nothing on "lions" (404).</summary>
    public static readonly MatrixActor OtherTenant = new("other-tenant", scenario => Task.FromResult(scenario.SessionClient("other-tenant")), isCrossTenant: true);
}
