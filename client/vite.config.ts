import path from "path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// Same image policy as the production server (Notes__AllowRemoteImages), so remote images in notes
// behave the same in development. The rest of the production CSP would break Vite's dev tooling.
const allowRemoteImages = /^(true|1|yes|on)$/i.test(process.env.Notes__AllowRemoteImages ?? "");
const devImageCsp = `img-src 'self' data: blob:${allowRemoteImages ? " https:" : ""}`;
// Build metadata, injected by the Docker build (APP_VERSION / GIT_SHA build args). Same rules as
// the Docker build passes to the backend, so the UI and /api/health agree; local builds report "dev".
const rawVersion = process.env.APP_VERSION?.trim();
const appVersion = rawVersion && /^[0-9A-Za-z][0-9A-Za-z.+_-]{0,63}$/.test(rawVersion) ? rawVersion : "dev";
const rawSha = process.env.GIT_SHA?.trim().toLowerCase();
const gitSha = rawSha && /^[0-9a-f]{7,40}$/.test(rawSha) ? rawSha : null;

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  define: {
    __APP_VERSION__: JSON.stringify(appVersion),
    __GIT_SHA__: JSON.stringify(gitSha),
  },
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    headers: {
      "Content-Security-Policy": devImageCsp,
    },
    proxy: {
      "/api": {
        target: process.env.API_PROXY_TARGET || "http://localhost:5080",
        // Keep the browser's Host, so the backend's Origin (CSRF) check sees the page's own origin
        changeOrigin: false,
      },
    },
  },
});
