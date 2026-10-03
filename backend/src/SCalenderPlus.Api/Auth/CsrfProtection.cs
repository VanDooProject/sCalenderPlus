using SCalenderPlus.Api.Problems;
using SCalenderPlus.Application.Errors;

namespace SCalenderPlus.Api.Auth;

/// <summary>
/// CSRF protection for cookie clients (docs/architecture/api.md §3): every unsafe request (anything but
/// GET/HEAD/OPTIONS/TRACE) below <c>/api</c> must carry <c>X-Requested-With: scal</c>, otherwise
/// <c>403 csrf_header_missing</c>. A cross-site page can only send such a custom header after a CORS preflight,
/// and the api answers no preflight (CORS is closed: no CORS middleware, no <c>Access-Control-*</c> headers).
/// This also covers anonymous endpoints (login CSRF) and complements the <c>SameSite=Lax</c> session cookie.
/// <para>Exempt: endpoints marked <see cref="DisableCsrfProtection{TBuilder}"/> (e.g. provider webhooks that
/// authenticate by signature) and requests authenticated by an API token (<see cref="IsTokenRequest"/>; such
/// requests carry no ambient credentials).</para>
/// </summary>
public static class CsrfProtection
{
    public const string HeaderName = "X-Requested-With";
    public const string HeaderValue = "scal";

    public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app) => app.Use(static (context, next) =>
        RequiresHeader(context) && !HasHeader(context)
            ? ApiProblems.Create(ErrorCodes.CsrfHeaderMissing, $"Unsafe requests need the header '{HeaderName}: {HeaderValue}'.").ExecuteAsync(context)
            : next(context));

    /// <summary>Exempts an endpoint that is not called by browsers with the session cookie (e.g. a signed webhook).</summary>
    public static TBuilder DisableCsrfProtection<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new CsrfExemptMetadata());

    /// <summary>
    /// Hook for API tokens (v1, <c>Authorization: Bearer scal_pat_…</c>): a request with a bearer token and without
    /// the session cookie cannot be forged by a browser, so it needs no CSRF header. The bearer handler that
    /// authenticates such requests lands with the API tokens feature.
    /// </summary>
    public static bool IsTokenRequest(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && !request.Cookies.ContainsKey(AuthenticationSetup.SessionCookieName);
    }

    private static bool RequiresHeader(HttpContext context) =>
        !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method) || HttpMethods.IsTrace(context.Request.Method))
        && context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && context.GetEndpoint()?.Metadata.GetMetadata<CsrfExemptMetadata>() is null
        && !IsTokenRequest(context.Request);

    private static bool HasHeader(HttpContext context) =>
        context.Request.Headers.TryGetValue(HeaderName, out var values)
        && values.Count == 1
        && string.Equals(values[0], HeaderValue, StringComparison.Ordinal);

    private sealed class CsrfExemptMetadata;
}
