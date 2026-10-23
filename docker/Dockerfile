# syntax=docker/dockerfile:1

# ==============================================================================
# Stage 1: Build the client (React + Vite)
# ==============================================================================
# Base images are pinned by digest for reproducible builds; Dependabot proposes updates.
# The bundle is plain JS/CSS, so it is built natively on the build machine, not under QEMU for
# every target platform. Keep the Node.js major in sync with .nvmrc, which CI reads.
FROM --platform=$BUILDPLATFORM node:25-alpine@sha256:bdf2cca6fe3dabd014ea60163eca3f0f7015fbd5c7ee1b0e9ccb4ced6eb02ef4 AS build-client

WORKDIR /build/client

# Playwright is only used for tests and README screenshots; never download browsers here
ENV PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
COPY client/package.json client/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY client/ ./
# Build metadata, baked into the bundle through Vite's define (see client/vite.config.ts).
# Declared late so a new commit doesn't invalidate the cached `npm ci` layer.
ARG APP_VERSION=dev
ARG GIT_SHA=
RUN npm run build

# ==============================================================================
# Stage 2: Build the backend (.NET 10)
# ==============================================================================
# The output is platform-neutral IL, so this stage runs natively too
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS build-backend

WORKDIR /build/backend

# Restore first, so a code change doesn't invalidate the package layer
COPY backend/global.json backend/Directory.Build.props ./
COPY backend/src/Notes.Api/Notes.Api.csproj backend/src/Notes.Api/packages.lock.json src/Notes.Api/
# --locked-mode: exactly the versions in packages.lock.json
RUN dotnet restore src/Notes.Api/Notes.Api.csproj --locked-mode

COPY backend/src/ src/
ARG APP_VERSION=dev
RUN dotnet publish src/Notes.Api/Notes.Api.csproj --no-restore -c Release -o /out \
      -p:InformationalVersion="${APP_VERSION}" -p:IncludeSourceRevisionInInformationalVersion=false

# ==============================================================================
# Stage 3: Runtime
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca AS production

# Npgsql probes for Kerberos (GSSAPI) on connect and logs an error when the library is missing
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

# App files stay owned by root and are read-only for the runtime user. Attachments go to /data,
# owned by the image's non-root "app" user, so a new named volume inherits that owner.
WORKDIR /app
COPY --from=build-backend /out ./
COPY --from=build-client /build/client/dist ./wwwroot
RUN mkdir /data && chown app:app /data

ARG APP_VERSION=dev
ARG GIT_SHA=
# EnableDiagnostics=0 turns off the debugger/profiler IPC endpoints, which a server doesn't need
# (and which would write to /tmp; the compose file runs the container read-only)
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1 \
    DOTNET_EnableDiagnostics=0 \
    Notes__DataDirectory=/data \
    GIT_SHA=${GIT_SHA}

LABEL org.opencontainers.image.title="Notes" \
      org.opencontainers.image.description="Self-hosted Markdown notes: notebooks, tags, search, attachments, to-dos and version history" \
      org.opencontainers.image.source="https://github.com/KamiNeko/notes" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version="${APP_VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}"

USER app
VOLUME /data

# Container port. Choose the host port with the -p mapping (HOST_PORT in docker-compose.yml).
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --start-interval=2s --retries=3 \
  CMD ["dotnet", "Notes.Api.dll", "healthcheck"]

ENTRYPOINT ["dotnet", "Notes.Api.dll"]
