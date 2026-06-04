# Updater Service

The updater is a small daemon running on the same Linux server as the API. It listens on a localhost-only HTTP socket and executes version switch operations on behalf of the API. This keeps shell execution and process management entirely outside the .NET process.

## Why a Separate Process?

A .NET process cannot cleanly restart itself. The updater handles the full lifecycle: fetch release → build/extract → run migrations → signal systemd to restart the API service. The API merely sends a command; the updater does the work.

## Architecture

```
Admin panel
    │  POST /admin/version/switch
    ▼
OpenPlan API  ──── HTTP POST localhost:5050/update ────▶  Updater daemon
                                                              │
                                                    git fetch + checkout tag
                                                    dotnet publish
                                                    dotnet ef database update
                                                    systemctl restart openplan-api
```

The updater binds only to `127.0.0.1:5050` — it is never exposed publicly.

## Setup

### 1. Place the updater script

Save the following as `/opt/openplan-updater/updater.sh` and make it executable:

```bash
#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/openplan-api-src"
PUBLISH_DIR="/opt/openplan-api"
SERVICE="openplan-api"
LISTEN_PORT=5050

# Minimal HTTP server: reads one request, acts, responds
handle() {
  local body="$1"
  local version
  version=$(echo "$body" | python3 -c "import sys,json; print(json.load(sys.stdin)['targetVersion'])")

  cd "$REPO_DIR"
  git fetch --tags
  git checkout "$version"
  dotnet publish OpenPlan.API -c Release -o "$PUBLISH_DIR"
  dotnet ef database update --project OpenPlan.API
  systemctl restart "$SERVICE"

  echo '{"status":"ok"}'
}

while true; do
  echo "Updater listening on $LISTEN_PORT"
  # Use ncat/socat to serve a single HTTP response
  request=$(echo -e "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n\r\n$(handle "$(cat)")" \
    | nc -l -p "$LISTEN_PORT" -q 1)
done
```

> For production use, replace the bash HTTP server with a small Python/Go/Rust HTTP server for reliability. The bash version is illustrative.

### 2. systemd Unit for the Updater

Create `/etc/systemd/system/openplan-updater.service`:

```ini
[Unit]
Description=OpenPlan Updater Daemon
After=network.target

[Service]
Type=simple
User=openplan
ExecStart=/opt/openplan-updater/updater.sh
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

```bash
systemctl daemon-reload
systemctl enable --now openplan-updater
```

### 3. Configure the API

Set the updater URL in `appsettings.json` or via environment variable:

```json
{
  "Updater": {
    "Url": "http://127.0.0.1:5050/update"
  }
}
```

## Security

- The updater socket must never be exposed outside `127.0.0.1`.
- The `openplan` user running the updater needs write access to `REPO_DIR` and `PUBLISH_DIR`, and `systemctl` permissions scoped to `openplan-api.service` only (use `sudoers` or a polkit rule).
- The API validates that `targetVersion` matches a known GitHub release tag before forwarding to the updater.
