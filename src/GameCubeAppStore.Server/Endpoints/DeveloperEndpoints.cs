using System.Security.Claims;
using GameCubeAppStore.Server.Data;
using GameCubeAppStore.Server.Models;
using GameCubeAppStore.Server.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GameCubeAppStore.Server.Endpoints;

public static class DeveloperEndpoints
{
    public static void MapDeveloperEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/developer").RequireAuthorization(new AuthorizeAttribute
        {
            AuthenticationSchemes = IdentityConstants.BearerScheme
        });

        api.MapGet("/software", async (StoreDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var developerId = GetUserId(user);
            var owned = await db.Software.AsNoTracking()
                .Where(s => s.DeveloperId == developerId)
                .Include(s => s.Releases).Include(s => s.Ratings).Include(s => s.Screenshots)
                .OrderByDescending(s => s.LastUpdatedAtUtc).ToListAsync(ct);
            return Results.Ok(owned.Select(s => new
            {
                s.Id, s.Title, s.Description, s.Category, s.IsPublished,
                s.UploadedAtUtc, s.LastUpdatedAtUtc,
                LatestVersion = s.Releases.OrderByDescending(r => r.UploadedAtUtc)
                    .FirstOrDefault()?.Version,
                ScreenshotCount = s.Screenshots.Count,
                RatingCount = s.Ratings.Count,
                AverageRating = s.Ratings.Count == 0 ? 0 : s.Ratings.Average(r => r.Stars)
            }));
        });

        api.MapGet("/software/{id:guid}", async (Guid id, StoreDbContext db,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            var software = await OwnedSoftware(db, id, GetUserId(user), ct);
            if (software is null) return Results.NotFound();
            return Results.Ok(new
            {
                software.Id, software.Title, software.Description, software.Category,
                software.IsPublished, software.UploadedAtUtc, software.LastUpdatedAtUtc,
                Detail = PublicEndpoints.ToDetail(software)
            });
        });

        api.MapPost("/software", async (SoftwareInput input, StoreDbContext db,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (!ValidSoftware(input)) return Results.BadRequest("Title, description or category invalid.");
            var now = DateTime.UtcNow;
            var software = new Software
            {
                DeveloperId = GetUserId(user), Title = input.Title.Trim(),
                Description = input.Description.Trim(), Category = input.Category.Trim(),
                UploadedAtUtc = now, LastUpdatedAtUtc = now
            };
            db.Software.Add(software);
            await db.SaveChangesAsync(ct);
            return Results.Created("/api/developer/software/" + software.Id, new { software.Id });
        });

        api.MapPut("/software/{id:guid}", async (Guid id, SoftwareInput input,
            StoreDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (!ValidSoftware(input)) return Results.BadRequest("Title, description or category invalid.");
            var software = await OwnedSoftware(db, id, GetUserId(user), ct);
            if (software is null) return Results.NotFound();
            software.Title = input.Title.Trim(); software.Description = input.Description.Trim();
            software.Category = input.Category.Trim();
            software.LastUpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapPost("/software/{id:guid}/publish", async (Guid id, StoreDbContext db,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            var software = await OwnedSoftware(db, id, GetUserId(user), ct);
            if (software is null) return Results.NotFound();
            if (software.Releases.Count == 0)
                return Results.BadRequest("Upload a release before publishing.");
            software.IsPublished = true; software.LastUpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapPost("/software/{id:guid}/unpublish", async (Guid id, StoreDbContext db,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            var software = await OwnedSoftware(db, id, GetUserId(user), ct);
            if (software is null) return Results.NotFound();
            software.IsPublished = false; software.LastUpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapPost("/software/{id:guid}/releases", async (Guid id, HttpRequest request,
            StoreDbContext db, AssetStorage storage, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var software = await OwnedSoftware(db, id, GetUserId(user), ct);
            if (software is null) return Results.NotFound();
            if (!request.HasFormContentType) return Results.BadRequest("Expected multipart form.");
            var form = await request.ReadFormAsync(ct);
            var version = form["version"].ToString().Trim();
            var notes = form["releaseNotes"].ToString().Trim();
            var upload = form.Files.GetFile("file");
            if (!ValidVersion(version) || notes.Length > 2000 || upload is null)
                return Results.BadRequest("Provide version, optional releaseNotes and a .dol/.zip file.");
            if (software.Releases.Any(r => r.Version == version))
                return Results.Conflict("That version already exists.");

            FileMetadata asset;
            try { asset = await storage.SaveAsync(upload, AssetKind.Download, ct); }
            catch (ArgumentException e) { return Results.BadRequest(e.Message); }
            try
            {
                var now = DateTime.UtcNow;
                db.Files.Add(asset);
                db.Releases.Add(new SoftwareRelease
                {
                    SoftwareId = id, Version = version, ReleaseNotes = notes,
                    FileId = asset.Id, UploadedAtUtc = now, LastUpdatedAtUtc = now
                });
                software.LastUpdatedAtUtc = now;
                await db.SaveChangesAsync(ct);
                return Results.Created("/api/developer/software/" + id,
                    new { asset.Id, version, asset.Sha256, asset.SizeBytes });
            }
            catch { storage.Delete(asset); throw; }
        }).DisableAntiforgery();

        api.MapPost("/software/{id:guid}/screenshots", async (Guid id, HttpRequest request,
            StoreDbContext db, AssetStorage storage, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var software = await OwnedSoftware(db, id, GetUserId(user), ct);
            if (software is null) return Results.NotFound();
            if (software.Screenshots.Count >= 8) return Results.BadRequest("Maximum 8 screenshots.");
            if (!request.HasFormContentType) return Results.BadRequest("Expected multipart form.");
            var form = await request.ReadFormAsync(ct);
            var upload = form.Files.GetFile("file");
            if (upload is null) return Results.BadRequest("Provide PNG as file.");
            FileMetadata asset;
            try { asset = await storage.SaveAsync(upload, AssetKind.Screenshot, ct); }
            catch (ArgumentException e) { return Results.BadRequest(e.Message); }
            try
            {
                db.Files.Add(asset);
                db.Screenshots.Add(new SoftwareScreenshot
                {
                    SoftwareId = id, FileId = asset.Id,
                    SortOrder = software.Screenshots.Count == 0
                        ? 0 : software.Screenshots.Max(x => x.SortOrder) + 1
                });
                software.LastUpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.Created("/api/assets/" + asset.Id,
                    new { asset.Id, url = "/api/assets/" + asset.Id });
            }
            catch { storage.Delete(asset); throw; }
        }).DisableAntiforgery();

        api.MapDelete("/software/{id:guid}/screenshots/{shotId:guid}", async (
            Guid id, Guid shotId, StoreDbContext db, AssetStorage storage,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            var software = await OwnedSoftware(db, id, GetUserId(user), ct);
            if (software is null) return Results.NotFound();
            var shot = software.Screenshots.FirstOrDefault(s => s.Id == shotId);
            if (shot is null) return Results.NotFound();
            var file = await db.Files.FindAsync(new object[] { shot.FileId }, ct);
            db.Screenshots.Remove(shot);
            if (file is not null) db.Files.Remove(file);
            software.LastUpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            if (file is not null) storage.Delete(file);
            return Results.NoContent();
        });

        api.MapDelete("/software/{id:guid}/releases/{releaseId:guid}", async (
            Guid id, Guid releaseId, StoreDbContext db, AssetStorage storage,
            ClaimsPrincipal user, CancellationToken ct) =>
        {
            var software = await OwnedSoftware(db, id, GetUserId(user), ct);
            if (software is null) return Results.NotFound();
            var release = software.Releases.FirstOrDefault(r => r.Id == releaseId);
            if (release is null) return Results.NotFound();
            var file = await db.Files.FindAsync(new object[] { release.FileId }, ct);
            db.Releases.Remove(release);
            if (file is not null) db.Files.Remove(file);
            if (software.Releases.Count <= 1) software.IsPublished = false;
            software.LastUpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            if (file is not null) storage.Delete(file);
            return Results.NoContent();
        });
    }

    private static string GetUserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    private static bool ValidSoftware(SoftwareInput input) =>
        !string.IsNullOrWhiteSpace(input.Title) && input.Title.Trim().Length <= 120 &&
        !string.IsNullOrWhiteSpace(input.Description) && input.Description.Trim().Length <= 4000 &&
        !string.IsNullOrWhiteSpace(input.Category) && input.Category.Trim().Length <= 50;

    private static bool ValidVersion(string version) =>
        version.Length is > 0 and <= 40 &&
        version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+');

    private static Task<Software?> OwnedSoftware(StoreDbContext db, Guid id,
        string developerId, CancellationToken ct) =>
        db.Software.Include(x => x.Releases).ThenInclude(x => x.File)
            .Include(x => x.Screenshots).Include(x => x.Ratings)
            .FirstOrDefaultAsync(x => x.Id == id && x.DeveloperId == developerId, ct);
}
