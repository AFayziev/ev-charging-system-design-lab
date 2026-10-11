using EvCharging.Api.Authentication;
using EvCharging.Persistence.Auditing;

namespace EvCharging.Api.Persistence;

internal sealed class HttpAuditActorAccessor(IHttpContextAccessor httpContextAccessor) : IAuditActorAccessor
{
    public AuditActor GetActor()
    {
        var identity = httpContextAccessor.HttpContext?.User.GetUserIdentity();
        if (identity is null)
        {
            throw new InvalidOperationException("An authenticated issuer and subject are required for auditing.");
        }

        return AuditActor.User(identity.Issuer, identity.Subject);
    }
}
