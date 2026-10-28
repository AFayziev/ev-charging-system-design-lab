using FluentResults;

namespace EvCharging.Domain.Media;

public sealed class StationMediaUploadFailedError()
    : Error("The upload expired or does not match the file it started with. Start a new upload.")
{
    public const string Code = "upload_failed";
}

public sealed class StationMediaUploadMissingError()
    : Error("The upload has not reached storage. Upload the file before completing it.")
{
    public const string Code = "upload_missing";
}

public sealed class StationMediaUploadValidationError(StationMediaUploadField field, string message) : Error(message)
{
    public StationMediaUploadField Field { get; } = field;
}

public sealed class StationMediaLimitError(string message) : Error(message)
{
    public const string Code = "media_limit_reached";
}
