using System.Buffers.Binary;
using System.Security.Cryptography;
using GameCubeAppStore.Server.Models;

namespace GameCubeAppStore.Server.Storage;

public enum AssetKind { Download, Screenshot }

/// <summary>Stores opaque, randomly keyed files outside wwwroot. Caller persists FileMetadata.</summary>
public sealed class AssetStorage
{
    private const long DownloadLimit = 64L * 1024 * 1024;
    private const long ScreenshotLimit = 256L * 1024;
    private readonly string root;

    public AssetStorage(string root)
    {
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }

    public async Task<FileMetadata> SaveAsync(IFormFile upload, AssetKind kind,
        CancellationToken cancellationToken)
    {
        var limit = kind == AssetKind.Screenshot ? ScreenshotLimit : DownloadLimit;
        if (upload.Length <= 0 || upload.Length > limit)
            throw new ArgumentException("File must be nonempty and within its size limit.");

        var originalName = Path.GetFileName(upload.FileName.Replace('\\', '/'));
        if (originalName.Length is 0 or > 255)
            throw new ArgumentException("Invalid filename.");
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        if (kind == AssetKind.Screenshot && extension != ".png")
            throw new ArgumentException("Screenshots must be PNG files.");
        if (kind == AssetKind.Download && extension is not (".dol" or ".zip"))
            throw new ArgumentException("Downloads must be .dol or .zip files.");

        var storageKey = Guid.NewGuid().ToString("N");
        var absolutePath = Path.Combine(root, storageKey);
        try
        {
            await using (var destination = new FileStream(absolutePath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            await using (var input = upload.OpenReadStream())
                await input.CopyToAsync(destination, cancellationToken);

            var size = new FileInfo(absolutePath).Length;
            if (size == 0 || size > limit || size != upload.Length)
                throw new ArgumentException("Uploaded file size does not match request.");

            if (kind == AssetKind.Screenshot)
                await ValidatePngAsync(absolutePath, cancellationToken);

            await using var verify = File.OpenRead(absolutePath);
            var sha = Convert.ToHexString(await SHA256.HashDataAsync(verify, cancellationToken))
                .ToLowerInvariant();

            return new FileMetadata
            {
                StorageKey = storageKey, OriginalName = originalName,
                ContentType = kind == AssetKind.Screenshot ? "image/png" : "application/octet-stream",
                SizeBytes = size, Sha256 = sha
            };
        }
        catch
        {
            File.Delete(absolutePath);
            throw;
        }
    }

    public string GetPath(FileMetadata file) => Path.Combine(root, file.StorageKey);

    public void Delete(FileMetadata file) => File.Delete(GetPath(file));

    private static async Task ValidatePngAsync(string path, CancellationToken cancellationToken)
    {
        var header = new byte[24];
        await using var stream = File.OpenRead(path);
        if (stream.Length < 33) throw new ArgumentException("PNG is too short.");
        await stream.ReadExactlyAsync(header, cancellationToken);
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (!header.AsSpan(0, 8).SequenceEqual(signature) ||
            BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8, 4)) != 13 ||
            !header.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new ArgumentException("Invalid PNG header.");

        var width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        if (width is < 1 or > 1024 || height is < 1 or > 1024)
            throw new ArgumentException("PNG dimensions must be between 1 and 1024.");
    }
}
