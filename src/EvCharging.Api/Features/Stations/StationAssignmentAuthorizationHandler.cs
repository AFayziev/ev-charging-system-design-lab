using EvCharging.Api.Authentication;
using EvCharging.Domain.Equipment;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Stations;

internal sealed class StationAssignmentRequirement : IAuthorizationRequirement
{
    public static StationAssignmentRequirement Instance { get; } = new();
}

internal sealed class StationAssignmentAuthorizationHandler(
    PlatformDbContext database,
    IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<StationAssignmentRequirement, Station>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        StationAssignmentRequirement requirement,
        Station resource)
    {
        var identity = context.User.GetUserIdentity();
        if (identity is null)
        {
            return;
        }

        var cancellationToken = httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        bool assigned = await database.AssignedStationIds(identity.Issuer, identity.Subject)
            .ContainsAsync(resource.Id, cancellationToken);
        if (assigned)
        {
            context.Succeed(requirement);
        }
    }
}
