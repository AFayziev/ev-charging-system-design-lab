using Elastic.Clients.Elasticsearch;

namespace EvCharging.SearchApi.Features.Stations;

/// <summary>A search to run: the parameters and, for a later page, the sort values to resume after.</summary>
internal sealed record StationSearchPage(SearchStationsRequest Request, FieldValue[] After);
