using DotnetAgenticStarterkit.Features.Files;

namespace DotnetAgenticStarterkit.Endpoints;

/// <summary>
/// Serves whatever <see cref="IFileStore"/> holds (parity plan P4.4) —
/// public, matching the "public to read" story the <see cref="Data.Listing"/>
/// photos it currently serves already have (P3.5). No provider-specific code
/// here; this endpoint works unchanged if <c>IFileStore</c>'s registration
/// ever swaps from <see cref="LocalDiskFileStore"/> to a blob provider.
/// </summary>
public static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/files/{id:guid}", async (Guid id, IFileStore fileStore, CancellationToken ct) =>
        {
            var result = await fileStore.OpenReadAsync(id, ct);
            return result is null
                ? Results.NotFound()
                : Results.File(result.Value.Content, result.Value.Metadata.ContentType, result.Value.Metadata.OriginalFileName);
        });

        return app;
    }
}
