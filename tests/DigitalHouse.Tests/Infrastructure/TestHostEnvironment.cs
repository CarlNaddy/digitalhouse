using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace DigitalHouse.Tests.Infrastructure;

/// <summary>
/// Minimal <see cref="IHostEnvironment"/> for tests that call
/// <c>AddMarketplace</c> directly (no <c>WebApplicationFactory</c> host
/// around them). Defaults to Production so those tests don't accidentally
/// pick up <c>DevFakePaymentGateway</c>'s Development-only branch; pass
/// <see cref="Environments.Development"/> to exercise that branch instead.
/// </summary>
public sealed class TestHostEnvironment(string environmentName = "Production") : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "DigitalHouse.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
