namespace EvCharging.Persistence.Entities;

public sealed class FileMetadata
{
    public long Id { get; set; }
    public FileKind Kind { get; set; }
    public FileState State { get; set; }
    public long? StationId { get; set; }
    public long? SessionId { get; set; }
    public long? SourceFileId { get; set; }
    public required string Bucket { get; set; }
    public required string ObjectKey { get; set; }
    public required string ContentType { get; set; }
    public long? ByteLength { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
