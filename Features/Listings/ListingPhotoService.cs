using DigitalHouse.Data;
using DigitalHouse.Features.Files;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Listings;

/// <summary>
/// Attaches a photo to a <see cref="Listing"/> — the worked pattern for
/// parity plan P4.4's <see cref="IFileStore"/> abstraction. Shared by the
/// minimal-API ingest endpoint (<c>Endpoints/ListingsApiEndpoints.cs</c>, for
/// external clients) and the Blazor edit page (<c>ListingEdit.razor</c>,
/// called directly via DI — Interactive Server components already run
/// server-side, so there's no reason to round-trip through HTTP just to call
/// the app's own endpoint).
/// </summary>
public class ListingPhotoService(
    IDbContextFactory<AppDbContext> dbFactory,
    IFileStore fileStore,
    ListingQueries listingQueries)
{
    // JPEG: FF D8 FF · PNG: 89 50 4E 47 — magic bytes, not the client-supplied
    // Content-Type header, which is spoofable (dotnet-aspnetcore:minimal-api-file-upload).
    private static readonly (string ContentType, byte[] Magic)[] _allowedImageTypes =
    [
        ("image/jpeg", [0xFF, 0xD8, 0xFF]),
        ("image/png", [0x89, 0x50, 0x4E, 0x47]),
    ];

    public async Task<StoredFile> AttachPhotoAsync(int listingId, Stream content, CancellationToken ct)
    {
        var header = new byte[8];
        var bytesRead = await content.ReadAsync(header, ct);
        var (contentType, _) = _allowedImageTypes.FirstOrDefault(t =>
            bytesRead >= t.Magic.Length && header.AsSpan(0, t.Magic.Length).SequenceEqual(t.Magic));
        if (contentType is null)
        {
            throw new InvalidOperationException("Only JPEG and PNG images are allowed.");
        }

        content.Position = 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var listing = await db.Listings.FindAsync([listingId], ct)
            ?? throw new KeyNotFoundException($"Listing {listingId} not found.");
        var previousPhotoId = listing.PhotoFileId;

        var stored = await fileStore.SaveAsync(content, "photo", contentType, ct);
        listing.PhotoFileId = stored.Id;
        await db.SaveChangesAsync(ct);

        if (previousPhotoId is { } id)
        {
            await fileStore.DeleteAsync(id, ct);
        }

        await listingQueries.InvalidateAsync(ct);
        return stored;
    }

    /// <summary>
    /// Deletes a listing and, if it has one, its photo — the raw
    /// <c>ExecuteDeleteAsync</c> the Blazor pages used before P4.4 would
    /// otherwise orphan the photo's file and <see cref="StoredFile"/> row.
    /// </summary>
    public async Task DeleteListingAsync(int listingId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var photoId = await db.Listings
            .Where(l => l.Id == listingId)
            .Select(l => l.PhotoFileId)
            .SingleOrDefaultAsync(ct);

        await db.Listings.Where(l => l.Id == listingId).ExecuteDeleteAsync(ct);

        if (photoId is { } id)
        {
            await fileStore.DeleteAsync(id, ct);
        }

        await listingQueries.InvalidateAsync(ct);
    }
}
