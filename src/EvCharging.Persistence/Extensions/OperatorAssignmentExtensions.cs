namespace EvCharging.Persistence.Extensions;

public static class OperatorAssignmentExtensions
{
    /// <summary>The stations assigned to the operator signed in with this identity.</summary>
    public static IQueryable<Guid> AssignedStationIds(this PlatformDbContext database, string issuer, string subject) =>
        database.OperatorAssignments
            .Where(assignment => database.OperatorUsers.Any(operatorUser => operatorUser.Id == assignment.OperatorUserId
                && operatorUser.IdentityIssuer == issuer
                && operatorUser.IdentitySubject == subject))
            .Select(assignment => assignment.StationId);
}
