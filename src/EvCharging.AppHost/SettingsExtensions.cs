using System.Globalization;
using System.Linq.Expressions;

namespace EvCharging.AppHost;

internal static class SettingsExtensions
{
    /// <summary>
    /// Passes the chosen properties of a settings model to a service as <c>Section__Property</c> environment
    /// variables, so the service binds the same section and keeps no values of its own. Nested properties become
    /// <c>Section__Parent__Property</c>.
    /// </summary>
    public static IResourceBuilder<TResource> WithSettings<TResource, TSettings>(
        this IResourceBuilder<TResource> resource,
        TSettings settings,
        string section,
        params Expression<Func<TSettings, object?>>[] properties)
        where TResource : IResourceWithEnvironment
    {
        foreach (var property in properties)
        {
            var name = $"{section}__{string.Join("__", PropertyPath(property))}";
            foreach (var (key, value) in Flatten(name, property.Compile()(settings)))
            {
                resource.WithEnvironment(key, value);
            }
        }

        return resource;
    }

    private static IEnumerable<string> PropertyPath(LambdaExpression property)
    {
        // Boxing a value-type property adds a conversion around the member access.
        var expression = property.Body is UnaryExpression { NodeType: ExpressionType.Convert } conversion
            ? conversion.Operand
            : property.Body;
        var path = new Stack<string>();
        while (expression is MemberExpression member)
        {
            path.Push(member.Member.Name);
            expression = member.Expression;
        }

        return path.Count > 0 && expression is ParameterExpression
            ? path
            : throw new ArgumentException("A setting must be a property of the settings model.", nameof(property));
    }

    private static IEnumerable<(string Key, string Value)> Flatten(string key, object? value)
    {
        // Configuration binds a list from indexed keys: Name__0, Name__1, and so on.
        if (value is System.Collections.IEnumerable and not string)
        {
            var index = 0;
            foreach (var item in (System.Collections.IEnumerable)value)
            {
                foreach (var entry in Flatten($"{key}__{index++}", item))
                {
                    yield return entry;
                }
            }

            yield break;
        }

        yield return (key, Format(value));
    }

    private static string Format(object? value) => value switch
    {
        null => throw new InvalidOperationException("A setting passed to a service must have a value."),
        string text => text,
        TimeSpan timeSpan => timeSpan.ToString("c", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        Enum or IFormattable => string.Format(CultureInfo.InvariantCulture, "{0}", value),
        _ => throw new NotSupportedException($"A {value.GetType().Name} cannot be passed as a single setting value.")
    };
}
