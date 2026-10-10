using EvCharging.Api.Persistence;
using EvCharging.Persistence;
using EvCharging.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Stations;

internal sealed class StationAssignmentRequirement : IAuthorizationRequirement
{
    public static StationAssignmentRequirement Instance { get; } = new();
}

internal sealed class StationAssignmentAuthorizationHandler(
    [FromKeyedServices(PlatformPersistenceExtensions.WriteRoute)] PlatformDbContext database,
    IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<StationAssignmentRequirement, Station>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        StationAssignmentRequirement requirement,
        Station resource)
    {
        string? issuer = context.User.FindFirst("iss")?.Value;
        string? subject = context.User.FindFirst("sub")?.Value;
        var cancellationToken = httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        bool assigned = await database.OperatorAssignments
            .AnyAsync(
                assignment => assignment.StationId == resource.Id
                    && database.OperatorUsers.Any(user => user.Id == assignment.OperatorUserId
                        && user.IdentityIssuer == issuer && user.IdentitySubject == subject),
                cancellationToken);
        if (assigned)
        {
            context.Succeed(requirement);
        }
    }
}
