using System.ComponentModel.DataAnnotations;

namespace EvCharging.S3ObjectStore;

public sealed class ObjectStoreOptions
{
    public const string SectionName = "ObjectStorage";

    /// <summary>The S3 endpoint the host calls.</summary>
    [Required, Url]
    public string ServiceUrl { get; set; } = "";

    /// <summary>The S3 endpoint browsers reach, carried by signed URLs; defaults to <see cref="ServiceUrl"/>.</summary>
    [Url]
    public string? PublicServiceUrl { get; set; }

    [Required]
    public string AccessKey { get; set; } = "";

    [Required]
    public string SecretKey { get; set; } = "";

    [Required]
    public string Region { get; set; } = "us-east-1";

    /// <summary>Uploads; reachable only through signed URLs.</summary>
    [Required]
    public string PrivateBucket { get; set; } = "";

    /// <summary>Published media, readable anonymously through <see cref="PublicBaseUrl"/>.</summary>
    [Required]
    public string PublicBucket { get; set; } = "";

    [Required, Url]
    public string PublicBaseUrl { get; set; } = "";

    [Range(typeof(TimeSpan), "00:00:30", "01:00:00")]
    public TimeSpan DownloadUrlLifetime { get; set; } = TimeSpan.FromMinutes(5);
}
