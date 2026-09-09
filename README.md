# DigitalHouse

ASP.NET Core Blazor Web App — .NET 10, MudBlazor, EF Core + PostgreSQL.
Started from the [dotnet-agentic-starterkit](https://github.com/CarlNaddy/dotnet-agentic-starterkit) template.

## Run locally

```bash
docker compose up -d db
dotnet tool restore
dotnet run -- seed        # apply migrations + seed sample data
dotnet watch run
dotnet test
```

Keeping the `Listing` sample for now (worked pattern for auth / jobs /
caching / file storage) — remove it any time with
`bash scripts/remove-sample.sh`; then `dotnet run -- seed` above becomes
`dotnet ef database update` (once you add your first model).

Conventions and AI tooling: see `CLAUDE.md`.
