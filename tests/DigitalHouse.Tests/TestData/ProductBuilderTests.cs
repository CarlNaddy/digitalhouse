using System.ComponentModel.DataAnnotations;
using DigitalHouse.Data;

namespace DigitalHouse.Tests.TestData;

/// <summary>
/// Guards the <see cref="ProductBuilder"/> (openspec:
/// add-digital-asset-marketplace, task 2.1): valid-by-default, overridable, and
/// deterministic for a given seed — except the deliberately-unique certificate
/// id and the slug derived from it.
/// </summary>
public sealed class ProductBuilderTests
{
    [Fact]
    public void Build_produces_a_product_that_passes_data_annotation_validation()
    {
        var product = new ProductBuilder().Build();

        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(
            product, new ValidationContext(product), results, validateAllProperties: true);

        Assert.True(valid, string.Join("; ", results.Select(r => r.ErrorMessage)));
    }

    [Fact]
    public void Build_sets_a_non_zero_price_seed_and_a_backdated_creation_date()
    {
        var product = new ProductBuilder().Build();

        Assert.NotEqual(0, product.PriceSeed);
        Assert.True(product.CreatedAt < DateTimeOffset.UtcNow);
        Assert.Equal(1_000_000, product.CurrentPriceMicros);
        Assert.Equal(0, product.Id);
    }

    [Fact]
    public void Build_gives_each_product_a_distinct_26_char_certificate_id()
    {
        var a = new ProductBuilder().Build();
        var b = new ProductBuilder().Build();

        Assert.Equal(26, a.PublicId.ToString().Length);
        Assert.NotEqual(a.PublicId, b.PublicId);
        Assert.NotEqual(a.Slug, b.Slug);
    }

    [Fact]
    public void The_same_seed_produces_the_same_data_apart_from_the_certificate_id()
    {
        var first = new ProductBuilder(seed: 123).Build();
        var second = new ProductBuilder(seed: 123).Build();

        Assert.Equal(first.Title, second.Title);
        Assert.Equal(first.Description, second.Description);
        Assert.Equal(first.PriceSeed, second.PriceSeed);
        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.NotEqual(first.PublicId, second.PublicId);
    }

    [Fact]
    public void With_overrides_win_over_the_generated_defaults()
    {
        var createdAt = new DateTimeOffset(2015, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var product = new ProductBuilder()
            .WithTitle("Fixed title")
            .WithSlug("fixed-slug")
            .WithPriceSeed(42)
            .WithCreatedAt(createdAt)
            .WithBuybackSpreadCapCents(500)
            .WithCurrentPriceMicros(null)
            .Build();

        Assert.Equal("Fixed title", product.Title);
        Assert.Equal("fixed-slug", product.Slug);
        Assert.Equal(42, product.PriceSeed);
        Assert.Equal(createdAt, product.CreatedAt);
        Assert.Equal(500, product.BuybackSpreadCapCents);
        Assert.Equal(0, product.CurrentPriceMicros);
    }

    [Fact]
    public void BuildMany_returns_the_requested_count_of_products_with_distinct_ids()
    {
        var products = new ProductBuilder().BuildMany(5);

        Assert.Equal(5, products.Count);
        Assert.Equal(5, products.Select(p => p.PublicId).Distinct().Count());
        Assert.Equal(5, products.Select(p => p.Slug).Distinct().Count());
    }
}
