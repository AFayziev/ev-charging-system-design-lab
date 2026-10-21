using System.Security.Claims;
using EvCharging.Api.Authentication;
using FluentResults;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Wolverine;

namespace EvCharging.Api.Features.Observations;

internal static class ObservationEndpoints
{
    // A reported observation is about 300 bytes; the allowance leaves room for formatting and longer addresses.
    private const int BytesPerObservation = 1024;

    public static WebApplication MapObservations(this WebApplication app)
    {
        var batchSize = app.Services.GetRequiredService<IOptions<ObservationIngestionOptions>>().Value.MaximumBatchSize;
        app.MapPost("/callbacks/wattharbor/observations", ReportWattHarbor)
            .RequireAuthorization(ObservationSourceAuthentication.WattHarborPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(batchSize * BytesPerObservation));
        return app;
    }

    // Parameter validation runs before this handler (see AddValidation).
    private static async Task<Results<Accepted<ObservationsAcceptedResponse>, ProblemHttpResult>> ReportWattHarbor(
        WattHarborObservationBatchRequest batch,
        ClaimsPrincipal source,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        // The route's policy requires this claim, so it is present for every request that reaches the handler.
        var integrationKey = source.FindFirstValue(ObservationSourceAuthentication.IntegrationKeyClaim)!;
        var result = await bus.InvokeAsync<Result<ObservationsAcceptedResponse>>(
            new ReportWattHarborObservationsCommand(integrationKey, batch.Observations), cancellationToken);
        return result.IsFailed
            ? TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Observations not accepted",
                detail: "The observations were not durably accepted. Redeliver them all with the same event identities.")
            : TypedResults.Accepted((string?)null, result.Value);
    }
}
