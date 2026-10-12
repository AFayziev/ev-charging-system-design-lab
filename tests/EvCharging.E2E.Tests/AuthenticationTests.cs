using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using EvCharging.Gateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace EvCharging.E2E.Tests;

[Collection(nameof(E2ECollection))]
public sealed class AuthenticationTests(AppHostFixture fixture)
{
    [Theory]
    [InlineData("driver")]
    [InlineData("operator")]
    [Trait("Category", "Browser")]
    public async Task Login_SyntheticUser_SharesSessionAndSignsOut(string username)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var frontend = fixture.App.GetEndpoint("frontend", "http");
        var identity = fixture.App.GetEndpoint("keycloak", "http");
        var model = fixture.App.Services.GetRequiredService<DistributedApplicationModel>();
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(30_000);
        var navigations = new HashSet<string>();
        page.Request += (_, request) =>
        {
            if (request.IsNavigationRequest)
            {
                navigations.Add(new Uri(request.Url).GetLeftPart(UriPartial.Path));
            }
        };
        var password = await model.Resources.OfType<ParameterResource>()
            .Single(resource => resource.Name == $"synthetic-{username}-password")
            .GetValueAsync(CancellationToken.None)
            ?? throw new InvalidOperationException("The synthetic user's password is required.");

        await page.GotoAsync(new Uri(frontend, "/auth/login?returnUrl=/").AbsoluteUri);
        Assert.StartsWith(new Uri(identity, "/realms/evcharging/").AbsoluteUri, page.Url);
        await page.GetByLabel("Username or email").FillAsync(username);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(
            new Uri(frontend, "/stations").AbsoluteUri);

        await Expect(page.GetByRole(
            AriaRole.Link,
            new() { Name = username == "driver" ? "Stations" : "Operator area" }))
            .ToBeVisibleAsync();
        await page.GetByRole(
            AriaRole.Link,
            new() { Name = username == "driver" ? "Stations" : "Operator area" })
            .ClickAsync();
        await Expect(page.GetByRole(
            AriaRole.Heading,
            new() { Name = username == "driver" ? "Find a station" : "Operator area" }))
            .ToBeVisibleAsync();

        var instances = new HashSet<string>();
        for (var request = 0; request < 12; request++)
        {
            var session = await page.EvaluateAsync<JsonElement>("""
                async () => {
                    const response = await fetch('/auth/session');
                    return {
                        status: response.status,
                        instance: response.headers.get('X-Gateway-Instance'),
                        body: await response.text()
                    };
                }
                """);
            Assert.Equal(200, session.GetProperty("status").GetInt32());
            using var bodyDocument = JsonDocument.Parse(session.GetProperty("body").GetString()
                ?? throw new InvalidOperationException("Session response body is required."));
            var body = bodyDocument.RootElement;
            Assert.True(body.GetProperty("signedIn").GetBoolean());
            Assert.Equal(username, body.GetProperty("user").GetProperty("username").GetString());
            Assert.Equal(
                new[] { "email", "name", "roles", "username" },
                body.GetProperty("user").EnumerateObject().Select(property => property.Name).Order().ToArray());
            Assert.Equal(
                new[] { "antiforgeryToken", "signedIn", "user" },
                body.EnumerateObject().Select(property => property.Name).Order().ToArray());
            instances.Add(session.GetProperty("instance").GetString() ?? "");
        }

        Assert.Equal(2, instances.Count);
        var apiResponse = await page.EvaluateAsync<int>(
            "async () => (await fetch('/api/diagnostics/instance', {headers: "
            + "{Authorization: 'Bearer forged', 'X-User-Id': 'forged', "
            + "'X-Forwarded-Host': 'forged.test'}})).status");
        Assert.Equal(200, apiResponse);
        var missingCsrf = await page.EvaluateAsync<int>(
            "async () => (await fetch('/api/__unmapped-test-route', "
            + "{method: 'POST'})).status");
        Assert.Equal(400, missingCsrf);
        var validCsrf = await page.EvaluateAsync<int>("""
            async () => {
                const session = await (await fetch('/auth/session')).json();
                return (await fetch('/api/__unmapped-test-route', {
                    method: 'POST', headers: {'X-CSRF-TOKEN': session.antiforgeryToken}
                })).status;
            }
            """);
        Assert.Equal(404, validCsrf);

        var sessionCookie = Assert.Single(
            await context.CookiesAsync(),
            cookie => cookie.Name == "EvCharging.Session");
        Assert.True(sessionCookie.HttpOnly);
        Assert.Equal(SameSiteAttribute.Lax, sessionCookie.SameSite);
        var rejected = await page.EvaluateAsync<int>(
            "async () => (await fetch('/auth/logout', { method: 'POST' })).status");
        Assert.Equal(400, rejected);

