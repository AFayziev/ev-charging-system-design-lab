using FluentResults;

namespace EvCharging.Domain.Media;

public sealed class StationMediaUploadFailedError()
    : Error("The upload expired or could not be processed. Start a new upload.")
{
    public const string Code = "upload_failed";
}

public sealed class StationMediaUploadValidationError(StationMediaUploadField field, string message) : Error(message)
{
    public StationMediaUploadField Field { get; } = field;
}

public sealed class StationMediaLimitError(string message) : Error(message)
{
    public const string Code = "media_limit_reached";
}

/// <summary>The photo is not in a state that allows the operation.</summary>
public sealed class StationMediaStateError(string message) : Error(message);

public sealed class StationMediaOrderError()
    : Error("The order must list every photo of the gallery exactly once.");
