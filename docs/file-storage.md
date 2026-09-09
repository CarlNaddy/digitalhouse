# File storage

The `rails-parity` **P4.4** batteries item (ActiveStorage analog).

## Decision: `IFileStore`, local disk now, config-driven provider swap

- **`IFileStore`** (`Features/Files/IFileStore.cs`) is the thin app-owned
  seam — content-type-agnostic on purpose (validating "this must be an
  image" is a caller concern, e.g. `ListingPhotoService`, not the store's;
  a later feature might store anything).
- **`LocalDiskFileStore`** is the only implementation today. Bytes go under
  `FileStorage:RootPath` (default `App_Data/uploads`, gitignored — dev-machine
  local, not repo content), named by the `StoredFile`'s own `Guid` — no
  path-traversal surface, no filename-collision handling needed.
- **Swap point:** `Program.cs` reads `FileStorage:Provider` (default
  `"LocalDisk"`) and registers the matching `IFileStore` implementation.
  Adding a blob provider (Azure/S3) later means adding a case to that switch
  and a new class — every caller (`ListingPhotoService`, `FileEndpoints`)
  keeps working unchanged, since they only ever see `IFileStore`.
- **Metadata always lives in Postgres** (`StoredFile` — id, original
  filename, content type, size, upload time), regardless of which provider
  stores the bytes.
- **Ingest:** `dotnet-aspnetcore:minimal-api-file-upload` — see "How it's
  wired" below for exactly which of its rules applied here.

## How it's wired

```
Features/Files/
  IFileStore.cs
  FileStorageOptions.cs     # Provider, RootPath — bound from config "FileStorage"
  LocalDiskFileStore.cs
Features/Listings/
  ListingPhotoService.cs    # the worked pattern — attach/replace/delete a Listing's photo
Endpoints/
  ListingsApiEndpoints.cs   # POST /api/listings/{id}/photo
  FileEndpoints.cs          # GET  /api/files/{id}
```

- **The worked pattern:** attaching a photo to a `Listing`.
  `ListingPhotoService.AttachPhotoAsync` validates the content by **magic
  bytes** (JPEG `FF D8 FF`, PNG `89 50 4E 47`), not the client-supplied
  `Content-Type` header, which is spoofable — the skill's explicit warning.
  Replacing a photo deletes the old file; `DeleteListingAsync` deletes a
  listing's photo too, not just the row (see "A bug this caught" below).
- **Two callers, one service:** `POST /api/listings/{id}/photo` (external/API
  clients) and `ListingEdit.razor`'s upload handler (the app's own UI) both
  call `ListingPhotoService.AttachPhotoAsync` — the endpoint parses the
  `IFormFile` and calls it; the Blazor Interactive Server component calls it
  **directly via DI**, no HTTP round-trip. That's deliberate, not a missed
  chance to reuse the endpoint: Interactive Server already runs server-side,
  so routing through HTTP would mean the server calling itself, and would
  hit the classic "Blazor Server calling its own API loses the auth cookie"
  pitfall for no benefit.
- **`GET /api/files/{id}`** is public (matches the "public to read" story
  the `Listing` photos it currently serves already have, P3.5) and
  provider-agnostic — it only ever calls `IFileStore.OpenReadAsync`.
- **The upload endpoint is cookie-authenticated (`ListingsWriter`), so
  antiforgery protection stays ON** — `dotnet-aspnetcore:minimal-api-file-upload`'s
  explicit warning against `.DisableAntiforgery()` on a
  cookie-authenticated endpoint (that's safe only for
  unauthenticated/JWT-bearer endpoints). A caller needs a valid
  `__RequestVerificationToken` alongside the file.
- **Size limits — both configured, deliberately at different scopes**
  (the skill's "only configuring one limit" and "one global limit constrains
  everything" mistakes, avoided two different ways):
  - `FormOptions.MultipartBodyLengthLimit` is global (5 MB) — there's no
    per-endpoint override for it, but this app has exactly one multipart
    form (the photo upload), so global is scoped in practice.
  - The Kestrel-level request size limit stays at its **framework default**
    globally, narrowed to 5 MB **only on the photo endpoint** via
    `[RequestSizeLimit(5 * 1024 * 1024)]`. Lowering Kestrel's global default
    would silently cap every other endpoint in the app too.
- **A bug this caught:** both `Listing` delete handlers
  (`Listings.razor`'s grid delete, `ListingDetails.razor`'s own delete) used
  a raw `ExecuteDeleteAsync` with no photo cleanup — once a listing could
  have a photo, that would orphan the file and its `StoredFile` row forever.
  `ListingDetails.razor`'s delete also never invalidated the P4.3 API cache
  (`Listings.razor`'s did; the second, independent delete path was missed
  when P4.3 landed). Both are now `ListingPhotoService.DeleteListingAsync`,
  which handles the photo file, the cache invalidation, and the row delete
  in one place instead of three call sites each getting it slightly
  differently right.

## Testing

`LocalDiskFileStoreTests` (`tests/DigitalHouse.Tests/Features/Files/`) —
save/read/delete round-trips against real Postgres (metadata) and a
throwaway temp directory (bytes), P2.3 `DatabaseTest` pattern.
`ListingPhotoServiceTests` (`tests/DigitalHouse.Tests/Features/Listings/`) —
the actual business logic: magic-byte rejection, attach/replace, the
missing-listing case, and delete-with-photo-cleanup — against a *real*
`LocalDiskFileStore`, not a fake, matching how the rest of this suite tests
against real infrastructure rather than mocks.

## Verified end-to-end (2026-09-02)

Against the real Docker Postgres, a running `dotnet run`, real `curl`
(antiforgery token + auth cookie, the established pattern):

- Unauthenticated `POST /api/listings/1/photo` → `302` (challenge redirect).
- Authenticated, no antiforgery token → `400`.
- Authenticated, with token, a real (minimal, hand-built) PNG → `200`,
  returns the new file's id; the listing's `photoFileId` in
  `GET /api/listings/1` updates to match.
- `GET /api/files/{id}` → the exact bytes back, byte-for-byte identical
  (`cmp`), correct `Content-Type: image/png`.
- Authenticated, with token, non-image content (plain text) → `400`, the
  service's own `"Only JPEG and PNG images are allowed."` message — the
  magic-byte check firing through the real HTTP layer, not just the unit
  test.
- A 6 MB upload against the 5 MB `[RequestSizeLimit]` → `400`, inner
  exception `"Request body too large"` — Kestrel's own size enforcement,
  confirmed by exact wording, not just a generic failure.
- `LocalDiskFileStoreTests` (3) + `ListingPhotoServiceTests` (4) pass.
  `dotnet test` → 29/29 (was 22), clean build with analyzers, `dotnet format
  --verify-no-changes` clean.
