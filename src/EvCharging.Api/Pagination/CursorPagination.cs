using EvCharging.Api.Errors;

namespace EvCharging.Api.Pagination;

internal static class CursorPagination
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 50;

    public static ValidationError? ValidateLimit(int limit, string propertyName) =>
        limit is < 1 or > MaximumPageSize
            ? new ValidationError(propertyName, $"The limit must be between 1 and {MaximumPageSize}.")
            : null;

    public static Guid? TrimPage<T>(List<T> items, int limit, Func<T, Guid> publicId)
    {
        if (items.Count <= limit)
        {
            return null;
        }

        items.RemoveRange(limit, items.Count - limit);
        return publicId(items[^1]);
    }
}
