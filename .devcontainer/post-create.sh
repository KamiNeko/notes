#!/usr/bin/env bash
# Runs once when the dev container is created: restores tools and packages.
set -euo pipefail

cd /workspace/backend
dotnet tool restore
dotnet restore Notes.slnx

cd /workspace/client
npm ci
# Chromium for the Playwright smoke tests (E2E_CHANNEL=chromium in docker-compose.yml)
npx playwright install --with-deps chromium
