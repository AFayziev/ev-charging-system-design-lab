using EvCharging.Api.Configuration;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Reservations;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Xunit;

namespace EvCharging.Api.Tests;

public sealed class QueryAuthenticationTests
{
    [Fact]
    public async Task ListReservations_WithoutHttpRequest_RejectsBeforeQuerying()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddDbContext<PlatformDbContext>(options => options.UseNpgsql());
        builder.UseWolverine(options =>
        {
            options.AddApplicationHandlers();
            options.UseEntityFrameworkCoreTransactions();
            options.Durability.Mode = DurabilityMode.MediatorOnly;
            options.Discovery.DisableConventionalDiscovery().IncludeType<ListReservationsQueryHandler>();
        });
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var result = await bus.InvokeAsync<Result<ReservationListResponse>>(new ListReservationsRequest(Limit: 0));
            Assert.True(result.IsFailed);
            Assert.True(result.HasError<UnauthenticatedError>());
            Assert.False(result.HasError<ValidationError>());
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
