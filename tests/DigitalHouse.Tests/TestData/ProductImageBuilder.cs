using DigitalHouse.Data;

namespace DigitalHouse.Tests.TestData;

/// <summary>Fluent builder for <see cref="ProductImage"/> test rows.</summary>
public sealed class ProductImageBuilder
{
    private long _productId;
    private Guid _storedFileId = Guid.NewGuid();
    private int _position;
    private bool _isPrimary;

    public ProductImageBuilder ForProduct(Product product) => ForProduct(product?.Id ?? 0);

    public ProductImageBuilder ForProduct(long productId)
    {
        _productId = productId;
        return this;
    }

    public ProductImageBuilder WithStoredFile(Guid storedFileId)
    {
        _storedFileId = storedFileId;
        return this;
    }

    public ProductImageBuilder AtPosition(int position)
    {
        _position = position;
        return this;
    }

    public ProductImageBuilder AsPrimary(bool isPrimary = true)
    {
        _isPrimary = isPrimary;
        return this;
    }

    public ProductImage Build() => new()
    {
        ProductId = _productId,
        StoredFileId = _storedFileId,
        Position = _position,
        IsPrimary = _isPrimary,
    };
}
