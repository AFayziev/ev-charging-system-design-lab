using EvCharging.Api.Errors;
using EvCharging.Contracts;
using EvCharging.Domain.Reservations;
using FluentResults;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine;

namespace EvCharging.Api.Features.Reservations;

internal static class ReservationEndpoints
{
    public static WebApplication MapReservations(this WebApplication app)
    {
        var reservations = app.MapGroup("/reservations").RequireAuthorization(PlatformRole.Driver.ToClaimValue());
        reservations.MapPost("/", Create);
        reservations.MapGet("/", List);
        reservations.MapGet("/booking-policy", BookingPolicy);
        reservations.MapGet("/{id:guid}", Detail);
        reservations.MapPost("/{id:guid}/cancel", Cancel);
        return app;
    }

    private static async Task<Results<Ok<BookingPolicyResponse>, ChallengeHttpResult>> BookingPolicy(
        IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<BookingPolicyResponse>>(
            new GetBookingPolicyRequest(), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<
        Ok<ReservationResponse>, NotFound, ChallengeHttpResult, ForbidHttpResult, ProblemHttpResult>> Cancel(
            Guid id, IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<ReservationResponse>>(
            new CancelReservationCommand(id), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<ReservationCancellationError>(out var conflicts) => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Reservation cancellation conflict",
                detail: conflicts.First().Message),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<
        Created<ReservationResponse>,
        ValidationProblem,
        NotFound,
        ChallengeHttpResult,
        ForbidHttpResult,
        ProblemHttpResult>> Create(
            CreateReservationRequest request,
            IMessageBus bus,
            CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<ReservationResponse>>(request, cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<ValidationError>(out var errors) => errors.ToResponse(),
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<ReservationAdmissionError>(out var conflicts) => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Reservation conflict",
                detail: conflicts.First().Message),
            _ => TypedResults.Created($"/api/reservations/{result.Value.Id}", result.Value)
        };
    }

    private static async Task<Results<Ok<ReservationListResponse>, ValidationProblem, ChallengeHttpResult>> List(
        [AsParameters] ListReservationsRequest request,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<ReservationListResponse>>(request, cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<ValidationError>(out var errors) => errors.ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<Ok<ReservationResponse>, NotFound, ChallengeHttpResult, ForbidHttpResult>> Detail(
        Guid id, IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<ReservationResponse>>(
            new GetReservationRequest(id), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ => TypedResults.Ok(result.Value)
        };
    }
}
