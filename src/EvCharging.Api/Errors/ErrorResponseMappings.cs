using Microsoft.AspNetCore.Http.HttpResults;

namespace EvCharging.Api.Errors;

internal static class ErrorResponseMappings
{
    public static NotFound ToResponse<TId>(this NotFoundError<TId> error) => TypedResults.NotFound();

    public static ValidationProblem ToResponse(this IEnumerable<ValidationError> errors)
    {
        var fieldErrors = errors
            .GroupBy(error => error.PropertyName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());
        return TypedResults.ValidationProblem(fieldErrors);
    }
}
