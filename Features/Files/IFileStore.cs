using DigitalHouse.Data;

namespace DigitalHouse.Features.Files;

/// <summary>
/// File storage abstraction (parity plan P4.4, ActiveStorage analog) — the
/// thin app-owned seam between features and wherever bytes actually live.
/// Content-type-agnostic on purpose: validating "this must be an image" is a
/// caller concern (see <c>Features/Listings/ListingPhotoService.cs</c>), not
/// something the store itself should hard-code, since a later feature might
/// store anything.
/// </summary>
public interface IFileStore
{
    Task<StoredFile> SaveAsync(Stream content, string originalFileName, string contentType, CancellationToken ct);

    Task<(Stream Content, StoredFile Metadata)?> OpenReadAsync(Guid id, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}
