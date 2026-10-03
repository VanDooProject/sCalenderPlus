using System.Net;

namespace SCalenderPlus.IntegrationTests.Authorization;

/// <summary>
/// One cell of the matrix: <see cref="Actor"/> calls <see cref="Operation"/> and must get <see cref="Expected"/>.
/// Route values (<c>{id}</c> → a seeded resource), the request body and extra headers (e.g. <c>If-Match</c>)
/// come from the scenario; <see cref="Query"/> is a query string (without <c>?</c>) for operations with required
/// query parameters. Error statuses must also carry the expected problem <c>code</c>
/// (<see cref="ExpectedProblemCode"/>), so a 403 from a missing CSRF header or a 404 from a mistyped route can't
/// pass for a permission decision.
/// </summary>
public sealed record MatrixCase(
    ApiOperation Operation,
    MatrixActor Actor,
    HttpStatusCode Expected,
    Func<MatrixScenario, IReadOnlyDictionary<string, string>>? RouteValues = null,
    Func<MatrixScenario, HttpContent?>? Body = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? Code = null,
    string? Query = null)
{
    /// <summary>Stable, human-readable id; also the theory data row.</summary>
    public string Id => $"{Operation} as {Actor.Name} -> {(int)Expected}";

    /// <summary>
    /// The problem <c>code</c> an error response must have: <see cref="Code"/>, else the code of a refused
    /// authorization for the status (401 <c>unauthenticated</c>, 403 <c>insufficient_permission</c>, 404
    /// <c>not_found</c>, 409 <c>conflict</c>); null for success statuses.
    /// </summary>
    public string? ExpectedProblemCode => Code ?? Expected switch
    {
        HttpStatusCode.Unauthorized => "unauthenticated",
        HttpStatusCode.Forbidden => "insufficient_permission",
        HttpStatusCode.NotFound => "not_found",
        HttpStatusCode.Conflict => "conflict",
        _ when (int)Expected >= 400 => throw new InvalidOperationException($"{Id}: state the expected problem code."),
        _ => null,
    };

    public override string ToString() => Id;
}

/// <summary>
/// An operation that is deliberately public (no authentication, no permission check). <see cref="Body"/> is a
/// valid request body; <see cref="Expected"/> the status an anonymous caller gets (default: any 2xx), e.g. 400
/// for token-based operations where the token in the body is the credential.
/// </summary>
public sealed record AnonymousOperation(
    ApiOperation Operation,
    string Reason,
    Func<MatrixScenario, HttpContent?>? Body = null,
    HttpStatusCode? Expected = null);
