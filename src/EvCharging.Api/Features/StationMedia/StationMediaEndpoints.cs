using EvCharging.Api.Errors;
using EvCharging.Contracts;
using EvCharging.Domain.Media;
using FluentResults;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine;

namespace EvCharging.Api.Features.StationMedia;

internal static class StationMediaEndpoints
{
    public static WebApplication MapStationMedia(this WebApplication app)
    {
        var media = app.MapGroup("/stations/{id:guid}/media")
            .RequireAuthorization(PlatformRole.Operator.ToClaimValue());
        media.MapGet("/edit", Editor);
        media.MapPost("/photo-uploads", StartPhotoUpload);
        media.MapPost("/{itemId:guid}/upload-completion", CompleteUpload);
        media.MapPut("/order", Reorder);
        media.MapDelete("/{itemId:guid}", Remove);
        return app;
    }

    private static async Task<Results<
        Ok<StationGalleryEditorResponse>, NotFound, ChallengeHttpResult, ForbidHttpResult>> Editor(
            Guid id, IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<StationGalleryEditorResponse>>(
            new GetStationGalleryEditorRequest(id), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<NotFoundError<Guid>>(out var errors) => errors.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<
        Created<StationPhotoUploadResponse>,
        ValidationProblem,
        NotFound,
        ChallengeHttpResult,
        ForbidHttpResult,
        ProblemHttpResult>> StartPhotoUpload(
            Guid id,
            StartStationPhotoUploadRequest request,
            IMessageBus bus,
            CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<StationPhotoUploadResponse>>(
            new StartStationPhotoUploadCommand(id, request), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<ValidationError>(out var errors) => errors.ToResponse(),
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<StationMediaLimitError>(out var limits) =>
                ErrorResponseMappings.CodedConflict(
                    "Gallery is full",
                    limits.First().Message,
                    StationMediaLimitError.Code),
            _ when result.HasError<StationMediaStateError>(out var states) => states.First().ToResponse(),
            _ when result.HasError<ConcurrencyConflictError>(out var conflicts) => conflicts.First().ToResponse(),
            _ => TypedResults.Created((string?)null, result.Value)
        };
    }

    private static async Task<Results<
        Ok<StationMediaUploadStateResponse>, NotFound, ChallengeHttpResult, ForbidHttpResult, ProblemHttpResult>>
        CompleteUpload(Guid id, Guid itemId, IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<StationMediaUploadStateResponse>>(
            new CompleteStationMediaUploadCommand(id, itemId), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<StationMediaUploadFailedError>(out var failures) =>
                ErrorResponseMappings.CodedConflict(
                    "Upload failed",
                    failures.First().Message,
                    StationMediaUploadFailedError.Code),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<
        Ok<StationGalleryVersionResponse>,
        ValidationProblem,
        NotFound,
        ChallengeHttpResult,
        ForbidHttpResult,
        ProblemHttpResult>> Reorder(
            Guid id, ReorderStationGalleryRequest request, IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<StationGalleryVersionResponse>>(
            new ReorderStationGalleryCommand(id, request), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<ValidationError>(out var errors) => errors.ToResponse(),
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<ConcurrencyConflictError>(out var conflicts) => conflicts.First().ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<
        Ok<StationGalleryVersionResponse>, NotFound, ChallengeHttpResult, ForbidHttpResult, ProblemHttpResult>> Remove(
            Guid id, Guid itemId, IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<StationGalleryVersionResponse>>(
            new RemoveStationMediaCommand(id, itemId), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<ConcurrencyConflictError>(out var conflicts) => conflicts.First().ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static ProblemHttpResult ToResponse(this StationMediaStateError error) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Photo cannot be changed",
        detail: error.Message);
}
