using System.ComponentModel.DataAnnotations;
using EvCharging.SearchApi.Configuration;

namespace Microsoft.Extensions.Hosting;

internal static class SearchIndexClientExtension
{
    public static IHostApplicationBuilder AddSearchIndexClient(this IHostApplicationBuilder builder)
    {
        var options = new SearchApiOptions();
        builder.Configuration.GetRequiredSection(SearchApiOptions.SectionName).Bind(options);
        Validator.ValidateObject(options, new ValidationContext(options), validateAllProperties: true);
        builder.Services.AddSingleton(Options.Options.Create(options));

        builder.AddElasticsearchClient(
            "search-reader",
            // The health check would tie the service's readiness to the index; an index outage is answered per request.
            settings => settings.DisableHealthChecks = true,
            clientSettings => clientSettings.RequestTimeout(options.RequestTimeout));
        return builder;
    }
}
