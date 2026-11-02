namespace EvCharging.MediaWorker.Processing;

/// <summary>The objects that publish one item; disposing releases their streams, and with them any temporary file.</summary>
public sealed class PreparedStationMedia(IReadOnlyList<PreparedStationObject> objects) : IDisposable
{
    public IReadOnlyList<PreparedStationObject> Objects { get; } = objects;

    public void Dispose()
    {
        foreach (var preparedObject in Objects)
        {
            preparedObject.Content.Dispose();
        }
    }
}
