#!/usr/bin/env bash
#
# Set up the local Postgres dev database for this project — standalone.
#
#   scripts/setup-local-db.sh [ProjectName]
#
# This is exactly the "Local database" step of scripts/new-project.sh, on its
# own, for when you *don't* want to run the full template rename — a fresh
# clone, a CI box, or just to repair a drifted connection string. It:
#   - reads POSTGRES_DB / POSTGRES_USER / POSTGRES_PASSWORD out of compose.yaml
#   - if host port 5432 is already taken by another database, moves
#     compose.yaml's `db` port mapping to the next free port
#   - sets the ConnectionStrings:Default user-secret to match (the host dev
#     loop, parity plan P1.3) — so the secret can't drift from what the
#     container actually runs with
#   - starts the `db` service:  docker compose up -d db
#
# ProjectName defaults to the basename of the single root *.csproj. Safe to
# re-run: an already-correct secret is just rewritten to the same value, and a
# port already held by THIS project's own db container is left alone.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

# --- project identifier --------------------------------------------------
NEW="${1:-}"
if [ -z "$NEW" ]; then
    csproj="$(find . -maxdepth 1 -name '*.csproj' | head -n1)"
    [ -n "$csproj" ] || { echo "no root .csproj found — pass the project name as an argument" >&2; exit 1; }
    NEW="$(basename "$csproj" .csproj)"
fi
[ -f "${NEW}.csproj" ] || { echo "${NEW}.csproj not found in $ROOT" >&2; exit 1; }

# --- db credentials from compose.yaml ----------------------------------
[ -f compose.yaml ] || { echo "compose.yaml not found in $ROOT" >&2; exit 1; }
compose_env() { sed -n "s/^[[:space:]]*$1:[[:space:]]*//p" compose.yaml | head -n1; }
pg_db="$(compose_env POSTGRES_DB)"
pg_user="$(compose_env POSTGRES_USER)"
pg_pw="$(compose_env POSTGRES_PASSWORD)"
[ -n "$pg_db" ] && [ -n "$pg_user" ] && [ -n "$pg_pw" ] \
    || { echo "could not read POSTGRES_DB / POSTGRES_USER / POSTGRES_PASSWORD from compose.yaml" >&2; exit 1; }

# --- pick a free host port ---------------------------------------------
# compose.yaml maps "<host>:5432". If <host> is taken by another database,
# `docker compose up` can't bind it — walk up to the first free port and
# rewrite the mapping so it and the connection string stay in lockstep.
host_port="$(sed -n 's/^[[:space:]]*-[[:space:]]*"\([0-9]\{1,5\}\):5432".*/\1/p' compose.yaml | head -n1)"
host_port="${host_port:-5432}"
have_docker=0
command -v docker >/dev/null 2>&1 && have_docker=1

if [ "$have_docker" = 1 ]; then
    port_busy() { (exec 3<>"/dev/tcp/127.0.0.1/$1") >/dev/null 2>&1; }
    ours_on_port() {
        local cid; cid="$(docker compose ps -q db 2>/dev/null || true)"
        [ -n "$cid" ] && docker port "$cid" 5432/tcp 2>/dev/null | grep -qE ":$1\$"
    }
    if port_busy "$host_port" && ! ours_on_port "$host_port"; then
        p=$((host_port + 1))
        while [ "$p" -lt 65535 ] && port_busy "$p"; do p=$((p + 1)); done
        echo "==> Port $host_port is in use — moving Postgres to $p"
        sed -i "s/- \"${host_port}:5432\"/- \"${p}:5432\"/" compose.yaml
        host_port="$p"
    fi
fi

# --- connection string ------------------------------------------------
conn="Host=localhost;Port=${host_port};Database=${pg_db};Username=${pg_user};Password=${pg_pw}"
echo "==> Setting user-secret ConnectionStrings:Default"
echo "    Database=${pg_db} Username=${pg_user} Password=*** Port=${host_port}"
if ! dotnet user-secrets set "ConnectionStrings:Default" "$conn" --project "${ROOT}/${NEW}.csproj" >/dev/null; then
    echo "dotnet user-secrets set failed — is the .NET SDK on PATH and ${NEW}.csproj valid?" >&2
    exit 1
fi

# --- start Postgres -------------------------------------------------
if [ "$have_docker" = 0 ]; then
    echo "docker not found — the secret is set, but start Postgres yourself before 'dotnet run -- seed'" >&2
    exit 1
fi
echo "==> Starting Postgres (docker compose up -d db)"
if ! docker compose up -d db; then
    echo "docker compose up -d db failed — is the Docker daemon running?" >&2
    exit 1
fi

echo
echo "Local database ready:"
echo "  connection : Host=localhost;Port=${host_port};Database=${pg_db};Username=${pg_user};Password=***"
echo "  next       : dotnet run -- seed   (apply migrations + seed sample data)"
