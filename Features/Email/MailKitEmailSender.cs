using DotnetAgenticStarterkit.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DotnetAgenticStarterkit.Features.Email;

/// <summary>
/// Sends the Identity-triggered emails (parity plan P4.2) via MailKit, over
/// the SMTP settings in <see cref="EmailOptions"/>. Implements the built-in
/// ASP.NET Core Identity <see cref="IEmailSender{TUser}"/> seam — the
/// canonical hook Identity's own scaffolded pages use — rather than a
/// bespoke interface; MailKit is the transport underneath it.
/// </summary>
public class MailKitEmailSender(
    IOptions<EmailOptions> options,
    RazorEmailRenderer renderer,
    ILogger<MailKitEmailSender> logger) : IEmailSender<ApplicationUser>
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var body = await renderer.RenderAsync<Templates.ConfirmationEmail>(
            new Dictionary<string, object?> { [nameof(Templates.ConfirmationEmail.Link)] = confirmationLink });
        await SendAsync(email, "Confirm your email", body);
    }

    public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        var body = await renderer.RenderAsync<Templates.PasswordResetEmail>(
            new Dictionary<string, object?> { [nameof(Templates.PasswordResetEmail.Link)] = resetLink });
        await SendAsync(email, "Reset your password", body);
    }

    public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        var body = await renderer.RenderAsync<Templates.PasswordResetCodeEmail>(
            new Dictionary<string, object?> { [nameof(Templates.PasswordResetCodeEmail.Code)] = resetCode });
        await SendAsync(email, "Your password reset code", body);
    }

    private async Task SendAsync(string to, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = htmlBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            _options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None);
        if (!string.IsNullOrEmpty(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password ?? "");
        }

        await client.SendAsync(message);
        await client.DisconnectAsync(quit: true);

        logger.LogInformation("Sent {Subject} email to {To}.", subject, to);
    }
}
