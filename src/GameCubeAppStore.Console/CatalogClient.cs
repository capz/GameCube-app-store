using System;
using System.Collections.Generic;
using System.IO;
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

    // Avoid unsupported reflection/culture APIs in the GameCube AOT runtime.
    public int RatingValue
    {
        get
        {
            if(AverageRating.Length==0)return 0;
            int whole = AverageRating[0]>='0'&&AverageRating[0]<='9'
                ? AverageRating[0]-'0' : 0;
            int tenth = AverageRating.Length>=3&&AverageRating[1]=='.'
                &&AverageRating[2]>='0'&&AverageRating[2]<='9'
                ? AverageRating[2]-'0' : 0;
            return whole*10+tenth;
        }
    }
}
internal sealed class CatalogDetailData
{
    public readonly List<string> Screenshots = new List<string>();
    public int ReleaseCount;
    public int ImageIndex;
}

/// <summary>Small, read-only HTTP API designed for DolphinDotNet's limited APIs.</summary>
internal sealed class CatalogClient
{
    private readonly HttpClient http = new HttpClient();
    private readonly string origin;
    public CatalogClient(string origin)
    {
        if(!origin.StartsWith("http://",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("GameCube requires an HTTP-only public catalog origin.");
        this.origin = origin.TrimEnd('/');
    }

    public async Task<List<CatalogItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response=await http.GetAsync(origin+"/api/catalog",cancellationToken);
        response.EnsureSuccessStatusCode();
        var json=await response.Content.ReadAsStringAsync();
        return ParseCatalog(json);
    }

    public async Task<CatalogDetailData> GetDetailAsync(string id,CancellationToken cancellationToken)
    {
        if(!Guid.TryParse(id,out var parsed))throw new ArgumentException("Invalid software ID.");
        using var response=await http.GetAsync(origin+"/api/catalog/"+parsed.ToString(),cancellationToken);
        response.EnsureSuccessStatusCode();
        var json=await response.Content.ReadAsStringAsync();
        return ParseDetail(json);
    }

    public async Task<byte[]> GetScreenshotAsync(string relativeUrl,CancellationToken cancellationToken)
    {
        if(!relativeUrl.StartsWith("/api/assets/",StringComparison.Ordinal) ||
            !Guid.TryParse(relativeUrl.Substring("/api/assets/".Length),out _))
            throw new ArgumentException("Invalid screenshot URL.");
        // Screenshots may exceed the HttpClient 64 KiB buffered-response default.
        // Use a bounded streaming read instead of GetByteArrayAsync.
        using var response=await http.GetAsync(origin+relativeUrl,
            HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream=await response.Content.ReadAsStreamAsync();
        using var data=new MemoryStream();
        var chunk=new byte[4096];
        while(true)
        {
            int n=await stream.ReadAsync(chunk,0,chunk.Length,cancellationToken);
            if(n==0)break;
            if(data.Length+n>256*1024)throw new IOException("Screenshot exceeds 256 KiB.");
            data.Write(chunk,0,n);
        }
        return data.ToArray();
    }


    private static List<CatalogItem> ParseCatalog(string json)
    {
        var reader=new JsonReader(json);
        var items=new List<CatalogItem>();
        CatalogItem? current=null;
        int depth=0;
        while(reader.Read())
        {
            if(reader.TokenType==JsonTokenType.StartObject)
            {
                depth++;
                if(depth==1)current=new CatalogItem();
            }
            else if(reader.TokenType==JsonTokenType.EndObject)
            {
                if(depth==1&&current!=null){items.Add(current);current=null;}
                depth--;
            }
            else if(reader.TokenType==JsonTokenType.PropertyName&&depth==1&&current!=null)
            {
                string key=reader.Value;
                if(!reader.Read())throw new FormatException("Incomplete catalog entry.");
                if(reader.TokenType==JsonTokenType.Null)continue;
                string value=reader.Value;
                if(key=="id")current.Id=value;
                else if(key=="title")current.Title=value;
                else if(key=="description")current.Description=value;
                else if(key=="category")current.Category=value;
                else if(key=="version")current.Version=value;
                else if(key=="coverUrl")current.CoverUrl=value;
                else if(key=="averageRating")current.AverageRating=value;
                else if(key=="ratingCount")current.RatingCount=reader.GetInt32();
                else if(key=="lastUpdatedAtUtc")current.LastUpdatedAtUtc=value;
            }
        }
        return items;
    }

    private static CatalogDetailData ParseDetail(string json)
    {
        var detail=new CatalogDetailData();
        var reader=new JsonReader(json);
        int area=0; // 1: screenshots string array; 2: release object array
        while(reader.Read())
        {
            if(reader.TokenType==JsonTokenType.PropertyName)
            {
                string key=reader.Value;
                if(!reader.Read())throw new FormatException("Incomplete detail.");
                if(reader.TokenType==JsonTokenType.StartArray)
                {
                    if(key=="screenshots")area=1;
                    else if(key=="releases")area=2;
                }
            }
            else if(reader.TokenType==JsonTokenType.EndArray)area=0;
            else if(area==1&&reader.TokenType==JsonTokenType.String)
            {
                var image=reader.Value;
                if(image.StartsWith("/api/assets/",StringComparison.Ordinal)
                    &&Guid.TryParse(image.Substring(12),out _)
                    &&detail.Screenshots.Count<8)detail.Screenshots.Add(image);
            }
            else if(area==2&&reader.TokenType==JsonTokenType.StartObject)
                detail.ReleaseCount++;
        }
        return detail;
    }
}
