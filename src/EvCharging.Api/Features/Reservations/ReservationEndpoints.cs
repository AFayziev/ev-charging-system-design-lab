using System.Security.Claims;
using EvCharging.Api.Errors;
using EvCharging.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EvCharging.Api.Features.Reservations;

internal static class ReservationEndpoints
{
    public static IHostApplicationBuilder AddReservations(this IHostApplicationBuilder builder)
    {
        builder.Services.AddScoped<CreateReservationCommandHandler>();
        builder.Services.AddScoped<ListReservationsQueryHandler>();
        builder.Services.AddScoped<GetReservationQueryHandler>();
        return builder;
    }

    public static WebApplication MapReservations(this WebApplication app)
    {
        var reservations = app.MapGroup("/reservations").RequireAuthorization(PlatformRole.Driver.ToClaimValue());
        reservations.MapPost("/", Create);
        reservations.MapGet("/", List);
        reservations.MapGet("/{id:guid}", Detail);
        return app;
    }

    private static async Task<Results<
        Created<ReservationResponse>,
        ValidationProblem,
        NotFound,
        ForbidHttpResult,
        ProblemHttpResult>> Create(
            CreateReservationRequest request,
            ClaimsPrincipal user,
            CreateReservationCommandHandler handler,
            CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, user, cancellationToken);
        return result switch
        {
            _ when result.HasError<ValidationError>(out var errors) => errors.ToResponse(),
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<ReservationOverlapError>(out var conflicts) => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Reservation conflict",
                detail: conflicts.First().Message),
            _ => TypedResults.Created($"/api/reservations/{result.Value.Id}", result.Value)
        };
    }

    private static async Task<Results<Ok<ReservationListResponse>, ValidationProblem, ForbidHttpResult>> List(
        [AsParameters] ListReservationsRequest request,
        ClaimsPrincipal user,
        ListReservationsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, user, cancellationToken);
        return result switch
        {
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<ValidationError>(out var errors) => errors.ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<Ok<ReservationResponse>, NotFound, ForbidHttpResult>> Detail(
        Guid id, ClaimsPrincipal user, GetReservationQueryHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, user, cancellationToken);
        return result switch
        {
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ => TypedResults.Ok(result.Value)
        };
    }
}
