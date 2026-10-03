using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using SCalenderPlus.Api.Problems;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.IntegrationTests.Infrastructure;
using ApiProgram = SCalenderPlus.Api.Program;

namespace SCalenderPlus.IntegrationTests.Problems;

/// <summary>Every error the api produces is application/problem+json with a catalogued code. No Docker needed.</summary>
public sealed class ProblemDetailsTests : IAsyncDisposable
{
    private static readonly Dictionary<string, RequestDelegate> _handlers = new(StringComparer.Ordinal)
    {
        ["/throw"] = _ => throw new InvalidOperationException("secret internal state"),
        ["/app-exception"] = _ => throw new AppException(
            ErrorCodes.PlanLimitReached,
            "Your plan allows 10 events with custom permissions.",
            new Dictionary<string, object?> { ["limit"] = new { key = "events_with_overrides", max = 10, used = 10 } }),
        ["/bad-request"] = _ => throw new BadHttpRequestException("Failed to read parameter \"x\""),
        ["/validation"] = context => TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["displayName"] = ["The displayName field is required."],
        }).ExecuteAsync(context),
        ["/validation-member-paths"] = context => TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["DisplayName"] = ["The DisplayName field is required."],
            ["Items[0].StartTime"] = ["Invalid."],
        }).ExecuteAsync(context),
        ["/problem-result"] = context => ApiProblems.Create(ErrorCodes.EmailNotVerified, "Verify your email first.").ExecuteAsync(context),
        ["/bare-status"] = context => TypedResults.StatusCode(StatusCodes.Status409Conflict).ExecuteAsync(context),
    };

    private readonly HostFactory<ApiProgram> _factory = new(
        TestSettings.For(TestSettings.UnreachableDatabase),
        services => TestPipeline.Add(services, _handlers));

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Unknown_api_route_is_not_found_problem()
    {
        using var response = await GetAsync("/api/v1/does-not-exist");

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Wrong_method_is_method_not_allowed_problem()
    {
        using var client = _factory.CreateClient();
        using var response = await client.PostAsync(new Uri("/health/live", UriKind.Relative), null, TestContext.Current.CancellationToken);

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.MethodNotAllowed, ErrorCodes.MethodNotAllowed);
    }

    [Fact]
    public async Task Unhandled_exception_is_internal_error_without_details()
    {
        using var response = await GetAsync("/__test/throw");

        var body = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.InternalServerError, ErrorCodes.InternalError);
        Assert.DoesNotContain("secret internal state", body.ToJsonString(), StringComparison.Ordinal);
        Assert.Null(body["detail"]);
    }

    [Fact]
    public async Task App_exception_maps_to_catalogued_status_with_detail_and_extensions()
    {
        using var response = await GetAsync("/__test/app-exception");

        var body = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.PaymentRequired, ErrorCodes.PlanLimitReached);
        Assert.Equal("Plan limit reached", (string?)body["title"]);
        Assert.Equal("Your plan allows 10 events with custom permissions.", (string?)body["detail"]);
        Assert.Equal(10, (int?)body["limit"]?["max"]);
    }

    [Fact]
    public async Task Request_binding_failure_is_bad_request()
    {
        using var response = await GetAsync("/__test/bad-request");

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.BadRequest);
    }

    [Fact]
    public async Task Validation_problem_has_code_and_errors_by_field()
    {
        using var response = await GetAsync("/__test/validation");

        var body = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Equal("The displayName field is required.", (string?)body["errors"]?["displayName"]?[0]);
    }

    [Fact]
    public async Task Validation_error_keys_are_camel_case_like_the_json_members()
    {
        using var response = await GetAsync("/__test/validation-member-paths");

        var body = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        var errors = body["errors"]!.AsObject();
        Assert.Equal(["displayName", "items[0].startTime"], errors.Select(e => e.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Problem_result_of_a_handler_keeps_its_code()
    {
        using var response = await GetAsync("/__test/problem-result");

        var body = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.EmailNotVerified);
        Assert.Equal("Verify your email first.", (string?)body["detail"]);
    }

    [Fact]
    public async Task Bare_status_code_gets_a_problem_body_with_the_default_code()
    {
        using var response = await GetAsync("/__test/bare-status");

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.Conflict, ErrorCodes.Conflict);
    }

    [Fact]
    public async Task Health_endpoints_keep_their_own_json_body()
    {
        using var response = await GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OpenApi_document_publishes_every_error_code()
    {
        using var response = await GetAsync("/openapi/v1.json");
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        var published = document["components"]!["schemas"]!["ErrorCode"]!["enum"]!.AsArray().Select(n => (string)n!).ToList();
        Assert.Equal(ErrorCodes.All.Order(StringComparer.Ordinal), published);
    }

    private async Task<HttpResponseMessage> GetAsync(string path)
    {
        using var client = _factory.CreateClient();
        return await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
    }
}
