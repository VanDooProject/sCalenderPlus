using System.Net;
using SCalenderPlus.IntegrationTests.Infrastructure;

namespace SCalenderPlus.IntegrationTests.Authorization;

/// <summary>Executes every matrix case against the real api with a migrated PostgreSQL database.</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class AuthorizationMatrixTests(MatrixScenario scenario) : IClassFixture<MatrixScenario>
{
    /// <summary>One row per case; a single skipped row while no protected operation exists (empty theories fail).</summary>
    public static IEnumerable<TheoryDataRow<string>> CaseIds =>
        AuthorizationMatrix.Cases.Count == 0
            ? [new TheoryDataRow<string>("none") { Skip = "No protected operations yet." }]
            : AuthorizationMatrix.Cases.Select(c => new TheoryDataRow<string>(c.Id));

    public static TheoryData<string> AnonymousOperationIds => [.. AuthorizationMatrix.AnonymousOperations.Select(a => a.Operation.ToString())];

    [Theory]
    [MemberData(nameof(CaseIds))]
    public async Task Matrix_case(string id)
    {
        var matrixCase = AuthorizationMatrix.Cases.Single(c => c.Id == id);
        using var client = await matrixCase.Actor.CreateClientAsync(scenario);

        using var response = await SendAsync(client, matrixCase.Operation, matrixCase.RouteValues?.Invoke(scenario), matrixCase.Body?.Invoke(scenario));

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == matrixCase.Expected, $"{id}: got {(int)response.StatusCode}. Body: {body}");
    }

    [Theory]
    [MemberData(nameof(AnonymousOperationIds))]
    public async Task Anonymous_operation_needs_no_authentication(string id)
    {
        var anonymous = AuthorizationMatrix.AnonymousOperations.Single(a => a.Operation.ToString() == id);
        using var client = scenario.CreateAnonymousClient();

        using var response = await SendAsync(client, anonymous.Operation, routeValues: null, body: null);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.IsSuccessStatusCode, $"{id}: {(int)response.StatusCode}");
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        ApiOperation operation,
        IReadOnlyDictionary<string, string>? routeValues,
        HttpContent? body)
    {
        var path = operation.Path;
        foreach (var (name, value) in routeValues ?? new Dictionary<string, string>())
        {
            path = path.Replace("{" + name + "}", Uri.EscapeDataString(value), StringComparison.Ordinal);
        }

        Assert.DoesNotContain("{", path, StringComparison.Ordinal);
        using var request = new HttpRequestMessage(new HttpMethod(operation.Method), new Uri(path, UriKind.Relative)) { Content = body };

        // Cookie clients must send the CSRF header on unsafe methods (api.md §3); a missing header is its own test.
        request.Headers.Add("X-Requested-With", "scal");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
