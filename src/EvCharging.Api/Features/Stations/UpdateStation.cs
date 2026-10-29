using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Domain.Equipment;
using EvCharging.Messaging;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace EvCharging.Api.Features.Stations;

public sealed record UpdateStationRequest(
    [property: Range(typeof(uint), "1", "4294967295")] uint Version,
    [property: Required, MaxLength(200)] string Name,
    [property: Required, MaxLength(500)] string Address,
    [property: Required(AllowEmptyStrings = true), MaxLength(4000)] string AccessInstructions);

public sealed record UpdateStationCommand(Guid Id, UpdateStationRequest Request)
    : IAuthenticatedRequest<StationEditorResponse>;

public sealed class UpdateStationCommandHandler(
    PlatformDbContext database,
    IDbContextOutbox outbox,
    IAuthorizationService authorization)
{
    public async Task<Result<StationEditorResponse>> HandleAsync(
        UpdateStationCommand command, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var station = await database.Stations
            .SingleOrDefaultAsync(station => station.PublicId == command.Id, cancellationToken);
        if (station is null)
        {
            return Result.Fail<StationEditorResponse>(new NotFoundError<Guid>("station", command.Id));
        }

        var permission = await authorization.AuthorizeAsync(user, station, [StationAssignmentRequirement.Instance]);
        if (!permission.Succeeded)
        {
            return Result.Fail<StationEditorResponse>(new ForbiddenError());
        }

        ApplyEdit(station, command.Request);
        return await SaveStationAsync(station, cancellationToken);
    }

    private void ApplyEdit(Station station, UpdateStationRequest request)
    {
        string name = request.Name.Trim();
        string address = request.Address.Trim();
        string accessInstructions = request.AccessInstructions.Trim();
        station.Name = name;
        station.Address = address;
        station.AccessInstructions = accessInstructions;
        var entry = database.Entry(station);
        entry.Property(station => station.Version).OriginalValue = request.Version;
        // Same-value submissions still check the browser's version and produce an audited edit.
        entry.Property(station => station.Name).IsModified = true;
        entry.Property(station => station.Address).IsModified = true;
        entry.Property(station => station.AccessInstructions).IsModified = true;
    }

    private async Task<Result<StationEditorResponse>> SaveStationAsync(
        Station station, CancellationToken cancellationToken)
    {
        outbox.Enroll(database);
        await outbox.PublishStationChangedAsync(station.Id);

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail<StationEditorResponse>(new ConcurrencyConflictError());
        }

        return Result.Ok(station.ToEditorResponse());
    }
}
