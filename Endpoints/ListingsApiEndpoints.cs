using DotnetAgenticStarterkit.Data;
using DotnetAgenticStarterkit.Features.Listings;
using Microsoft.AspNetCore.Mvc;

namespace DotnetAgenticStarterkit.Endpoints;

/// <summary>
/// JSON API over <see cref="Listing"/> — reads (P4.3's worked surface for
/// caching + rate limiting) and, since P4.4, a photo-upload endpoint
/// (<c>dotnet-aspnetcore:minimal-api-file-upload</c>). Reads are public,
/// unauthenticated, matching the Blazor pages' "public to read" policy
/// (P3.5); the upload is gated to <c>ListingsWriter</c> like every other
/// write.
/// </summary>
public static class ListingsApiEndpoints
{
    public static IEndpointRouteBuilder MapListingsApiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/listings")
            .CacheOutput("Listings")
            .RequireRateLimiting("Api");

        group.MapGet("/", async (ListingQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetAllAsync(ct)));

        group.MapGet("/{id:int}", async (int id, ListingQueries queries, CancellationToken ct) =>
        {
            var listing = await queries.GetByIdAsync(id, ct);
            return listing is null ? Results.NotFound() : Results.Ok(listing);
        });

        // P4.4: cookie-authenticated (ListingsWriter), so antiforgery stays ON —
        // disabling it here would remove CSRF protection from a write endpoint
        // (dotnet-aspnetcore:minimal-api-file-upload's explicit warning). The
        // Blazor edit page doesn't call this — it uses ListingPhotoService
        // directly (see ListingEdit.razor) — this is for external/API clients.
        group.MapPost("/{id:int}/photo", [RequestSizeLimit(5 * 1024 * 1024)] async (
            int id, IFormFile file, ListingPhotoService photos, CancellationToken ct) =>
        {
            try
            {
                await using var stream = file.OpenReadStream();
                var stored = await photos.AttachPhotoAsync(id, stream, ct);
                return Results.Ok(new { stored.Id });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        }).RequireAuthorization("ListingsWriter");

        return app;
    }
}
