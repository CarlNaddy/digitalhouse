using DigitalHouse.Data;
using DigitalHouse.Features.Marketplace;
using DigitalHouse.Tests.TestData;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// <see cref="CertificateId"/> (openspec: add-digital-asset-marketplace, task
/// 4.3): the full ULID only for the current owner or an admin; a masked form for
/// everyone else.
/// </summary>
public sealed class CertificateIdTests
{
    private static readonly Product _product = ProductBuilder.Valid();
    private static readonly ApplicationUser _owner = new() { Id = "owner", Email = "owner@example.test" };
    private static readonly ApplicationUser _other = new() { Id = "other", Email = "other@example.test" };

    private static AssetOwnership OwnedBy(string userId) =>
        new AssetOwnershipBuilder().ForProduct(_product.Id).HeldBy(userId).Build();

    [Fact]
    public void Masked_reveals_only_the_first_and_last_four_characters()
    {
        var full = _product.PublicId.ToString();
        var masked = CertificateId.Masked(_product.PublicId);

        Assert.Equal(26, masked.Length);
        Assert.StartsWith(full[..4], masked, StringComparison.Ordinal);
        Assert.EndsWith(full[^4..], masked, StringComparison.Ordinal);
        Assert.Equal(new string('•', 18), masked[4..^4]);
        Assert.DoesNotContain(full[4..^4], masked, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_owner_sees_the_full_value()
    {
        Assert.Equal(
            _product.PublicId.ToString(),
            CertificateId.For(_product, _owner, OwnedBy("owner"), isAdmin: false));
    }

    [Fact]
    public void Admin_sees_the_full_value_even_when_not_the_owner()
    {
        Assert.Equal(
            _product.PublicId.ToString(),
            CertificateId.For(_product, _other, OwnedBy("owner"), isAdmin: true));
    }

    [Fact]
    public void A_different_collector_sees_the_masked_value()
    {
        Assert.Equal(
            CertificateId.Masked(_product.PublicId),
            CertificateId.For(_product, _other, OwnedBy("owner"), isAdmin: false));
    }

    [Fact]
    public void A_guest_sees_the_masked_value()
    {
        Assert.Equal(
            CertificateId.Masked(_product.PublicId),
            CertificateId.For(_product, viewer: null, OwnedBy("owner"), isAdmin: false));
    }

    [Fact]
    public void A_former_owner_sees_the_masked_value()
    {
        // The current ownership row belongs to someone else now.
        Assert.Equal(
            CertificateId.Masked(_product.PublicId),
            CertificateId.For(_product, _owner, OwnedBy("new-owner"), isAdmin: false));
    }
}
