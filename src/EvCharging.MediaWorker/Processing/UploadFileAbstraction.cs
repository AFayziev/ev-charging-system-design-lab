namespace EvCharging.MediaWorker.Processing;

/// <summary>Lets TagLib read an upload held in a stream. The stream stays open and belongs to the caller.</summary>
internal sealed class UploadFileAbstraction(Stream upload) : TagLib.File.IFileAbstraction
{
    public string Name => "upload.mp4";

    public Stream ReadStream => upload;

    public Stream WriteStream => throw new NotSupportedException("Uploads are only read.");

    public void CloseStream(Stream stream)
    {
    }
}
