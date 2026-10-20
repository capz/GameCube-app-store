using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using GameCubeAppStore.Server.Data;
using GameCubeAppStore.Server.Models;
using GameCubeAppStore.Server.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GameCubeAppStore.Server.Endpoints;

public static class PublicEndpoints
{
    public static void MapPublicEndpoints(this WebApplication app)
    {
        app.MapGet("/api/catalog", async (StoreDbContext db, CancellationToken ct) =>
        {
            var software = await db.Software.AsNoTracking()
                .Where(s => s.IsPublished)
                .Include(s => s.Releases).Include(s => s.Screenshots).Include(s => s.Ratings)
                .OrderByDescending(s => s.LastUpdatedAtUtc).Take(25).ToListAsync(ct);
            return Results.Ok(software.Select(ToEntry).ToArray());
        });

        app.MapGet("/api/catalog/{id:guid}", async (Guid id, StoreDbContext db, CancellationToken ct) =>
        {
            var software = await db.Software.AsNoTracking()
                .Include(s => s.Releases).ThenInclude(r => r.File)
                .Include(s => s.Screenshots).Include(s => s.Ratings)
                .FirstOrDefaultAsync(s => s.Id == id && s.IsPublished, ct);
            return software is null ? Results.NotFound() : Results.Ok(ToDetail(software));
        });

        app.MapGet("/api/assets/{id:guid}", async (Guid id, StoreDbContext db, AssetStorage storage,
            ClaimsPrincipal user, HttpContext context, CancellationToken ct) =>
        {
            var asset = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
            if (asset is null) return Results.NotFound();

            var publicFile = await db.Releases.AnyAsync(r => r.FileId == id && r.Software.IsPublished, ct)
                || await db.Screenshots.AnyAsync(s => s.FileId == id && s.Software.IsPublished, ct);
            if (!publicFile)
            {
                // Private draft assets are only visible to their developer with a bearer token.
                var authenticated = await context.AuthenticateAsync(IdentityConstants.BearerScheme);
                var ownerId = authenticated.Succeeded
                    ? authenticated.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) : null;
                var ownsAsset = ownerId is not null &&
                    (await db.Releases.AnyAsync(r => r.FileId == id && r.Software.DeveloperId == ownerId, ct)
                    || await db.Screenshots.AnyAsync(s => s.FileId == id && s.Software.DeveloperId == ownerId, ct));
                if (!ownsAsset) return Results.NotFound();
            }

            var path = storage.GetPath(asset);
            if (!File.Exists(path)) return Results.NotFound();
            context.Response.Headers.CacheControl = publicFile ? "public, max-age=3600" : "no-store";
            return Results.File(path, asset.ContentType, asset.ContentType == "image/png" ? null : asset.OriginalName, enableRangeProcessing: true);
        });

        // Account is required to prevent unauthenticated rating spam. Console browsing is anonymous.
        app.MapPut("/api/catalog/{id:guid}/rating", async (Guid id, RatingInput request,
            StoreDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (request.Stars is < 1 or > 5) return Results.BadRequest("Stars must be 1–5.");
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Results.Unauthorized();

            var software = await db.Software.FirstOrDefaultAsync(s => s.Id == id && s.IsPublished, ct);
            if (software is null) return Results.NotFound();
            if (software.DeveloperId == userId)
                return Results.BadRequest("Developers cannot rate their own software.");

            var rating = await db.Ratings.FirstOrDefaultAsync(
                r => r.SoftwareId == id && r.UserId == userId, ct);
            var now = DateTime.UtcNow;
            if (rating is null)
            {
                rating = new SoftwareRating
                {
                    SoftwareId = id, UserId = userId, Stars = request.Stars,
                    UploadedAtUtc = now, LastUpdatedAtUtc = now
                };
                db.Ratings.Add(rating);
            }
            else
            {
                rating.Stars = request.Stars;
                rating.LastUpdatedAtUtc = now;
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(new AuthorizeAttribute
        {
            AuthenticationSchemes = IdentityConstants.BearerScheme
        });
    }

    internal static CatalogEntry ToEntry(Software s)
    {
        var ratingCount = s.Ratings.Count;
        return new CatalogEntry(s.Id, s.Title, s.Description.Length > 180 ? s.Description.Substring(0, 180) : s.Description, s.Category,
            s.Releases.OrderByDescending(r => r.UploadedAtUtc).FirstOrDefault()?.Version,
            s.Screenshots.OrderBy(x => x.SortOrder)
                .Select(x => "/api/assets/" + x.FileId).FirstOrDefault(),
            ratingCount == 0 ? 0 : s.Ratings.Average(r => r.Stars),
            ratingCount, s.LastUpdatedAtUtc);
    }

    internal static CatalogDetail ToDetail(Software s)
    {
        var count = s.Ratings.Count;
        return new CatalogDetail(s.Id, s.Title, s.Description, s.Category,
            s.UploadedAtUtc, s.LastUpdatedAtUtc,
            count == 0 ? 0 : s.Ratings.Average(r => r.Stars), count,
            s.Screenshots.OrderBy(x => x.SortOrder).Take(8)
                .Select(x => "/api/assets/" + x.FileId).ToArray(),
            s.Releases.OrderByDescending(r => r.UploadedAtUtc).Take(20)
                .Select(r => new ReleaseEntry(r.Id, r.Version, r.ReleaseNotes,
                    "/api/assets/" + r.FileId, r.File.Sha256, r.File.SizeBytes,
                    r.UploadedAtUtc, r.LastUpdatedAtUtc)).ToArray());
    }
}
