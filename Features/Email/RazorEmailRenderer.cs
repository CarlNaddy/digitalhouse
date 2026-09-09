using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace DotnetAgenticStarterkit.Features.Email;

/// <summary>
/// Renders a Razor component to an HTML string — the "Razor-templated bodies"
/// half of parity plan P4.2. <see cref="HtmlRenderer"/> (the official,
/// first-party way to render Razor components outside of a request, .NET 8+)
/// is registered scoped in <c>Program.cs</c>; email templates are plain
/// components under <c>Features/Email/Templates/</c>, no <c>@page</c>
/// directive and no app layout.
/// </summary>
public sealed class RazorEmailRenderer(HtmlRenderer htmlRenderer)
{
    public Task<string> RenderAsync<TComponent>(IDictionary<string, object?> parameters)
        where TComponent : IComponent =>
        htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameterView = ParameterView.FromDictionary(parameters);
            var output = await htmlRenderer.RenderComponentAsync<TComponent>(parameterView);
            return output.ToHtmlString();
        });
}
