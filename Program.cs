using DigitalHouse.Components;
using DigitalHouse.Components.Account;
using DigitalHouse.Data;
using DigitalHouse.Data.Seed;
using DigitalHouse.Endpoints;
using DigitalHouse.Features.Console;
using DigitalHouse.Features.Email;
using DigitalHouse.Features.Files;
using DigitalHouse.Features.Jobs;
using DigitalHouse.Features.Listings;
using DigitalHouse.Features.Marketplace;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// Testable clock. Feature code injects TimeProvider rather than calling
// DateTimeOffset.Now directly; tests swap in FakeTimeProvider.
builder.Services.AddSingleton(TimeProvider.System);

string[] supportedCultures = ["en", "de"];
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Connection string 'Default' is not configured. Set it via user-secrets in "
        + "development or the ConnectionStrings__Default environment variable in production.");

// Factory, not AddDbContext: interactive Blazor components outlive a request
// scope, so each operation creates a short-lived context (MS "Blazor with EF Core").
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(connectionString));

// ASP.NET Core Identity stores need a scoped AppDbContext; hand them one from the
// same factory so there is still a single context configuration.
builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// Authentication & authorization (parity plan P3.2–P3.5) — Identity with EF Core
// stores in AppDbContext.
builder.Services.AddCascadingAuthenticationState();

// P3.5: Listings are public to read, gated to write. "ListingsWriter" = any
// signed-in user (create / edit); "ListingsAdmin" = the Admin role (delete).
// An Admin user is seeded in P3.6.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ListingsWriter", policy => policy.RequireAuthenticatedUser())
    .AddPolicy("ListingsAdmin", policy => policy.RequireRole("Admin"));

var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
});
authentication.AddIdentityCookies();

// P3.4: external OAuth2 providers. Each registers only when its ClientId (and
// secret) are configured — key shape Authentication:<Provider>:{ClientId,
// ClientSecret}, from user-secrets in dev / env vars in prod. Callback paths
// are the handler defaults: /signin-google, /signin-microsoft, /signin-github.
var googleAuth = builder.Configuration.GetSection("Authentication:Google");
if (!string.IsNullOrEmpty(googleAuth["ClientId"]))
{
    authentication.AddGoogle(options =>
    {
        options.ClientId = googleAuth["ClientId"]!;
        options.ClientSecret = googleAuth["ClientSecret"]!;
    });
}

var microsoftAuth = builder.Configuration.GetSection("Authentication:Microsoft");
if (!string.IsNullOrEmpty(microsoftAuth["ClientId"]))
{
    authentication.AddMicrosoftAccount(options =>
    {
        options.ClientId = microsoftAuth["ClientId"]!;
        options.ClientSecret = microsoftAuth["ClientSecret"]!;
    });
}

var gitHubAuth = builder.Configuration.GetSection("Authentication:GitHub");
if (!string.IsNullOrEmpty(gitHubAuth["ClientId"]))
{
    authentication.AddGitHub(options =>
    {
        options.ClientId = gitHubAuth["ClientId"]!;
        options.ClientSecret = gitHubAuth["ClientSecret"]!;
        options.Scope.Add("user:email");
    });
}

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    // P4.2: a registered user must confirm their email before signing in —
    // SignInManager checks this automatically, surfacing SignInResult.NotAllowed.
    // External logins bypass it (ExternalLogin.razor sets EmailConfirmed = true,
    // trusting the provider's own verification); the dev admin seed does too.
    options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// P5.4: Data Protection keys persist in Postgres (AppDbContext implements
// IDataProtectionKeyContext) instead of the in-memory default, which
// regenerates a new key on every restart — silently invalidating every
// issued auth cookie and antiforgery token. A fixed application name keeps
// the key ring stable across the different ContentRootPath values `dotnet
// watch run` (host) and the container (/app) each use — without it, the
// same Postgres-stored keys wouldn't be recognized as belonging to "this app"
// from both environments. See docs/deployment.md.
builder.Services.AddDataProtection()
    .SetApplicationName("DigitalHouse")
    .PersistKeysToDbContext<AppDbContext>();

// P3.3: Register/Login static-SSR pages redirect via NavigationManager, which
// throws a handled NavigationException during static rendering — see
// Components/Account/IdentityRedirectManager.cs.
builder.Services.AddScoped<IdentityRedirectManager>();

// Email (parity plan P4.2) — MailKit over SMTP settings in EmailOptions (dev
// default: the smtp4dev sink in compose.yaml, zero config needed). Templates
// are Razor components rendered to HTML via HtmlRenderer (the first-party way
// to render components outside a request, .NET 8+) — see docs/email.md.
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<HtmlRenderer>();
builder.Services.AddScoped<RazorEmailRenderer>();
builder.Services.AddScoped<IEmailSender<ApplicationUser>, MailKitEmailSender>();

