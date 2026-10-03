using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SCalenderPlus.Application.Email;
using SCalenderPlus.Application.Jobs;
using SCalenderPlus.Infrastructure.Auditing;
using SCalenderPlus.Infrastructure.Identity;
using SCalenderPlus.Infrastructure.Jobs;
using SCalenderPlus.Infrastructure.Persistence;
using SCalenderPlus.IntegrationTests.Infrastructure;
using ApiProgram = SCalenderPlus.Api.Program;

namespace SCalenderPlus.IntegrationTests.Auth;

/// <summary>
/// The api on a fresh, migrated database, with helpers for account tests: HTTPS clients with a cookie jar (the
/// session cookie is <c>Secure</c>) and the CSRF header, users seeded through Identity, queued emails (the
/// <c>email.send</c> jobs the worker would deliver) and audit rows.
/// </summary>
internal sealed partial class ApiTestHost : IAsyncDisposable
{
    public const string Password = "correct horse battery";
    public static readonly Uri BaseAddress = new("https://localhost");

    private ApiTestHost(HostFactory<ApiProgram> api) => Api = api;

    public HostFactory<ApiProgram> Api { get; }

    public static async Task<ApiTestHost> StartAsync(
        PostgresFixture postgres,
        Action<Dictionary<string, string?>>? configure = null,
        Action<IServiceCollection>? services = null)
    {
        var settings = TestSettings.For(await postgres.CreateDatabaseAsync(), autoMigrate: true);
        configure?.Invoke(settings);
        var host = new ApiTestHost(new HostFactory<ApiProgram>(settings, services));
        _ = host.Api.Services; // start: migrations run before the host serves
        return host;
    }

    public static string UniqueEmail(string name = "mia") => $"{name}-{Guid.NewGuid():N}@example.test";

    /// <summary>HTTPS client with a cookie jar; sends <c>X-Requested-With: scal</c> like the web app unless disabled.</summary>
    public HttpClient CreateClient(bool csrfHeader = true)
    {
        var client = Api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = BaseAddress, AllowAutoRedirect = false, HandleCookies = true });
        if (csrfHeader)
        {
            client.DefaultRequestHeaders.Add("X-Requested-With", "scal");
        }

        return client;
    }

    public async Task<Guid> CreateUserAsync(string email, bool emailConfirmed = true, string password = Password, string displayName = "Mia")
    {
        await using var scope = Api.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            UserName = email,
            DisplayName = displayName,
            EmailConfirmed = emailConfirmed,
        };
        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return user.Id;
    }

    public async Task<HttpClient> SignedInClientAsync(string email, string password = Password)
    {
        var client = CreateClient();
        using var response = await LoginAsync(client, email, password);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return client;
    }

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = Password, bool rememberMe = false) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, rememberMe }, TestContext.Current.CancellationToken);

    /// <summary>Emails queued for <paramref name="to"/> (pending <c>email.send</c> jobs), oldest first.</summary>
    public async Task<IReadOnlyList<EmailMessage>> EmailsToAsync(string to)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        var jobs = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Job>().AsNoTracking()
            .Where(j => j.Type == "email.send").OrderBy(j => j.CreatedAt).ThenBy(j => j.Id).ToListAsync(TestContext.Current.CancellationToken);
        return [.. jobs
            .Select(j => JsonSerializer.Deserialize<EmailMessage>(j.Payload, JobContext.PayloadJson)!)
            .Where(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase))];
    }

    public async Task<EmailMessage> SingleEmailToAsync(string to) => Assert.Single(await EmailsToAsync(to));

    public Task<IReadOnlyList<AuditEvent>> AuditEventsAsync(Guid userId) => AuditEventsAsync("user", userId);

    public async Task<IReadOnlyList<AuditEvent>> AuditEventsAsync(string resourceType, Guid resourceId)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditEvent>().AsNoTracking()
            .Where(e => e.ResourceType == resourceType && e.ResourceId == resourceId.ToString())
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Runs <paramref name="query"/> on a fresh database context (assertions on stored state).</summary>
    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task<long> AclVersionAsync(Guid userId) =>
        QueryAsync(db => db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.AclVersion).SingleAsync(TestContext.Current.CancellationToken));

    public async Task<AppUser> FindUserAsync(string email)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email))!;
    }

    /// <summary>The web app link of an account email: path, user id and token.</summary>
    public static (string Path, Guid UserId, string Token) LinkIn(EmailMessage message)
    {
        var match = LinkPattern().Match(message.TextBody);
        Assert.True(match.Success, "No account link in: " + message.TextBody);
        return (match.Groups["path"].Value, Guid.Parse(match.Groups["userId"].Value), Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    public ValueTask DisposeAsync() => Api.DisposeAsync();

    [GeneratedRegex(@"https://app\.example\.test(?<path>/[a-z-]+)\?userId=(?<userId>[0-9a-f-]{36})&token=(?<token>\S+)")]
    private static partial Regex LinkPattern();
}
