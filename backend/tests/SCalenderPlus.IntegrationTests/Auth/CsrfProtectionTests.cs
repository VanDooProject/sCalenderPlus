using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;
using ApiProgram = SCalenderPlus.Api.Program;

namespace SCalenderPlus.IntegrationTests.Auth;

/// <summary>Issue #29: unsafe api requests need <c>X-Requested-With: scal</c>; CORS is closed. No Docker needed.</summary>
public sealed class CsrfProtectionTests : IAsyncDisposable
{
    private readonly HostFactory<ApiProgram> _api = new(TestSettings.For(TestSettings.UnreachableDatabase));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Theory]
    [InlineData(null)]
    [InlineData("XMLHttpRequest")]
    [InlineData("SCAL")]
    public async Task Post_without_the_exact_header_is_a_403_problem(string? headerValue)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/login", UriKind.Relative))
        {
            Content = JsonContent.Create(new { email = "mia@example.test", password = "whatever password" }),
        };
        if (headerValue is not null)
        {
            request.Headers.Add("X-Requested-With", headerValue);
        }

        using var response = await client.SendAsync(request, Ct);

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.CsrfHeaderMissing);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Every_unsafe_method_is_checked(string method)
    {
        using var client = CreateClient();

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), new Uri("/api/v1/me", UriKind.Relative)), Ct);

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.CsrfHeaderMissing);
    }

    [Fact]
    public async Task Safe_methods_and_non_api_paths_need_no_header()
    {
        using var client = CreateClient();

        using var get = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct);
        using var health = await client.PostAsync(new Uri("/health/live", UriKind.Relative), null, Ct);

        await ProblemResponse.AssertProblemAsync(get, HttpStatusCode.Unauthorized, ErrorCodes.Unauthenticated);
        await ProblemResponse.AssertProblemAsync(health, HttpStatusCode.MethodNotAllowed, ErrorCodes.MethodNotAllowed);
    }

    [Fact]
    public async Task Bearer_token_requests_without_session_cookie_are_exempt()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/not-a-route", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "scal_pat_test");

        using var response = await client.SendAsync(request, Ct);

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Bearer_header_does_not_exempt_a_request_with_the_session_cookie()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/not-a-route", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "scal_pat_test");
        request.Headers.Add("Cookie", "__Host-scal=anything");

        using var response = await client.SendAsync(request, Ct);

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.CsrfHeaderMissing);
    }

    [Fact]
    public async Task Cors_is_closed_preflights_get_no_access_control_headers()
    {
        using var client = CreateClient();
        using var preflight = new HttpRequestMessage(HttpMethod.Options, new Uri("/api/v1/auth/login", UriKind.Relative));
        preflight.Headers.Add("Origin", "https://evil.example");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-requested-with");

        using var response = await client.SendAsync(preflight, Ct);

        Assert.False(response.IsSuccessStatusCode);
        Assert.DoesNotContain(response.Headers, h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
    }

    private HttpClient CreateClient() => _api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = ApiTestHost.BaseAddress, HandleCookies = false });
}
