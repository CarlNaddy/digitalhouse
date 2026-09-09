using Bunit;
using DigitalHouse.Components.Pages.Listings;
using DigitalHouse.Tests.Infrastructure;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace DigitalHouse.Tests.Components;

/// <summary>
/// The P2.4 bUnit smoke test: render a real MudBlazor component
/// (<see cref="DeleteListingDialog"/>) and drive a user interaction through it.
/// </summary>
public sealed class DeleteListingDialogTests : MudBlazorTestContext
{
    [Fact]
    public async Task Renders_the_listing_title_in_the_confirmation_prompt()
    {
        var provider = Render<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();

        var parameters = new DialogParameters<DeleteListingDialog>
        {
            { x => x.Title, "Sunny 3-bed near the park" },
        };
        await provider.InvokeAsync(() =>
            dialogService.ShowAsync<DeleteListingDialog>("Delete listing", parameters));
        provider.Render();

        Assert.Contains("Sunny 3-bed near the park", provider.Markup);
    }

    [Fact]
    public async Task Clicking_delete_closes_the_dialog_with_a_positive_result()
    {
        var provider = Render<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();

        var parameters = new DialogParameters<DeleteListingDialog>
        {
            { x => x.Title, "Riverside loft, 2 bed" },
        };

        IDialogReference reference = null!;
        await provider.InvokeAsync(async () =>
            reference = await dialogService.ShowAsync<DeleteListingDialog>("Delete listing", parameters));
        provider.Render();

        var deleteButton = provider.FindAll("button")
            .Single(b => b.TextContent.Trim() == "Delete");
        await deleteButton.ClickAsync(new MouseEventArgs());

        var result = await reference.Result;
        Assert.NotNull(result);
        Assert.False(result.Canceled);
        Assert.Equal(true, result.Data);
    }
}