        await page.GetByRole(AriaRole.Button, new() { Name = "Account menu" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Sign out" }).ClickAsync();
        await page.WaitForURLAsync(new Uri(frontend, "/stations").AbsoluteUri);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Find a station" }))
            .ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true }))
            .ToBeVisibleAsync();
        var signedOut = await page.EvaluateAsync<bool>(
            "async () => (await (await fetch('/auth/session')).json()).signedIn");
        Assert.False(signedOut);
        Assert.Contains(new Uri(frontend, "/auth/callback").AbsoluteUri, navigations);
        Assert.Contains(new Uri(frontend, "/auth/logout-callback").AbsoluteUri, navigations);
        Assert.Contains(
            new Uri(identity, "/realms/evcharging/protocol/openid-connect/logout").AbsoluteUri,
            navigations);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Refresh_ConcurrentRequests_RenewsSharedTokenAndRejectsForgedBearer()
    {
        using var authenticationHost = await fixture.CreateAuthenticationHostAsync();
        var tickets = authenticationHost.Services.GetRequiredService<ITicketStore>();
        var contexts = authenticationHost.Services.GetRequiredService<IDbContextFactory<AuthenticationDbContext>>();
        await using var database = await contexts.CreateDbContextAsync();
        var existingSessionIds = await database.Sessions.Select(value => value.Id).ToListAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        Guid sessionId = await database.Sessions.AsNoTracking()
            .Where(value => !existingSessionIds.Contains(value.Id))
            .Select(value => value.Id)
            .SingleAsync();
        string key = sessionId.ToString();
        var ticket = Assert.IsType<AuthenticationTicket>(await tickets.RetrieveAsync(key));
        var previousToken = ticket.Properties.GetTokenValue("access_token");
        ticket.Properties.UpdateTokenValue("expires_at", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("o"));
        await tickets.RenewAsync(key, ticket);
        var refreshResponses = await page.EvaluateAsync<int[]>(
            "async () => Promise.all(Array.from({length: 8}, async () => "
            + "(await fetch('/api/diagnostics/instance')).status))");
        Assert.All(refreshResponses, status => Assert.Equal(200, status));
        var afterRefresh = Assert.IsType<AuthenticationTicket>(await tickets.RetrieveAsync(key));
        Assert.NotEqual(previousToken, afterRefresh.Properties.GetTokenValue("access_token"));

        using var api = fixture.App.CreateHttpClient("api", "http");
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", afterRefresh.Properties.GetTokenValue("access_token"));
        using var acceptedBearer = await api.GetAsync("/diagnostics/instance");
        Assert.Equal(HttpStatusCode.OK, acceptedBearer.StatusCode);
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "forged");
        using var rejectedBearer = await api.GetAsync("/diagnostics/instance");
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedBearer.StatusCode);

    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Refresh_RevokedRoles_DeniesProtectedRequests()
    {
        using var authenticationHost = await fixture.CreateAuthenticationHostAsync();
        var tickets = authenticationHost.Services.GetRequiredService<ITicketStore>();
        var contexts = authenticationHost.Services.GetRequiredService<IDbContextFactory<AuthenticationDbContext>>();
        await using var database = await contexts.CreateDbContextAsync();
        var existingSessionIds = await database.Sessions.Select(value => value.Id).ToListAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        Guid sessionId = await database.Sessions.AsNoTracking()
            .Where(value => !existingSessionIds.Contains(value.Id))
            .Select(value => value.Id)
            .SingleAsync();
        string key = sessionId.ToString();
        var ticket = Assert.IsType<AuthenticationTicket>(await tickets.RetrieveAsync(key));
        var model = fixture.App.Services.GetRequiredService<DistributedApplicationModel>();
        using var provider = fixture.App.CreateHttpClient("keycloak", "http");
        var adminPassword = await model.Resources.OfType<ParameterResource>()
            .Single(resource => resource.Name == "keycloak-admin-password")
            .GetValueAsync(CancellationToken.None)
            ?? throw new InvalidOperationException("The identity administrator password is required.");
        using var adminResponse = await provider.PostAsync(
            "/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = "admin",
                ["password"] = adminPassword
            }));
        adminResponse.EnsureSuccessStatusCode();
        using var adminToken = JsonDocument.Parse(await adminResponse.Content.ReadAsStringAsync());
        provider.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", adminToken.RootElement.GetProperty("access_token").GetString());
        var subject = ticket.Principal.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("A user subject is required.");
        var rolesPath = $"/admin/realms/evcharging/users/{subject}/role-mappings/realm";
        var roles = await provider.GetFromJsonAsync<JsonElement>(rolesPath);
        using var removeRoles = new HttpRequestMessage(HttpMethod.Delete, rolesPath)
        { Content = JsonContent.Create(roles) };
        using var removedRoles = await provider.SendAsync(removeRoles);
        removedRoles.EnsureSuccessStatusCode();
        try
        {
            ticket.Properties.UpdateTokenValue(
                "expires_at", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("o"));
            await tickets.RenewAsync(key, ticket);
            var forbidden = await page.EvaluateAsync<int>(
                "async () => (await fetch('/api/diagnostics/instance')).status");
            Assert.Equal(403, forbidden);
        }
        finally
        {
            using var restoredRoles = await provider.PostAsJsonAsync(rolesPath, roles);
            restoredRoles.EnsureSuccessStatusCode();
        }

    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task Login_ExistingBrowser_ReplacesIdentity()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await fixture.LoginAsync(page, "driver");
        var identityCookies = (await context.CookiesAsync())
            .Where(cookie => cookie.Name.StartsWith("KEYCLOAK", StringComparison.Ordinal));

        foreach (var cookie in identityCookies)
        {
            await context.ClearCookiesAsync(
                new() { Name = cookie.Name, Domain = cookie.Domain, Path = cookie.Path });
        }

        await fixture.LoginAsync(page, "operator");
        var replaced = await page.EvaluateAsync<string>(
            "async () => (await (await fetch('/auth/session')).json()).user.username");
        Assert.Equal("operator", replaced);
    }

    [Fact]
    [Trait("Category", "Api")]
    public async Task Login_ExternalReturnUrl_Rejected()
    {
        using var client = fixture.App.CreateHttpClient("gateway", "http");

        foreach (var returnUrl in new[] { "https://example.test/", "//example.test/", "/\\example.test/" })
        {
            using var denied = await client.GetAsync("/auth/login?returnUrl=" + Uri.EscapeDataString(returnUrl));
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
    }
}
