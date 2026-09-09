using DotnetAgenticStarterkit.Features.Email;
using DotnetAgenticStarterkit.Features.Email.Templates;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotnetAgenticStarterkit.Tests.Features.Email;

/// <summary>
/// The pure, deterministic half of parity plan P4.2's email feature — no SMTP,
/// no database. <see cref="RazorEmailRenderer"/> renders a template component
/// to HTML the same way <see cref="MailKitEmailSender"/> does; verifying the
/// rendered output here doesn't require sending anything.
/// </summary>
public sealed class RazorEmailRendererTests
{
    [Fact]
    public async Task RenderAsync_renders_the_link_into_the_confirmation_template()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var htmlRenderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var renderer = new RazorEmailRenderer(htmlRenderer);

        var html = await renderer.RenderAsync<ConfirmationEmail>(
            new Dictionary<string, object?>
            {
                [nameof(ConfirmationEmail.Link)] = "https://example.test/confirm?code=abc",
            });

        Assert.Contains("https://example.test/confirm?code=abc", html);
        Assert.Contains("Confirm your email", html);
    }

    [Fact]
    public async Task RenderAsync_renders_the_code_into_the_reset_code_template()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var htmlRenderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var renderer = new RazorEmailRenderer(htmlRenderer);

        var html = await renderer.RenderAsync<PasswordResetCodeEmail>(
            new Dictionary<string, object?> { [nameof(PasswordResetCodeEmail.Code)] = "123456" });

        Assert.Contains("123456", html);
    }
}
