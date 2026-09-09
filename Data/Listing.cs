using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Data;

public enum ListingStatus
{
    Draft,
    Active,
    UnderOffer,
    Sold,
}

/// <summary>
/// A property listing. First real domain entity — the end-to-end CRUD vehicle
/// for rails-parity plan P1.8.
/// </summary>
public class Listing
{
    public int Id { get; set; }

    [Required]
    [StringLength(120)]
    public string Title { get; set; } = "";

    [Required]
    [StringLength(200)]
    public string Address { get; set; } = "";

    [Required]
    [StringLength(80)]
    public string City { get; set; } = "";

    [Range(1, 100_000_000, ErrorMessage = "Price must be greater than zero.")]
    [Precision(12, 2)]
    public decimal Price { get; set; }

    [Range(0, 20)]
    public int Bedrooms { get; set; }

    [Range(0, 20)]
    public int Bathrooms { get; set; }

    [Display(Name = "Area (m²)")]
    [Range(1, 100_000, ErrorMessage = "Area must be greater than zero.")]
    public int FloorAreaSqm { get; set; }

    public ListingStatus Status { get; set; } = ListingStatus.Draft;

    [Display(Name = "Listed on")]
    [Range(typeof(DateOnly), "2000-01-01", "2100-12-31", ErrorMessage = "Enter a valid date.")]
    public DateOnly ListedOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [StringLength(2000)]
    public string? Description { get; set; }

    /// <summary>The listing's photo, if any — see <see cref="StoredFile"/> (parity plan P4.4).</summary>
    public Guid? PhotoFileId { get; set; }
}
