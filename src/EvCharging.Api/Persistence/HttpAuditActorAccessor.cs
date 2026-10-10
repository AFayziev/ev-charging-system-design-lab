using EvCharging.Persistence.Auditing;

namespace EvCharging.Api.Persistence;

internal sealed class HttpAuditActorAccessor(IHttpContextAccessor httpContextAccessor) : IAuditActorAccessor
{
    public AuditActor GetActor()
    {
        var user = httpContextAccessor.HttpContext?.User;
        string? issuer = user?.FindFirst("iss")?.Value;
        string? subject = user?.FindFirst("sub")?.Value;
        if (user?.Identity?.IsAuthenticated != true
            || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            throw new InvalidOperationException("An authenticated issuer and subject are required for auditing.");
        }

        return AuditActor.User(issuer, subject);
    }
}
