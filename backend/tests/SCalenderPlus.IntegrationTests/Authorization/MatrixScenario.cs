using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using SCalenderPlus.Infrastructure.Identity;
using SCalenderPlus.IntegrationTests.Infrastructure;
using ApiProgram = SCalenderPlus.Api.Program;

namespace SCalenderPlus.IntegrationTests.Authorization;

/// <summary>
/// The seeded world the matrix runs in: one migrated database and api host per test class, plus named
/// resources (ids) created by <see cref="SeedAsync"/> that cases use as route values, and one signed-in session
/// (cookie jar) per actor. Seeding grows with the features (M1-C: groups "lions"/"other-tenant" with members per role).
/// </summary>
public sealed class MatrixScenario(PostgresFixture postgres) : IAsyncLifetime
{
    public const string Password = "matrix password 1";
    public static readonly Uri BaseAddress = new("https://localhost");

    private readonly Dictionary<string, string> _resources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CookieContainer> _sessions = new(StringComparer.Ordinal);
    private HostFactory<ApiProgram>? _api;

    public HostFactory<ApiProgram> Api => _api ?? throw new InvalidOperationException("Scenario not initialized.");

    /// <summary>Id of a seeded resource, e.g. <c>Get("group:lions")</c>, or a seeded user's email (<c>Get("email:user")</c>).</summary>
    public string Get(string name) =>
        _resources.TryGetValue(name, out var id) ? id : throw new KeyNotFoundException($"Resource '{name}' was not seeded.");

    internal void Set(string name, string id) => _resources[name] = id;

    public HttpClient CreateAnonymousClient() => Api.CreateClient(new() { BaseAddress = BaseAddress, AllowAutoRedirect = false, HandleCookies = false });

    /// <summary>A client in the actor's seeded session; cookie updates (refreshed sessions) are kept for later cases.</summary>
    public HttpClient SessionClient(string actor) =>
        _sessions.TryGetValue(actor, out var cookies)
            ? Api.CreateDefaultClient(BaseAddress, new CookieContainerHandler(cookies))
            : throw new KeyNotFoundException($"No session seeded for actor '{actor}'.");

    /// <summary>A client with a brand-new session of the seeded user (for cases that end or replace their session).</summary>
    public async Task<HttpClient> NewSessionClientAsync(string actor)
    {
        var cookies = new CookieContainer();
        await SignInAsync(cookies, Get("email:" + actor));
        return Api.CreateDefaultClient(BaseAddress, new CookieContainerHandler(cookies));
    }

    /// <summary>A client of a 2FA user that passed the password step only (pending-login cookie, no session).</summary>
    public async Task<HttpClient> PendingSecondFactorClientAsync(string actor)
    {
        var cookies = new CookieContainer();
        await SignInAsync(cookies, Get("email:" + actor), completeSecondFactor: false);
        return Api.CreateDefaultClient(BaseAddress, new CookieContainerHandler(cookies));
    }

    /// <summary>The current authenticator code of a seeded user with an authenticator secret.</summary>
    public string CurrentTotp(string actor) => Totp.Compute(Get("totp:" + actor));

    public async ValueTask InitializeAsync()
    {
        _api = new HostFactory<ApiProgram>(TestSettings.For(await postgres.CreateDatabaseAsync(), autoMigrate: true));
        using var _ = _api.CreateClient(); // start the host: migrations run before it serves
        await SeedAsync();
    }

    private async Task SeedAsync()
    {
        // "user" has an authenticator secret but 2FA off (the enable case turns it on).
        await SeedUserAsync(Actors.User.Name, emailConfirmed: true, twoFactor: TwoFactorSeed.SecretOnly);
        await SeedUserAsync(Actors.UnverifiedUser.Name, emailConfirmed: false);
        await SeedUserAsync(Actors.OtherUser.Name, emailConfirmed: true);
        await SeedUserAsync(Actors.FreshSession.Name, emailConfirmed: true, signIn: false);
        await SeedUserAsync(Actors.TwoFactorUser.Name, emailConfirmed: true, twoFactor: TwoFactorSeed.Enabled);
        await SeedUserAsync(Actors.TwoFactorPending.Name, emailConfirmed: true, twoFactor: TwoFactorSeed.Enabled, signIn: false);
    }

    private enum TwoFactorSeed
    {
        None,
        SecretOnly,
        Enabled,
    }

    private async Task SeedUserAsync(string actor, bool emailConfirmed, bool signIn = true, TwoFactorSeed twoFactor = TwoFactorSeed.None)
    {
        var email = $"{actor}@matrix.example.test";
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser
            {
                Id = Guid.CreateVersion7(),
                Email = email,
                UserName = email,
                DisplayName = actor,
                EmailConfirmed = emailConfirmed,
            };
            var result = await users.CreateAsync(user, Password);
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
            Set("user:" + actor, user.Id.ToString());

            if (twoFactor != TwoFactorSeed.None)
            {
                await users.ResetAuthenticatorKeyAsync(user);
                Set("totp:" + actor, (await users.GetAuthenticatorKeyAsync(user))!);
                if (twoFactor == TwoFactorSeed.Enabled)
                {
                    await users.SetTwoFactorEnabledAsync(user, true);
                    await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
                }
            }
        }

        Set("email:" + actor, email);
        if (signIn)
        {
            var cookies = new CookieContainer();
            await SignInAsync(cookies, email);
            _sessions[actor] = cookies;
        }
    }

    private async Task SignInAsync(CookieContainer cookies, string email, bool completeSecondFactor = true)
    {
        using var client = Api.CreateDefaultClient(BaseAddress, new CookieContainerHandler(cookies));
        client.DefaultRequestHeaders.Add("X-Requested-With", "scal");
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Seeding: login of {email} failed with {(int)response.StatusCode}.");

        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        if (completeSecondFactor && (bool)body!["twoFactorRequired"]!)
        {
            var actor = email[..email.IndexOf('@', StringComparison.Ordinal)];
            using var second = await client.PostAsJsonAsync("/api/v1/auth/login/2fa", new { code = CurrentTotp(actor) });
            Assert.True(second.StatusCode == HttpStatusCode.OK, $"Seeding: second factor of {email} failed with {(int)second.StatusCode}.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }
    }
}
