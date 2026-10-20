using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Serialization.Json;

namespace GameCubeAppStore.Console;

internal sealed class CatalogItem
{
    public string Id = "";
    public string Title = "";
    public string Description = "";
    public string Category = "";
    public string Version = "";
    public string CoverUrl = "";
    public string AverageRating = "0";
    public int RatingCount;
    public string LastUpdatedAtUtc = "";
}

/// <summary>
/// Anonymous read-only HTTP catalog client. Parsing uses DolphinDotNet's bounded
/// JsonReader instead of System.Text.Json, which is unavailable on GameCube.
/// </summary>
internal sealed class CatalogClient : IDisposable
{
    private readonly HttpClient http = new HttpClient();
    private readonly string origin;

    public CatalogClient(string origin)
    {
        if (!origin.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("GameCube requires an HTTP catalog origin.");
        this.origin = origin.TrimEnd('/');
    }

    public async Task<List<CatalogItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(origin + "/api/catalog", cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return ParseCatalog(json);
    }

    public async Task<string> GetDetailJsonAsync(string id, CancellationToken cancellationToken)
    {
        // Only GUIDs returned by the server are accepted in the URL.
        if (!Guid.TryParse(id, out var guid)) throw new ArgumentException("Invalid software ID.");
        using var response = await http.GetAsync(
            origin + "/api/catalog/" + guid.ToString(), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<byte[]> GetScreenshotAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        if (!relativeUrl.StartsWith("/api/assets/", StringComparison.Ordinal))
            throw new ArgumentException("Screenshot URL must belong to this catalog.");
        return await http.GetByteArrayAsync(origin + relativeUrl, cancellationToken);
    }

    public void Dispose() => http.Dispose();

    private static List<CatalogItem> ParseCatalog(string json)
    {
        var reader = new JsonReader(json);
        var items = new List<CatalogItem>();
        CatalogItem? current = null;
        int depth = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                depth++;
                if (depth == 1) current = new CatalogItem();
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (depth == 1 && current != null)
                {
                    items.Add(current);
                    current = null;
                }
                depth--;
            }
            else if (reader.TokenType == JsonTokenType.PropertyName && depth == 1 && current != null)
            {
                var property = reader.Value;
                if (!reader.Read()) throw new FormatException("Incomplete catalog entry.");
                if (reader.TokenType == JsonTokenType.Null) continue;
                var value = reader.Value;
                if (property == "id") current.Id = value;
                else if (property == "title") current.Title = value;
                else if (property == "description") current.Description = value;
                else if (property == "category") current.Category = value;
                else if (property == "version") current.Version = value;
                else if (property == "coverUrl") current.CoverUrl = value;
                else if (property == "averageRating") current.AverageRating = value;
                else if (property == "ratingCount") current.RatingCount = reader.GetInt32();
                else if (property == "lastUpdatedAtUtc") current.LastUpdatedAtUtc = value;
            }
        }
        return items;
    }
}
