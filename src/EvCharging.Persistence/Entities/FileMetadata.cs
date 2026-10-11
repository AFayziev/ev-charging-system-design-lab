namespace EvCharging.Persistence.Entities;

public sealed class FileMetadata
{
    public Guid Id { get; set; }
    public FileKind Kind { get; set; }
    public FileState State { get; set; }
    public Guid? StationId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? SourceFileId { get; set; }
    public required string Bucket { get; set; }
    public required string ObjectKey { get; set; }
    public required string ContentType { get; set; }
    public long? ByteLength { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
