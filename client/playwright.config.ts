import { tmpdir } from "node:os";
import { join } from "node:path";
import { defineConfig, devices } from "@playwright/test";

/**
 * End-to-end smoke tests against the demo: the .NET backend with the demo account (seeded into its
 * own database) and the Vite dev server. Ports differ from the dev defaults so a running dev setup
 * isn't touched.
 *
 *   E2E_DATABASE         Npgsql connection string of the database the backend may use (created and
 *                        migrated on start; default: database notes_e2e on the dev Postgres)
 *   E2E_CHANNEL          browser channel (default "chrome": a locally installed Chrome, no download)
 *   E2E_EXECUTABLE_PATH  a Chromium binary to use instead of a channel
 */
const API_PORT = 3201;
const CLIENT_PORT = 5201;
const executablePath = process.env.E2E_EXECUTABLE_PATH;
const database =
  process.env.E2E_DATABASE || "Host=localhost;Port=5432;Database=notes_e2e;Username=notes;Password=notes";

export default defineConfig({
  testDir: "e2e",
  // The tests share one in-memory demo server, so they run one after another
  workers: 1,
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  reporter: "list",
  use: {
    baseURL: `http://localhost:${CLIENT_PORT}`,
    locale: "en-US",
    trace: "retain-on-failure",
    ...(executablePath ? { launchOptions: { executablePath } } : { channel: process.env.E2E_CHANNEL || "chrome" }),
  },
  projects: [
    // Tests tagged @phone run only on the phone layout, all others only on desktop
    { name: "desktop", use: { ...devices["Desktop Chrome"] }, grepInvert: /@phone/ },
    { name: "phone", use: { ...devices["Pixel 7"] }, grep: /@phone/ },
  ],
  webServer: [
    {
      command: "dotnet run --project ../backend/src/Notes.Api --no-launch-profile",
      env: {
        ASPNETCORE_URLS: `http://localhost:${API_PORT}`,
        ConnectionStrings__Notes: database,
        Notes__SeedDemo: "true",
        Notes__DataDirectory: join(tmpdir(), "notes-e2e-data"),
      },
      url: `http://localhost:${API_PORT}/api/health`,
      // The first run builds the backend
      timeout: 180_000,
      reuseExistingServer: !process.env.CI,
    },
    {
      command: `npm run dev -- --port ${CLIENT_PORT} --strictPort`,
      env: { API_PROXY_TARGET: `http://localhost:${API_PORT}` },
      url: `http://localhost:${CLIENT_PORT}`,
      reuseExistingServer: !process.env.CI,
    },
  ],
});
