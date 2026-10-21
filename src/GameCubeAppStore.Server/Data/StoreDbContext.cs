using GameCubeAppStore.Server.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GameCubeAppStore.Server.Data;

public sealed class StoreDbContext(DbContextOptions<StoreDbContext> options)
    : IdentityDbContext<DeveloperUser>(options)
{
    public DbSet<Software> Software => Set<Software>();
    public DbSet<SoftwareRelease> Releases => Set<SoftwareRelease>();
    public DbSet<SoftwareScreenshot> Screenshots => Set<SoftwareScreenshot>();
    public DbSet<SoftwareRating> Ratings => Set<SoftwareRating>();
    public DbSet<FileMetadata> Files => Set<FileMetadata>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Software>(b =>
        {
            b.Property(x => x.Title).HasMaxLength(120).IsRequired();
            b.Property(x => x.Description).HasMaxLength(4000).IsRequired();
            b.Property(x => x.Category).HasMaxLength(50).IsRequired();
            b.HasOne(x => x.Developer).WithMany(x => x.Software).HasForeignKey(x => x.DeveloperId);
            b.HasIndex(x => new { x.IsPublished, x.LastUpdatedAtUtc });
        });
        builder.Entity<FileMetadata>(b =>
        {
            b.Property(x => x.StorageKey).HasMaxLength(64).IsRequired();
            b.HasIndex(x => x.StorageKey).IsUnique();
            b.Property(x => x.OriginalName).HasMaxLength(255).IsRequired();
            b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        });
        builder.Entity<SoftwareRelease>(b =>
        {
            b.Property(x => x.Version).HasMaxLength(40).IsRequired();
            b.Property(x => x.ReleaseNotes).HasMaxLength(2000).IsRequired();
            b.HasIndex(x => new { x.SoftwareId, x.Version }).IsUnique();
            b.HasOne(x => x.Software).WithMany(x => x.Releases).HasForeignKey(x => x.SoftwareId);
            b.HasOne(x => x.File).WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SoftwareScreenshot>(b =>
        {
            b.HasOne(x => x.Software).WithMany(x => x.Screenshots).HasForeignKey(x => x.SoftwareId);
            b.HasOne(x => x.File).WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SoftwareRating>(b =>
        {
            b.HasIndex(x => new { x.SoftwareId, x.UserId }).IsUnique();
            b.HasOne(x => x.Software).WithMany(x => x.Ratings).HasForeignKey(x => x.SoftwareId);
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });
    }
}
