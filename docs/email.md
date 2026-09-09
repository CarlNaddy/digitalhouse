# Email

The `rails-parity` **P4.2** batteries item (ActionMailer analog). No
first-party ASP.NET Core mail-sending library exists, so this is a
decide-a-library + wire-a-seam + write-a-convention exercise, same as the
rest of P4.

## Decision: MailKit, ASP.NET Core Identity's own `IEmailSender<TUser>` seam

- **MailKit** — the transport (SMTP client). Not `System.Net.Mail`
  (`SmtpClient`), which is officially obsolete/unsupported for new
  development.
- **`IEmailSender<TUser>`** (`Microsoft.AspNetCore.Identity`, .NET 8+) — the
  seam. This is the interface ASP.NET Core Identity's own scaffolded pages
  call for confirmation/reset emails, so implementing it (`MailKitEmailSender`
  in `Features/Email/`) is the "keep it behind a thin seam" (guiding
  principle 2) done the Microsoft-standard way, not a bespoke abstraction.
- **Templates** are Razor **components** (`.razor` files, no `@page`), not
  MVC views or a third-party string-templating library — this app is Blazor,
  not MVC, so there's no `IRazorViewEngine` view-rendering pipeline to reuse.
  `Microsoft.AspNetCore.Components.Web.HtmlRenderer` (first-party, .NET 8+)
  renders a component to an HTML string outside of a normal request; that's
  what `RazorEmailRenderer` wraps.
- **Dev sink: smtp4dev** (`compose.yaml`, service `mail`) — catches every
  outgoing email instead of delivering it, with a web UI to read them at
  `http://localhost:5001`. `EmailOptions`' defaults (`localhost:2525`, no
  auth) point at it with zero configuration.

## How it's wired

```
Features/Email/
  EmailOptions.cs          # SMTP host/port/from/credentials, bound from config "Email"
  RazorEmailRenderer.cs    # renders a template component to an HTML string
  MailKitEmailSender.cs    # IEmailSender<ApplicationUser> — sends via MailKit
  Templates/
    ConfirmationEmail.razor
    PasswordResetEmail.razor
    PasswordResetCodeEmail.razor
```

- `Program.cs`: `AddIdentityCore<ApplicationUser>(o => o.SignIn.RequireConfirmedAccount = true)`
  — a registered user must confirm their email before signing in;
  `SignInManager` checks this automatically (`SignInResult.NotAllowed`).
  `Configure<EmailOptions>` + `AddScoped<HtmlRenderer>` +
  `AddScoped<IEmailSender<ApplicationUser>, MailKitEmailSender>`.
- **Confirmation flow**: `Register.razor` creates the user, generates a
  confirmation token (`UserManager.GenerateEmailConfirmationTokenAsync`,
  base64url-encoded), sends it via `EmailSender.SendConfirmationLinkAsync`,
  and shows a "check your email" message instead of signing in immediately.
  `Components/Account/Pages/ConfirmEmail.razor` completes it
  (`UserManager.ConfirmEmailAsync`) when the link is clicked.
- **Password reset flow**: `ForgotPassword.razor` → generates a reset token
  (`UserManager.GeneratePasswordResetTokenAsync`) → `SendPasswordResetLinkAsync`
  → `ResetPassword.razor` (`UserManager.ResetPasswordAsync`) →
  `ResetPasswordConfirmation.razor`. Always shows the same "if an account
  exists..." message regardless of whether the email was found, matching
  Identity's own default anti-enumeration behavior.
- `Login.razor` handles `SignInResult.IsNotAllowed` with its own message
  (distinct from a generic invalid-login error) and links to
  `Account/ForgotPassword`.
- **External logins are unaffected** — `ExternalLogin.razor` already sets
  `EmailConfirmed = true` when auto-provisioning (trusts the provider's own
  verification, P3.4); the dev admin seed does too (`IdentitySeeder`, P3.6).
  Neither goes through this flow.
- `IEmailSender<TUser>` requires a third method, `SendPasswordResetCodeAsync`
  (the code-entry reset variant, e.g. for API/mobile clients) —
  `MailKitEmailSender` implements it (for interface completeness) and
  `PasswordResetCodeEmail.razor` is its template, but this app's own UI uses
  only the link-based flow above.

## Adding a new email

1. Add a template component under `Features/Email/Templates/` — plain
   `.razor`, no `@page`, parameters for whatever the body needs.
2. Send it: `renderer.RenderAsync<TTemplate>(parameters)` for the HTML, then
   the same MIME/SMTP send `MailKitEmailSender`'s private `SendAsync` does
   (extract a shared helper if a second non-Identity-triggered email appears).
3. If it isn't one of the three `IEmailSender<TUser>` hooks, call it directly
   from wherever the triggering event happens — a page, an endpoint, a job
   (see `docs/background-jobs.md` for the job-triggered case).

## Testing

`RazorEmailRenderer` is pure and deterministic — no SMTP, no database —
tested directly (`tests/DigitalHouse.Tests/Features/Email/RazorEmailRendererTests.cs`):
construct an `HtmlRenderer` over an empty `IServiceProvider` and a
`NullLoggerFactory`, render a template, assert on the output. **Don't** test
MailKit's own SMTP behavior — that's the library's job. The full send path
(SMTP connect, Identity's token generation, the confirm/reset pages) was
verified end-to-end manually (below), not as an automated test — automating
it would mean either a live SMTP dependency in CI or mocking MailKit, neither
of which is worth it for a one-time infrastructure check.

## Verified end-to-end (2026-09-02)

Against the real Docker Postgres + smtp4dev, a running `dotnet run`, real
`curl` requests (antiforgery token + `_handler` field, matching the P3.3
verification pattern):

- **Confirmation**: registered a new user → smtp4dev's REST API shows a
  "Confirm your email" message with the real rendered HTML body (the
  confirmation link inside it, generated correctly) → login *before*
  confirming → `SignInResult.NotAllowed`, the custom message → followed the
  link → `UserManager.ConfirmEmailAsync` succeeded → login *after*
  confirming → 302 to `/`.
- **Password reset**: `ForgotPassword` for the same user → smtp4dev shows a
  "Reset your password" email → followed its link → `ResetPassword` with a
  new password → 302 to `ResetPasswordConfirmation` → login with the *new*
  password → 302 to `/`.
- `RazorEmailRendererTests` (2 tests) pass. `dotnet test` → 20/20, clean
  build with analyzers, `dotnet format --verify-no-changes` clean.
