using System.Text.RegularExpressions;

namespace DigitalHouse.Tests.Features.Marketplace;

/// <summary>
/// Guards the "who may write a <c>Product</c>'s pricing identity" rule (openspec:
/// add-digital-asset-marketplace, task 3.6). Only an allowlist of files may
/// assign <c>Product.CurrentPriceMicros</c> / <c>.PriceSeed</c> / <c>.PublicId</c>
/// anywhere under the app tree. (<c>Product.CreatedAt</c> is <c>init</c>-only, so
/// the compiler already forbids post-construction writes; the runtime gate in
/// <c>AppDbContext</c> and <c>ProductPersistenceTests</c> cover it too.)
/// </summary>
public sealed partial class ProductImmutabilityArchitectureTests
{
    private static readonly string[] _scannedDirectories = ["Features", "Components", "Endpoints", "Data"];

    private static readonly string[] _allowlist =
    [
        Path.Combine("Data", "Product.cs"),
        Path.Combine("Features", "Marketplace", "PricingEngine.cs"),
        Path.Combine("Data", "Seed", "MarketplaceSeeder.cs"),
        Path.Combine("Features", "Marketplace", "ProductAdminService.cs"),
    ];

    [GeneratedRegex(@"\b(CurrentPriceMicros|PriceSeed|PublicId)\b\s*[+\-*/%&|^]?=(?!=)")]
    private static partial Regex GuardedAssignment();

    [Fact]
    public void Only_allowlisted_files_assign_a_products_pricing_identity()
    {
        var repoRoot = FindRepoRoot();
        var offenders = new List<string>();

        foreach (var dir in _scannedDirectories)
        {
            var path = Path.Combine(repoRoot, dir);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    // EF-generated migration/snapshot code is not hand-authored entity assignment.
                    continue;
                }

                var relative = Path.GetRelativePath(repoRoot, file);
                if (_allowlist.Contains(relative))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                if (GuardedAssignment().IsMatch(text))
                {
                    offenders.Add(relative);
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These files assign a guarded Product pricing-identity member but are not on the allowlist: "
            + string.Join(", ", offenders));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DigitalHouse.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
