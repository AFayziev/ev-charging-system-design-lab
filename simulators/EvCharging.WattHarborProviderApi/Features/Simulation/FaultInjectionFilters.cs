using EvCharging.WattHarborProviderApi.Features.Commands;
using EvCharging.WattHarborProviderApi.Features.Sessions;

namespace EvCharging.WattHarborProviderApi.Features.Simulation;

internal static class FaultInjectionFilters
{
    public static EndpointFilterDelegate ResponseLoss(
        EndpointFilterFactoryContext factory, EndpointFilterDelegate next)
    {
        var parameters = factory.MethodInfo.GetParameters();
        if (!parameters.Any(parameter =>
            parameter.ParameterType == typeof(StartChargingRequest)
            || parameter.ParameterType == typeof(StopChargingRequest)))
        {
            return next;
        }

        int correlationIndex = parameters.Single(parameter => parameter.ParameterType == typeof(Guid)).Position;
        var faults = factory.ApplicationServices.GetRequiredService<CommandFaults>();
        return async invocation =>
        {
            var result = await next(invocation);
            if (faults.Find(invocation.GetArgument<Guid>(correlationIndex)) is { LoseResponse: true })
            {
                // The handler commits the effect before returning; serialization has not started.
                invocation.HttpContext.Abort();
                return TypedResults.Empty;
            }

            return result;
        };
    }

    public static EndpointFilterDelegate LookupFailure(
        EndpointFilterFactoryContext factory, EndpointFilterDelegate next)
    {
        var parameters = factory.MethodInfo.GetParameters();
        bool lookup = parameters.Any(parameter =>
            parameter.ParameterType == typeof(GetCommandByExternalOperationIdQueryHandler)
            || parameter.ParameterType == typeof(GetSessionByExternalSessionIdQueryHandler));
        if (!lookup)
        {
            return next;
        }

        int correlationIndex = parameters.Single(parameter => parameter.ParameterType == typeof(Guid)).Position;
        var faults = factory.ApplicationServices.GetRequiredService<CommandFaults>();
        return invocation => faults.Find(invocation.GetArgument<Guid>(correlationIndex))?.LookupStatusCode is { } status
            ? ValueTask.FromResult<object?>(TypedResults.StatusCode(status))
            : next(invocation);
    }
}
