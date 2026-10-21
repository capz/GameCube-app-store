namespace GameCubeAppStore.Server.Models;

// Explicit, small contract for DolphinDotNet's bounded, reflection-free JSON reader.
public sealed record CatalogEntry(
    Guid Id, string Title, string Description, string Category,
    string? Version, string? CoverUrl, double AverageRating, int RatingCount,
    DateTime LastUpdatedAtUtc);

public sealed record CatalogDetail(
    Guid Id, string Title, string Description, string Category,
    DateTime UploadedAtUtc, DateTime LastUpdatedAtUtc,
    double AverageRating, int RatingCount,
    IReadOnlyList<string> Screenshots, IReadOnlyList<ReleaseEntry> Releases);

public sealed record ReleaseEntry(
    Guid Id, string Version, string ReleaseNotes, string DownloadUrl,
    string Sha256, long SizeBytes, DateTime UploadedAtUtc,
    DateTime LastUpdatedAtUtc, string FileName);

public sealed record SoftwareInput(string Title, string Description, string Category);
public sealed record RatingInput(int Stars);
