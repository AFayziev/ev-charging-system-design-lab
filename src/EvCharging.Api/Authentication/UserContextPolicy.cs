using JasperFx;
using JasperFx.CodeGeneration;
using Wolverine.Configuration;
using Wolverine.Middleware;
using Wolverine.Runtime.Handlers;

namespace EvCharging.Api.Authentication;

internal sealed class UserContextPolicy : IHandlerPolicy
{
    public void Apply(
        IReadOnlyList<HandlerChain> chains,
        GenerationRules rules,
        IServiceContainer container)
    {
        foreach (var chain in chains)
        {
            if (!typeof(IUserAwareRequest).IsAssignableFrom(chain.MessageType))
            {
                continue;
            }

            var middleware = new MiddlewarePolicy();
            middleware.AddType(typeof(RequestUserMiddleware));

            var responseType = FindRequiredResponseType(chain.MessageType);
            if (responseType is not null)
            {
                var guardType = typeof(RequireUserMiddleware<>).MakeGenericType(responseType);
                middleware.AddType(guardType);
            }

            middleware.Apply([chain], rules, container);
        }
    }

    private static Type? FindRequiredResponseType(Type messageType)
    {
        var markers = messageType.GetInterfaces()
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAuthenticatedRequest<>))
            .ToArray();

        return markers.Length switch
        {
            0 => null,
            1 => markers[0].GetGenericArguments()[0],
            _ => throw new InvalidOperationException(
                $"{messageType.FullName} declares multiple authenticated response types.")
        };
    }
}
