# Test coverage

Coverage collection and the recorded baseline (rails-parity plan **P2.5**).

## Collecting locally

The suite runs on the Microsoft Testing Platform, so coverage comes from the
first-party `Microsoft.Testing.Extensions.CodeCoverage` package (referenced by the
test project) — not a VSTest `--collect` data collector.

```bash
docker compose up -d db   # the DB-tier tests (P2.3) need Docker
dotnet test -c Release -- \
  --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
```

The report lands at `TestResults/coverage.cobertura.xml` (git-ignored). Feed it to
the `dotnet-test:coverage-analysis` skill or ReportGenerator for a breakdown.

## In CI

`.github/workflows/ci.yml` runs restore → build → test-with-coverage on every
push to `main` and every PR. It writes a lines/branches table to the job summary
and uploads `coverage.cobertura.xml` as a build artifact. (The full
publish/deploy pipeline is a separate concern — parity plan P5.3.)

## Baseline

Recorded 2026-09-01, at the P2.5 commit, from the command above:

| Metric | Covered | Total | % |
|---|---|---|---|
| Lines | 917 | 1382 | **66.4%** |
| Branches | 90 | 246 | **36.6%** |

This is whole-assembly coverage — it includes EF Core migrations and other
generated code, which drags both numbers down (branch coverage especially). Treat
it as a **direction marker**, not a target: new feature work should not push the
line rate below this. Refining the measurement (excluding `Data/Migrations/**`
and generated Razor) is a later cleanup, not part of the baseline.
