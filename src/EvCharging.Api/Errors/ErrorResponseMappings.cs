using Microsoft.AspNetCore.Http.HttpResults;

namespace EvCharging.Api.Errors;

internal static class ErrorResponseMappings
{
    public static NotFound ToResponse<TId>(this NotFoundError<TId> error) => TypedResults.NotFound();

    public static ProblemHttpResult ToResponse(this ConcurrencyConflictError error) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Resource changed",
        detail: error.Message);

    public static ValidationProblem ToResponse(this IEnumerable<ValidationError> errors)
    {
        var fieldErrors = errors
            .GroupBy(error => error.PropertyName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());
        return TypedResults.ValidationProblem(fieldErrors);
    }
}
