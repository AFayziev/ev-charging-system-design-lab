using FluentResults;

namespace EvCharging.MediaWorker.Processing;

/// <summary>The upload cannot become a published item; <see cref="FailureCode"/> is the reason kept on the item.</summary>
public sealed class StationMediaRejectedError(string failureCode) : Error($"The upload was rejected: {failureCode}.")
{
    public string FailureCode { get; } = failureCode;
}

/// <summary>
/// The upload is not in storage yet but still can be: the completion notice can overtake the file, which may land
/// until the upload URL expires.
/// </summary>
public sealed class StationUploadNotArrivedError() : Error("The upload has not reached storage yet.");
