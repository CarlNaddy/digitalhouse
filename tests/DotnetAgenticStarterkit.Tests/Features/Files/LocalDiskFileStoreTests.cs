using System.Text;
using DotnetAgenticStarterkit.Data;
using DotnetAgenticStarterkit.Features.Files;
using DotnetAgenticStarterkit.Tests.Infrastructure;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DotnetAgenticStarterkit.Tests.Features.Files;

/// <summary>
/// <see cref="LocalDiskFileStore"/> against real Postgres (metadata) and a
/// throwaway temp directory (bytes) — parity plan P4.4.
/// </summary>
public sealed class LocalDiskFileStoreTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private LocalDiskFileStore CreateStore(string tempRoot) =>
        new(CreateDbContextFactory(), Options.Create(new FileStorageOptions { RootPath = "" }),
            new TestHostEnvironment { ContentRootPath = tempRoot });

    [Fact]
    public async Task SaveAsync_then_OpenReadAsync_round_trips_the_content()
    {
        var tempRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var store = CreateStore(tempRoot);
            var bytes = Encoding.UTF8.GetBytes("hello file store");

            StoredFile saved;
            await using (var content = new MemoryStream(bytes))
            {
                saved = await store.SaveAsync(content, "hello.txt", "text/plain", Ct);
            }

            Assert.Equal(bytes.Length, saved.SizeBytes);

            var opened = await store.OpenReadAsync(saved.Id, Ct);
            Assert.NotNull(opened);
            await using var readStream = opened.Value.Content;
            using var reader = new StreamReader(readStream);
            Assert.Equal("hello file store", await reader.ReadToEndAsync(Ct));
            Assert.Equal("text/plain", opened.Value.Metadata.ContentType);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteAsync_removes_both_the_file_and_the_metadata()
    {
        var tempRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var store = CreateStore(tempRoot);
            StoredFile saved;
            await using (var content = new MemoryStream(Encoding.UTF8.GetBytes("bye")))
            {
                saved = await store.SaveAsync(content, "bye.txt", "text/plain", Ct);
            }

            await store.DeleteAsync(saved.Id, Ct);

            Assert.Null(await store.OpenReadAsync(saved.Id, Ct));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task OpenReadAsync_returns_null_for_an_unknown_id()
    {
        var tempRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.Null(await CreateStore(tempRoot).OpenReadAsync(Guid.NewGuid(), Ct));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "DotnetAgenticStarterkit.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
