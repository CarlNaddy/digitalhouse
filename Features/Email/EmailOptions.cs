namespace DigitalHouse.Features.Email;

/// <summary>
/// SMTP settings for outgoing mail (parity plan P4.2), bound from config
/// section <c>Email</c>. Defaults match the dev sink in <c>compose.yaml</c>
/// (smtp4dev, no auth) so local dev works with zero configuration; a real
/// SMTP provider in prod is configured the same way as everything else here —
/// user-secrets in dev (only to override the dev sink), env vars in prod.
/// </summary>
public class EmailOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 2525;
    public string From { get; set; } = "no-reply@DigitalHouse.local";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseSsl { get; set; }
}
