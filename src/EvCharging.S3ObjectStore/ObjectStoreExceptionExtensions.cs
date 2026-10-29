using System.Net;
using Amazon.Runtime;
using Amazon.S3;

namespace EvCharging.S3ObjectStore;

public static class ObjectStoreExceptionExtensions
{
    /// <summary>
    /// Whether a storage call failed because storage or the network was unavailable, so that repeating it later can
    /// succeed. The SDK has already repeated the call within its own retry budget.
    /// </summary>
    public static bool IsTransientStorageFailure(this Exception exception) => exception switch
    {
        // A status of zero means no response arrived, such as a refused connection or a timeout.
        AmazonServiceException service => (int)service.StatusCode is 0 or >= 500
            || service.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
            || service.ErrorCode is "SlowDown" or "RequestTimeout"
            // A batch delete answers 200 and reports the keys it could not delete.
            || service is DeleteObjectsException,
        AmazonClientException => true,
        // The connection ended while the object body was being read.
        IOException => true,
        _ => false
    };
}