// Background jobs (parity plan P4.1) — Hangfire, storage in the app's own
// Postgres DB (own schema, managed by Hangfire itself — not an EF Core
// migration; see docs/background-jobs.md). AddHangfireServer runs the worker
// as a hosted service, started by app.Run() below — never during `seed`.
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();
builder.Services.AddScoped<ListingJobs>();

// Caching + rate limiting (parity plan P4.3), all first-party. HybridCache
// sits in front of the DB for the read-only Listings API; a Redis
// IDistributedCache backplane is (vNext) — this is in-memory only, added
// when the app runs more than one instance. See docs/caching.md.
builder.Services.AddHybridCache();
builder.Services.AddScoped<ListingQueries>();

builder.Services.AddOutputCache(options =>
    options.AddPolicy("Listings", policy => policy.Expire(TimeSpan.FromSeconds(30))));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("Api", policy =>
    {
        policy.PermitLimit = 5;
        policy.Window = TimeSpan.FromSeconds(10);
        policy.QueueLimit = 0;
    });
});

// File storage (parity plan P4.4, ActiveStorage analog) — IFileStore behind a
// config-driven provider switch; only LocalDisk exists today, a blob
// provider (Azure/S3) is a later addition to this switch, not a rewrite of
// callers. See docs/file-storage.md.
builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection("FileStorage"));
var fileStorageProvider = builder.Configuration["FileStorage:Provider"] ?? "LocalDisk";
switch (fileStorageProvider)
{
    case "LocalDisk":
        builder.Services.AddScoped<IFileStore, LocalDiskFileStore>();
        break;
    default:
        throw new InvalidOperationException(
            $"Unknown FileStorage:Provider '{fileStorageProvider}'. Supported: LocalDisk.");
}

builder.Services.AddScoped<ListingPhotoService>();

// Marketplace feature (openspec: add-digital-asset-marketplace) — options,
// domain services, the Stripe payment seam, and recurring jobs. See
// Features/Marketplace/MarketplaceServiceCollectionExtensions.cs.
builder.Services.AddMarketplace(builder.Configuration, builder.Environment);

// dotnet-aspnetcore:minimal-api-file-upload — the multipart body limit is a
// global FormOptions setting with no per-endpoint override, but this app has
// only one multipart form (the photo upload), so 5 MB here is scoped in
// practice even though the config isn't. The Kestrel-level request size
// limit, which *does* have a per-endpoint override, stays at its framework
// default globally and is narrowed to 5 MB only on the photo endpoint
// ([RequestSizeLimit] in ListingsApiEndpoints.cs) — lowering it here would
// silently cap every other endpoint in the app, not just this one.
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 5 * 1024 * 1024);

// Health checks (parity plan P5.4). "self" (tagged "live") never touches a
// dependency — that's what /alive maps to, a fast liveness probe. /health
// maps to every check, "self" plus the database, for readiness (is this
// instance actually able to serve requests, not just running).
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

// `dotnet run -- seed`: apply migrations + seed sample data, then exit.
if (args.Contains(SeedCommand.Verb))
{
    await SeedCommand.RunAsync(app.Services);
    return;
}

// `dotnet run -- console`: the rails-console substitute (parity plan P6.2) —
// runs Features/Console/Scratch.cs against the real app, then exits.
if (args.Contains(ConsoleCommand.Verb))
{
    await ConsoleCommand.RunAsync(app.Services);
    return;
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// P5.4: a containerized deploy terminates TLS at the ingress/reverse-proxy
// layer, not in-process — the container itself only ever speaks plain HTTP
// (see docs/deployment.md). Without this, the app never sees the original
// request as HTTPS, so UseHttpsRedirection below would redirect-loop behind
// a proxy that already terminated TLS. Must run before UseHttpsRedirection.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
// Default KnownNetworks/KnownProxies only trust loopback — silently a no-op
// behind a real cloud load balancer, whose IP isn't loopback and usually
// isn't static enough to allowlist. Clearing them trusts *any* proxy's
// headers instead; that's the standard tradeoff for a containerized deploy,
// where the platform's own network boundary (only the LB can reach the
// container) is what actually enforces trust, not this allowlist.
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseHttpsRedirection();
app.UseRequestLocalization(localizationOptions);

app.UseStaticFiles();
app.UseAntiforgery();

app.UseRateLimiter();
app.UseOutputCache();

app.MapCultureEndpoints();
app.MapAccountEndpoints();
app.MapListingsApiEndpoints();
app.MapFileEndpoints();
app.MapStripeWebhookEndpoints();
// /health: every check (readiness — can this instance actually serve
// requests, DB included). /alive: only "live"-tagged checks (liveness — is
// the process itself running, no dependencies) — the MS-documented split
// for container orchestrators that probe the two differently.
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthorizationFilter()],
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Recurring jobs are declarative — re-registering the same job id on every
// startup just updates its schedule, so this is idempotent.
using (var scope = app.Services.CreateScope())
{
    var recurringJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobs.AddOrUpdate<ListingJobs>(
        "daily-listing-count",
        job => job.RecordDailyListingCountAsync(CancellationToken.None),
        Cron.Daily());
}

app.Run();
