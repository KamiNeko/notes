#!/usr/bin/env bash
# Runs once when the dev container is created: restores tools and packages.
set -euo pipefail

cd /workspace/backend
dotnet tool restore
dotnet restore Notes.slnx

# npm workspaces: installs the client and packages/ui
cd /workspace
npm ci
cd /workspace/client
# Chromium for the Playwright smoke tests (E2E_CHANNEL=chromium in docker-compose.yml)
npx playwright install --with-deps chromium
