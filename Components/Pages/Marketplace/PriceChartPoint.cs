namespace DigitalHouse.Components.Pages.Marketplace;

/// <summary>One sampled point on the price-history chart (openspec:
/// add-digital-asset-marketplace) — a timestamp and the USD price at it.</summary>
public readonly record struct PriceChartPoint(DateTimeOffset At, decimal PriceUsd);
