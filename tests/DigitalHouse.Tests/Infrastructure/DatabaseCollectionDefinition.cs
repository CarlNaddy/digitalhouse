namespace DigitalHouse.Tests.Infrastructure;

/// <summary>
/// Binds <see cref="PostgresFixture"/> to a single xUnit collection so every
/// database test shares one container. Tests in the collection run sequentially
/// (xUnit does not parallelise within a collection), which is what makes the
/// per-test <see cref="PostgresFixture.ResetAsync"/> safe.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollectionDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "database";
}
