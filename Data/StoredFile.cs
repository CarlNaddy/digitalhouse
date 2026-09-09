namespace DotnetAgenticStarterkit.Data;

/// <summary>
/// Metadata for a file held by an <see cref="Features.Files.IFileStore"/>
/// (parity plan P4.4) — the bytes live wherever the store implementation puts
/// them (local disk today; a blob provider later), but the metadata always
/// lives here, one row per stored file regardless of provider.
/// </summary>
public class StoredFile
{
    public Guid Id { get; set; }

    public required string OriginalFileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public DateTime UploadedAtUtc { get; set; }
}
