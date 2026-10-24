# Deployment guide

How to install, expose, update, back up and remove Notes. For the security background, see the [security guide](security.md).

## Requirements

- A Linux host with **Docker Engine** and the **Compose plugin** (amd64 or arm64). 1 CPU core, 1 GB RAM and a few GB of disk are plenty for a household.
- For access from outside your home network: a domain and a **reverse proxy with HTTPS**.

### Proxmox LXC

1. Create an unprivileged Debian 13 (or Ubuntu 24.04) container: 1–2 cores, 1–2 GB RAM, 8 GB disk or more.
2. Under *Options > Features*, enable **nesting** (and **keyctl** for unprivileged containers). Docker needs both.
3. Inside the container, install Docker from Docker's repository (`curl -fsSL https://get.docker.com | sh`, or follow docs.docker.com).
4. Optional but recommended: put the container's disk on encrypted storage (ZFS native encryption or LUKS), because notes are not encrypted by the app.

## Install

```bash
git clone https://github.com/KamiNeko/notes.git /opt/notes
cd /opt/notes
cp .env.example .env
chmod 600 .env
```

Edit `.env`:

```ini
POSTGRES_PASSWORD=<output of: openssl rand -hex 24>
NOTES_ADMIN_EMAIL=you@example.com
NOTES_ADMIN_PASSWORD=<at least 12 characters>
```

Start it:

```bash
docker compose up -d --build
docker compose ps          # both containers "healthy"
```

Open `http://<host>:8080`, sign in as the administrator, and add the other accounts under the account menu > *Manage users*. Then remove `NOTES_ADMIN_PASSWORD` from `.env`: the account exists, the line isn't needed anymore.

### Using the published image instead of building

Releases are published as `ghcr.io/kamineko/notes:<version>` for amd64 and arm64. In `docker-compose.yml`, comment out `build:` and set `image:` to a specific version. Verify the image before you start it:

```bash
gh attestation verify oci://ghcr.io/kamineko/notes:<version> --repo KamiNeko/notes
docker compose pull && docker compose up -d
```

## Reverse proxy and HTTPS

Set in `.env`, then `docker compose up -d`:

```ini
NOTES_PUBLIC_URL=https://notes.example.com
NOTES_TRUST_PROXY=true
# Only needed if the proxy connects from a public address:
# NOTES_TRUSTED_PROXIES=203.0.113.10
```

If the proxy runs on the same host, publish the port on localhost only (`HOST_PORT=127.0.0.1:8080`) so nobody can bypass it.

**Caddy** (certificates are automatic):

```caddyfile
notes.example.com {
    reverse_proxy 192.168.1.20:8080
    request_body {
        max_size 30MB
    }
}
```

**Nginx Proxy Manager:** add a proxy host for `notes.example.com` → `http://192.168.1.20:8080`, request a Let's Encrypt certificate, enable *Force SSL* and *HTTP/2*. Under *Advanced*, add `client_max_body_size 210m;` so uploads (25 MB by default) and imports (200 MB by default) pass; the app enforces its own, smaller limit per request.

**Plain nginx:**

```nginx
server {
    listen 443 ssl;
    http2 on;
    server_name notes.example.com;
    # ssl_certificate ... ;
    # Imports up to Notes__MaxImportMb (200 MB); the app limits every other request itself
    client_max_body_size 210m;
    location / {
        proxy_pass http://192.168.1.20:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $host;
    }
}
```

Check it: after signing in over HTTPS, the browser's developer tools show the cookie `__Host-notes_session` and the response header `Strict-Transport-Security`.

## Updating

Read the [changelog](../CHANGELOG.md) first, then:

```bash
cd /opt/notes
git pull                      # or change the image version in docker-compose.yml
docker compose up -d --build  # or: docker compose pull && docker compose up -d
docker image prune -f
```

Migrations run automatically on start. Make a backup before major version updates. To hear about security fixes, watch the repository on GitHub (*Watch > Custom > Releases, Security alerts*).

