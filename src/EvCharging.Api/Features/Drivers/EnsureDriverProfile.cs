using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Domain.Identity;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Drivers;

public sealed record EnsureDriverProfileCommand : IAuthenticatedRequest<DriverRegistrationResponse>;

public sealed record DriverRegistrationResponse(bool Created);

internal sealed class IncompleteDriverRegistrationError() : Error("A verified email is required to register.");

public sealed class EnsureDriverProfileCommandHandler(PlatformDbContext database)
{
    public async Task<Result<DriverRegistrationResponse>> HandleAsync(
        EnsureDriverProfileCommand command,
        UserIdentity identity,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var email = principal.GetVerifiedEmail();
        if (email is null)
        {
            return Result.Fail<DriverRegistrationResponse>(new IncompleteDriverRegistrationError());
        }

        if (await database.Drivers.AnyAsync(
            driver => driver.IdentityIssuer == identity.Issuer && driver.IdentitySubject == identity.Subject,
            cancellationToken))
        {
            return Result.Ok(new DriverRegistrationResponse(false));
        }

        var driver = Driver.Register(identity.Issuer, identity.Subject, email);
        database.Drivers.Add(driver);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsDriverIdentityConflict())
        {
            database.Entry(driver).State = EntityState.Detached;
            return Result.Ok(new DriverRegistrationResponse(false));
        }

        return Result.Ok(new DriverRegistrationResponse(true));
    }
}
