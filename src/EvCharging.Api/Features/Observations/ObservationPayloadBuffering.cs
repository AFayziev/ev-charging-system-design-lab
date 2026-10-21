namespace EvCharging.Api.Features.Observations;

internal static class ObservationPayloadBuffering
{
    private const string CallbackPrefix = "/callbacks";

    // Minimal API binding consumes the request body and offers no way to keep the text it parsed, so the
    // callback routes buffer it, bounded, for the envelope's raw payload.
    public static IApplicationBuilder UseObservationPayloadBuffering(this IApplicationBuilder app) =>
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(CallbackPrefix),
            branch => branch.Use(async (context, next) =>
            {
                context.Request.EnableBuffering(
                    ObservationEndpoints.MaximumPayloadBytes, ObservationEndpoints.MaximumPayloadBytes);
                await next(context);
            }));
}
