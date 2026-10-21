using System.Security.Claims;
using System.Text;
using EvCharging.Api.Authentication;
using FluentResults;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace EvCharging.Api.Features.Observations;

internal static class ObservationEndpoints
{
    public const int MaximumPayloadBytes = 16 * 1024;

    public static WebApplication MapObservations(this WebApplication app)
    {
        app.MapPost("/callbacks/wattharbor/observations", ReportWattHarbor)
            .RequireAuthorization(ObservationSourceAuthentication.WattHarborPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(MaximumPayloadBytes));
        return app;
    }

    // Parameter validation runs before this handler (see AddValidation).
    private static async Task<Results<Accepted<ObservationAcceptedResponse>, ProblemHttpResult>> ReportWattHarbor(
        WattHarborObservationRequest observation,
        HttpRequest request,
        ClaimsPrincipal source,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        // The route's policy requires this claim, so it is present for every request that reaches the handler.
        var integrationKey = source.FindFirstValue(ObservationSourceAuthentication.IntegrationKeyClaim)!;
        var rawPayload = await ReadRawPayloadAsync(request, cancellationToken);
        var result = await bus.InvokeAsync<Result<ObservationAcceptedResponse>>(
            new ReportWattHarborObservationCommand(integrationKey, observation, rawPayload), cancellationToken);
        return result.IsFailed
            ? TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Observation not accepted",
                detail: "The observation was not durably accepted. Redeliver it with the same event identity.")
            : TypedResults.Accepted((string?)null, result.Value);
    }

    // The envelope keeps the payload as received; ObservationPayloadBuffering keeps the body readable.
    private static async Task<string> ReadRawPayloadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
