using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace EvCharging.E2E.Tests;

internal static class BrowserAuthentication
{
    public static async Task LoginAsync(this AppHostFixture fixture, IPage page, string username)
    {
        var model = fixture.App.Services.GetRequiredService<DistributedApplicationModel>();
        string password = await model.Resources.OfType<ParameterResource>()
            .Single(resource => resource.Name == $"synthetic-{username}-password")
            .GetValueAsync(CancellationToken.None)
            ?? throw new InvalidOperationException("The synthetic user's password is required.");
        page.SetDefaultTimeout(30_000);
        await page.GotoAsync(new Uri(fixture.App.GetEndpoint("frontend", "http"), "/auth/login?returnUrl=/stations")
            .AbsoluteUri);
        await page.GetByLabel("Email", new() { Exact = true }).FillAsync($"{username}@example.test");
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Account menu" }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }
}
