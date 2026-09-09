namespace DotnetAgenticStarterkit.Features.Console;

/// <summary>
/// Entry point for <c>dotnet run -- console</c> — runs <see cref="Scratch.RunAsync"/>
/// against the real, fully-configured app, then exits. See <see cref="Scratch"/>
/// for the actual "rails console" substitute (parity plan P6.2).
/// </summary>
public static class ConsoleCommand
{
    public const string Verb = "console";

    public static async Task RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await Scratch.RunAsync(scope.ServiceProvider, CancellationToken.None);
    }
}
