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
        media.MapPost("/{itemId:guid}/versions/{versionId:guid}/upload-completion", CompleteUpload);
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
            _ when result.HasError<ConcurrencyConflictError>(out var conflicts) => conflicts.First().ToResponse(),
            _ => TypedResults.Created((string?)null, result.Value)
        };
    }

    private static async Task<Results<
        Ok<StationMediaVersionResponse>, NotFound, ChallengeHttpResult, ForbidHttpResult, ProblemHttpResult>>
        CompleteUpload(Guid id, Guid itemId, Guid versionId, IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<StationMediaVersionResponse>>(
            new CompleteStationMediaUploadCommand(id, itemId, versionId), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<StationMediaUploadMissingError>(out var absent) =>
                ErrorResponseMappings.CodedConflict(
                    "Upload missing",
                    absent.First().Message,
                    StationMediaUploadMissingError.Code),
            _ when result.HasError<StationMediaUploadChangedError>(out var changes) =>
                ErrorResponseMappings.CodedConflict(
                    "Upload changed",
                    changes.First().Message,
                    StationMediaUploadChangedError.Code),
            _ when result.HasError<StationMediaUploadFailedError>(out var failures) =>
                ErrorResponseMappings.CodedConflict(
                    "Upload failed",
                    failures.First().Message,
                    StationMediaUploadFailedError.Code),
            _ => TypedResults.Ok(result.Value)
        };
    }
}
