using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace DotnetAgenticStarterkit.Tests.Infrastructure;

/// <summary>
/// bUnit context pre-wired for this app's components (rails-parity plan P2.4):
/// MudBlazor services registered and JS interop in loose mode, so menus,
/// dialogs, popovers and the like render without a real browser. Derive a test
/// class from this instead of <see cref="BunitContext"/> directly.
/// </summary>
/// <remarks>
/// A component that opens a popover/menu/dialog still needs the matching provider
/// (<c>MudPopoverProvider</c>, <c>MudDialogProvider</c>) rendered in the test
/// tree — register services here, render providers in the test.
/// </remarks>
public abstract class MudBlazorTestContext : BunitContext
{
    protected MudBlazorTestContext()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
