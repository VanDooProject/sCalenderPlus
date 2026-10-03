using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using SCalenderPlus.Application.Groups;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.Infrastructure.Identity;
using SCalenderPlus.IntegrationTests.Infrastructure;
using ApiProgram = SCalenderPlus.Api.Program;

namespace SCalenderPlus.IntegrationTests.Authorization;

/// <summary>
/// The seeded world the matrix runs in: one migrated database and api host per test class, plus named
/// resources (ids) created by <see cref="SeedAsync"/> that cases use as route values, and one signed-in session
/// (cookie jar) per actor. Seeding grows with the features: groups "lions" (one member per role), "doomed" (deleted
/// by a case) and "other" (another tenant's group), plus invite links of "lions".
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

        // Groups (#34–#36).
        foreach (var actor in new[] { Actors.GroupOwner, Actors.GroupAdmin, Actors.GroupMember, Actors.GroupViewer, Actors.NonMember, Actors.OtherTenant })
        {
            await SeedUserAsync(actor.Name, emailConfirmed: true);
        }

        // Members without a session that membership cases change or remove (one per destructive case).
        foreach (var target in LionsTargets)
        {
            await SeedUserAsync(target.Name, emailConfirmed: true, signIn: false);
        }

        await SeedGroupAsync(
            "lions",
            [
                (Actors.GroupOwner.Name, GroupRole.Owner), (Actors.GroupAdmin.Name, GroupRole.Admin), (Actors.GroupMember.Name, GroupRole.Member), (Actors.GroupViewer.Name, GroupRole.Viewer),
                .. LionsTargets.Select(t => (t.Name, t.Role)),
            ]);
        await SeedGroupAsync("doomed", (Actors.GroupOwner.Name, GroupRole.Owner));
        await SeedInviteAsync("lions", "revocable");
        await SeedInviteAsync("lions", "acceptable");
        await SeedGroupAsync("other", (Actors.OtherTenant.Name, GroupRole.Owner));

        // Calendars (#41, #42): "lions" (group calendar, default role defaults) with user grants for the editor and
        // free/busy actors; "lions-doomed" (deleted by a case); "other" (another tenant's calendar).
        await SeedUserAsync(Actors.CalendarEditor.Name, emailConfirmed: true);
        await SeedUserAsync(Actors.CalendarFreeBusy.Name, emailConfirmed: true);
        await SeedCalendarAsync("lions", "lions", (Actors.CalendarEditor.Name, CalendarLevel.Edit), (Actors.CalendarFreeBusy.Name, CalendarLevel.FreeBusy));
        await SeedCalendarAsync("lions-doomed", "lions");
        await SeedCalendarAsync("other", "other");
    }

    /// <summary>A calendar owned by a seeded group with user grants; resource <c>calendar:{name}</c>.</summary>
    private async Task SeedCalendarAsync(string name, string group, params (string Actor, CalendarLevel Level)[] grants)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var now = SystemClock.Instance.GetCurrentInstant();
        var calendar = new Calendar
        {
            Id = Guid.CreateVersion7(),
            OwnerGroupId = Guid.Parse(Get("group:" + group)),
            Name = name,
            DefaultTimeZone = "Europe/Berlin",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Calendars.Add(calendar);
        foreach (var (actor, level) in grants)
        {
            var grant = CalendarGrantEntry.For(calendar.Id, Principal.User(Guid.Parse(Get("user:" + actor))), level);
            grant.CreatedBy = Guid.Parse(Get("user:" + Actors.GroupOwner.Name));
            grant.CreatedAt = grant.UpdatedAt = now;
            db.CalendarGrants.Add(grant);
            Set($"grant:{name}:{actor}", grant.Id.ToString());
        }

        await db.SaveChangesAsync();
        Set("calendar:" + name, calendar.Id.ToString());
    }

    /// <summary>An invite link (role member) of the group, created by its owner: resources <c>invite:{name}</c> and <c>token:{name}</c>.</summary>
    private async Task SeedInviteAsync(string group, string name)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        var invites = scope.ServiceProvider.GetRequiredService<GroupInviteService>();
        var created = await invites.CreateAsync(
            Guid.Parse(Get("user:" + Actors.GroupOwner.Name)),
            Guid.Parse(Get("group:" + group)),
            new NewInvite(Email: null, GroupRole.Member, MaxUses: GroupInvite.MaxLinkUses));
        Set("invite:" + name, created.Invite.Id.ToString());
        Set("token:" + name, System.Web.HttpUtility.ParseQueryString(created.Link!.Query)["token"]!);
    }

    /// <summary>Further members of "lions" (resource <c>user:{name}</c>): targets of role changes and removals.</summary>
    public static IReadOnlyList<(string Name, GroupRole Role)> LionsTargets { get; } =
    [
        ("lions-role-target", GroupRole.Member),
        ("lions-removed-by-owner", GroupRole.Member),
        ("lions-removed-by-admin", GroupRole.Viewer),
        ("lions-kept", GroupRole.Member),
    ];

    /// <summary>A group whose first member is its billing owner; resource <c>group:{name}</c>.</summary>
    private async Task SeedGroupAsync(string name, params (string Actor, GroupRole Role)[] members)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var now = SystemClock.Instance.GetCurrentInstant();
        var group = new Group
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            OwnerUserId = Guid.Parse(Get("user:" + members[0].Actor)),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Groups.Add(group);
        foreach (var (actor, role) in members)
        {
            db.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = Guid.Parse(Get("user:" + actor)), Role = role, JoinedAt = now, UpdatedAt = now });
        }

        await db.SaveChangesAsync();
        Set("group:" + name, group.Id.ToString());
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
