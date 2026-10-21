namespace EvCharging.Api.Streaming;

public sealed class ObservationStreamException(string message, Exception innerException)
    : Exception(message, innerException);
