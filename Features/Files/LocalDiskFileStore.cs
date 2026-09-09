using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigitalHouse.Features.Files;

/// <summary>
/// <see cref="IFileStore"/> over the local filesystem — the only provider
/// today (parity plan P4.4). Bytes go under <see cref="FileStorageOptions.RootPath"/>,
/// named by the <see cref="StoredFile"/>'s own <c>Guid</c> (content-addressed
/// by id, not by the original filename — no path-traversal surface, no
/// collision handling needed). Metadata lives in <see cref="AppDbContext"/>
/// regardless of which provider stores the bytes.
/// </summary>
public class LocalDiskFileStore(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<FileStorageOptions> options,
    IHostEnvironment env) : IFileStore
{
    private readonly string _root = Path.Combine(env.ContentRootPath, options.Value.RootPath);

    public async Task<StoredFile> SaveAsync(Stream content, string originalFileName, string contentType, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);

        var stored = new StoredFile
        {
            Id = Guid.NewGuid(),
            OriginalFileName = originalFileName,
            ContentType = contentType,
            UploadedAtUtc = DateTime.UtcNow,
        };

        var path = PathFor(stored.Id);
        await using (var file = File.Create(path))
        {
            await content.CopyToAsync(file, ct);
            stored.SizeBytes = file.Length;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.StoredFiles.Add(stored);
        await db.SaveChangesAsync(ct);

        return stored;
    }

    public async Task<(Stream Content, StoredFile Metadata)?> OpenReadAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var metadata = await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, ct);
        var path = PathFor(id);
        if (metadata is null || !File.Exists(path))
        {
            return null;
        }

        return (File.OpenRead(path), metadata);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.StoredFiles.Where(f => f.Id == id).ExecuteDeleteAsync(ct);

        var path = PathFor(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(Guid id) => Path.Combine(_root, id.ToString("N"));
}
