using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SCalenderPlus.Api.Auth;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Auth;

/// <summary>
/// Issue #28, unverified-account restrictions (api.md §3): endpoints marked <c>RequireVerifiedEmail()</c> (share
/// links, invites, imports, accepting pending shares — added in later milestones) answer 403
/// <c>email_not_verified</c> to unverified accounts. Exercised on a test-only "create share link" endpoint.
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class VerifiedEmailPolicyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string ShareLinkPath = TestPipeline.PathPrefix + "/calendars/share-links";
    private ApiTestHost _host = null!;

    public async ValueTask InitializeAsync() =>
        _host = await ApiTestHost.StartAsync(postgres, services: services => TestPipeline.AddEndpoints(services, endpoints =>
            endpoints.MapPost(ShareLinkPath, () => TypedResults.Created()).RequireVerifiedEmail()));

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task Unverified_user_creating_a_share_link_gets_403_email_not_verified()
    {
        var email = ApiTestHost.UniqueEmail();
        await _host.CreateUserAsync(email, emailConfirmed: false);
        using var client = await _host.SignedInClientAsync(email);

        using var response = await client.PostAsync(new Uri(ShareLinkPath, UriKind.Relative), null, TestContext.Current.CancellationToken);

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.EmailNotVerified);
    }

    [Fact]
    public async Task Verified_user_passes_and_anonymous_gets_401()
    {
        var email = ApiTestHost.UniqueEmail();
        await _host.CreateUserAsync(email, emailConfirmed: true);
        using var verified = await _host.SignedInClientAsync(email);
        using var anonymous = _host.CreateClient();

        using var allowed = await verified.PostAsync(new Uri(ShareLinkPath, UriKind.Relative), null, TestContext.Current.CancellationToken);
        using var unauthenticated = await anonymous.PostAsync(new Uri(ShareLinkPath, UriKind.Relative), null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        await ProblemResponse.AssertProblemAsync(unauthenticated, HttpStatusCode.Unauthorized, ErrorCodes.Unauthenticated);
    }
}
