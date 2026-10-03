using System.Net;

namespace SCalenderPlus.IntegrationTests.Authorization;

/// <summary>
/// One cell of the matrix: <see cref="Actor"/> calls <see cref="Operation"/> and must get <see cref="Expected"/>.
/// Route values (<c>{id}</c> → a seeded resource) and the request body come from the scenario.
/// </summary>
public sealed record MatrixCase(
    ApiOperation Operation,
    MatrixActor Actor,
    HttpStatusCode Expected,
    Func<MatrixScenario, IReadOnlyDictionary<string, string>>? RouteValues = null,
    Func<MatrixScenario, HttpContent?>? Body = null)
{
    /// <summary>Stable, human-readable id; also the theory data row.</summary>
    public string Id => $"{Operation} as {Actor.Name} -> {(int)Expected}";

    public override string ToString() => Id;
}

/// <summary>An operation that is deliberately public (no authentication, no permission check).</summary>
public sealed record AnonymousOperation(ApiOperation Operation, string Reason);
