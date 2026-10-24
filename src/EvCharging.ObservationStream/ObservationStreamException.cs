namespace EvCharging.ObservationStream;

public sealed class ObservationStreamException(string message, Exception innerException)
    : Exception(message, innerException);