PostgreSQL itself stays on major version 17 (Dependabot only proposes minor updates). Moving to a new PostgreSQL major needs a dump and restore, as below.

## Backup

The database holds everything except attachment contents; the `notes-files` volume holds the attachments.

```bash
cd /opt/notes
mkdir -p backups
docker compose exec -T db pg_dump -U notes -Fc notes > backups/notes-$(date +%F).dump
docker run --rm -v notes_notes-files:/data:ro -v "$PWD/backups":/backup alpine \
  tar czf /backup/notes-files-$(date +%F).tar.gz -C /data attachments
```

Run it daily from cron, copy the files off the host, and keep them as protected as the server: they contain all notes. A Proxmox backup (`vzdump`) of the whole LXC works too; stop the stack first or use snapshot mode on ZFS/LVM-thin storage.

## Restore

```bash
cd /opt/notes
docker compose up -d db
docker compose exec -T db pg_restore -U notes -d notes --clean --if-exists < backups/notes-2026-10-23.dump
docker run --rm -v notes_notes-files:/data -v "$PWD/backups":/backup alpine \
  sh -c 'rm -rf /data/attachments && tar xzf /backup/notes-files-2026-10-23.tar.gz -C /data && chown -R 1654:1654 /data'
docker compose up -d
```

## Accounts from the command line

```bash
docker compose exec notes dotnet Notes.Api.dll users list
docker compose exec -it notes dotnet Notes.Api.dll users add alice@example.com --name "Alice" [--admin]
docker compose exec -it notes dotnet Notes.Api.dll users set-password alice@example.com
docker compose exec notes dotnet Notes.Api.dll users reset-2fa alice@example.com
```

The password is read from standard input (or `NOTES_PASSWORD`). Use this if you lock yourself out. `reset-2fa` turns off two-factor sign-in for someone who lost their phone and recovery codes, and ends their sessions.

## Logs and monitoring

```bash
docker compose logs -f notes
docker compose logs notes | grep -i "sign-in\|refused\|rate limit"   # security events
curl -fsS http://localhost:8080/api/health                           # {"status":"ok",...}
```

Limit Docker's log size in `/etc/docker/daemon.json`:

```json
{ "log-driver": "json-file", "log-opts": { "max-size": "20m", "max-file": "5" } }
```

## Removing Notes

To take Notes out of service and remove all data for good:

1. Let users download their data first (account menu > *Export notes*).
2. Stop and delete the containers **and volumes**: `docker compose down -v --rmi local`.
3. Delete the checkout including `.env`: `rm -rf /opt/notes`.
4. Delete the backups and log files, and the LXC container if it was only used for Notes.

## Troubleshooting

| Symptom | Cause and fix |
| :--- | :--- |
| `set POSTGRES_PASSWORD in .env` on start | `.env` is missing or empty; copy `.env.example`. |
| The container stops right away with *Invalid settings: …* | A setting has a value the app doesn't accept; the message names it and says what is allowed. Fix it in `.env` and start again. |
| Log: *There are no users yet* | `NOTES_ADMIN_EMAIL`/`NOTES_ADMIN_PASSWORD` were not set on the first start; set them and restart, or use `users add … --admin`. |
| Sign-in works, but saving says *Request origin not allowed* | The URL in the browser differs from what the app sees. Set `NOTES_PUBLIC_URL` to the exact URL, and `NOTES_TRUST_PROXY=true` behind a proxy. |
| *Too many failed sign-ins for this account* | Ten wrong passwords in 15 minutes. Wait 15 minutes or restart the container. |
| Uploads fail with *The file is too large* | Raise `NOTES_MAX_UPLOAD_MB` and the proxy's body size limit. |
| An import fails with *The file is larger than …* or a proxy error 413 | Raise `NOTES_MAX_IMPORT_MB` and the proxy's body size limit. |
| All users appear to come from one address in the log | The proxy isn't trusted: set `NOTES_TRUST_PROXY=true` (and `NOTES_TRUSTED_PROXIES` if it isn't on a private network). |
