using Microsoft.AspNetCore.Identity;

namespace GameCubeAppStore.Server.Models;

public sealed class DeveloperUser : IdentityUser
{
    public ICollection<Software> Software { get; set; } = new List<Software>();
}

public sealed class Software
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DeveloperId { get; set; } = "";
    public DeveloperUser Developer { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Utility";
    public bool IsPublished { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<SoftwareRelease> Releases { get; set; } = new List<SoftwareRelease>();
    public ICollection<SoftwareScreenshot> Screenshots { get; set; } = new List<SoftwareScreenshot>();
    public ICollection<SoftwareRating> Ratings { get; set; } = new List<SoftwareRating>();
}

public sealed class FileMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StorageKey { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SoftwareRelease
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SoftwareId { get; set; }
    public Software Software { get; set; } = null!;
    public string Version { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public Guid FileId { get; set; }
    public FileMetadata File { get; set; } = null!;
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SoftwareScreenshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SoftwareId { get; set; }
    public Software Software { get; set; } = null!;
    public Guid FileId { get; set; }
    public FileMetadata File { get; set; } = null!;
    public int SortOrder { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SoftwareRating
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SoftwareId { get; set; }
    public Software Software { get; set; } = null!;
    public string UserId { get; set; } = "";
    public DeveloperUser User { get; set; } = null!;
    public int Stars { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
