using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Features.Reservations;
using EvCharging.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Xunit;

namespace EvCharging.Api.Tests;

public sealed class ApiAuthenticationTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("   ", HttpStatusCode.Unauthorized)]
    [InlineData("driver-subject", HttpStatusCode.BadRequest)]
    public async Task Authenticate_SignedToken_RequiresValidIdentity(
        string? subject, HttpStatusCode expectedStatus)
    {
        const string Issuer = "https://identity.example.test/realms/evcharging";
        const string Audience = "evcharging-api";
        using var signingKey = RSA.Create(2048);
        var key = new RsaSecurityKey(signingKey);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Configuration["ApiAuthentication:Authority"] = Issuer;
        builder.Configuration["ApiAuthentication:MetadataAddress"] = Issuer + "/.well-known/openid-configuration";
        builder.Configuration["ApiAuthentication:Audience"] = Audience;
        builder.Configuration["ApiAuthentication:AllowLocalHttp"] = "false";
        builder.AddApiAuthentication();
        builder.Services.AddDbContext<PlatformDbContext>(options => options.UseNpgsql());
        builder.UseWolverine(options =>
        {
            options.AddApplicationHandlers();
            options.UseEntityFrameworkCoreTransactions();
            options.Durability.Mode = DurabilityMode.MediatorOnly;
            options.Discovery.DisableConventionalDiscovery().IncludeType<ListReservationsQueryHandler>();
        });
        builder.Services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                metadata.SigningKeys.Add(key);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            });

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapReservations();
        await app.StartAsync();

        var claims = new List<Claim> { new("role", "driver") };
        if (subject is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, subject));
        }

        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            claims,
            now.AddMinutes(-1),
            now.AddMinutes(5),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("The test server must publish its listening address.");
        using var client = new HttpClient { BaseAddress = new Uri(addresses.Addresses.Single()) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));

        using var response = await client.GetAsync("/reservations?limit=0");
        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus == HttpStatusCode.Unauthorized)
        {
            Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        }
        else if (expectedStatus == HttpStatusCode.BadRequest)
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Single(problem.GetProperty("errors").GetProperty("Limit").EnumerateArray());
        }
    }
}
