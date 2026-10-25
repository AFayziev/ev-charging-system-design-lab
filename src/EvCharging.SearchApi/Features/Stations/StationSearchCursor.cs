using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Clients.Elasticsearch;

namespace EvCharging.SearchApi.Features.Stations;

/// <summary>
/// Opaque cursor holding the search parameters and the sort values of the last station on the page, which
/// Elasticsearch resumes after. The next page needs nothing but the cursor, so it always continues the same search.
/// </summary>
internal static class StationSearchCursor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Encode(SearchStationsRequest request, IEnumerable<FieldValue> sortValues)
    {
        var after = sortValues.Select(value => JsonSerializer.SerializeToElement(value.Value, Json)).ToArray();
        var payload = new Payload(request with { Cursor = null }, after);
        return Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload, Json));
    }

    /// <summary>Restores the search, which must be as valid as one sent directly, with the caller's page size.</summary>
    public static StationSearchPage? Decode(string cursor, int limit)
    {
        if (cursor.Length > StationSearchLimits.MaximumCursorLength || !Base64Url.IsValid(cursor))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(Base64Url.DecodeFromChars(cursor), Json);
            if (payload?.Request is null || payload.After is null)
            {
                return null;
            }

            var request = payload.Request with { Limit = limit, Cursor = null };
            if (!Validator.TryValidateObject(request, new ValidationContext(request), null, validateAllProperties: true)
                || !TryReadAfter(payload.After, request.Sort, out var after))
            {
                return null;
            }

            return new StationSearchPage(request, after);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TryReadAfter(JsonElement[] items, StationSearchSort sort, out FieldValue[] after)
    {
        after = [];
        var leading = sort == StationSearchSort.Name ? JsonValueKind.String : JsonValueKind.Number;
        if (items.Length != 2
            || items[0].ValueKind != leading
            || items[1].ValueKind != JsonValueKind.String
            || !Guid.TryParse(items[1].GetString(), out _))
        {
            return false;
        }

        after =
        [
            leading == JsonValueKind.String
                ? FieldValue.String(items[0].GetString()!)
                : FieldValue.Double(items[0].GetDouble()),
            FieldValue.String(items[1].GetString()!)
        ];
        return true;
    }

    private sealed record Payload(SearchStationsRequest? Request, JsonElement[]? After);
}
