#!/usr/bin/env bash
#
# One-command full local stack (parity plan P5.2): app + Postgres + mail
# sink, all in Docker. Two real steps under the hood — `docker compose up`
# alone can't build the app image, since compose.yaml's `app` service uses
# the image `dotnet publish -t:PublishContainer` builds (P5.1), not a
# Dockerfile `build:` section (deliberately — see docs/deployment.md) — this
# script is what makes it one command for the person running it.
#
#   bash scripts/run-stack.sh              build + start, seed if the DB is empty
#   bash scripts/run-stack.sh --no-seed    build + start only
#   bash scripts/run-stack.sh --down       stop the stack

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

if [ "${1:-}" = "--down" ]; then
    docker compose down
    exit 0
fi

seed=1
[ "${1:-}" = "--no-seed" ] && seed=0

CSPROJ="$(ls "$ROOT"/*.csproj | head -1)"

# compose.yaml's `app` image must match what $CSPROJ's ContainerRepository
# actually builds — lowercased (Docker/OCI repository names can't be
# uppercase; MSBuildProjectName.ToLowerInvariant() in the .csproj already
# handles this for `dotnet publish` itself, but compose.yaml can't evaluate
# MSBuild functions, so this script computes the same value and exports it
# for compose.yaml's ${APP_IMAGE:-digitalhouse} to pick up.
export APP_IMAGE
APP_IMAGE="$(basename "$CSPROJ" .csproj | tr '[:upper:]' '[:lower:]')"

echo "==> Building the app image (dotnet publish -t:PublishContainer)"
dotnet publish "$CSPROJ" -t:PublishContainer -c Release

echo "==> Starting the full stack"
docker compose up -d

if [ "$seed" = 1 ]; then
    echo "==> Seeding (idempotent — a no-op if the DB already has data)"
    docker compose run --rm app seed
fi

cat <<EOF

Stack is up:
  App:   http://localhost:8080
  Mail:  http://localhost:5001   (dev SMTP sink — every outgoing email lands here)

  docker compose logs -f app     # tail the app's logs
  bash scripts/run-stack.sh --down
EOF
