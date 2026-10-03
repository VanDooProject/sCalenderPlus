using System.Net;

namespace SCalenderPlus.IntegrationTests.Authorization;

/// <summary>
/// The authorization matrix (workflow.md §6): every operation of the OpenAPI document is either listed in
/// <see cref="AnonymousOperations"/> with a reason, or has <see cref="Cases"/> — at least
/// "anonymous → 401", plus "cross-tenant → 404" when the path has parameters, plus one case per relevant
/// actor/level. <see cref="AuthorizationMatrixCoverageTests"/> fails for any operation without an entry, and
/// <see cref="AuthorizationMatrixTests"/> executes every case against the real api and PostgreSQL.
/// See README.md in this folder for how to add an endpoint.
/// </summary>
public static class AuthorizationMatrix
{
    public static IReadOnlyList<AnonymousOperation> AnonymousOperations { get; } =
    [
        Anonymous("GET", "/health/live", "Container liveness probe; status only."),
        Anonymous("GET", "/health/ready", "Container readiness probe; check names and statuses only."),
    ];

    public static IReadOnlyList<MatrixCase> Cases { get; } =
    [
        // Add cases per operation, grouped by feature, e.g.
        //   .. For("GET", "/api/v1/me")
        //       .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
        //       .Expect(Actors.User, HttpStatusCode.OK),
    ];

    private static AnonymousOperation Anonymous(string method, string path, string reason) =>
        new(new ApiOperation(method, path), reason);

    /// <summary>Starts the cases of one operation: <c>.. For("GET", "/api/v1/groups/{id}").Expect(...)</c>.</summary>
    internal static OperationCases For(string method, string path) => new(new ApiOperation(method, path));
}

/// <summary>Fluent builder for the cases of one operation; enumerate it into <see cref="AuthorizationMatrix.Cases"/>.</summary>
internal sealed class OperationCases(ApiOperation operation) : IEnumerable<MatrixCase>
{
    private readonly List<MatrixCase> _cases = [];
    private Func<MatrixScenario, IReadOnlyDictionary<string, string>>? _routeValues;
    private Func<MatrixScenario, HttpContent?>? _body;

    /// <summary>Route values for every following case, e.g. <c>s => new() { ["id"] = s.Get("group:lions") }</c>.</summary>
    public OperationCases WithRoute(Func<MatrixScenario, IReadOnlyDictionary<string, string>> routeValues)
    {
        _routeValues = routeValues;
        return this;
    }

    /// <summary>A valid request body for every following case (authorization must be checked before validation).</summary>
    public OperationCases WithBody(Func<MatrixScenario, HttpContent?> body)
    {
        _body = body;
        return this;
    }

    public OperationCases Expect(MatrixActor actor, HttpStatusCode expected)
    {
        _cases.Add(new MatrixCase(operation, actor, expected, _routeValues, _body));
        return this;
    }

    public IEnumerator<MatrixCase> GetEnumerator() => _cases.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
