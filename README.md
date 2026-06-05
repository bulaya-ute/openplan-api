# OpenPlan API

The backend for [OpenPlan](https://github.com/bulaya-ute/openplan-web) — a self-hostable, open-source task manager. Built with .NET 8 and PostgreSQL.

**License:** MIT · **Status:** Working prototype

---

## Quick Install (Linux)

```bash
curl -fsSL https://raw.githubusercontent.com/bulaya-ute/openplan-api/main/deploy/setup.sh \
  -o setup.sh && bash setup.sh
```

The script walks you through every setting interactively — install directories, database credentials, ports, service names, JWT secret, CORS origins, and more. It installs all prerequisites, sets up PostgreSQL, publishes the API as a systemd service, builds the web and admin apps, and starts the updater daemon.

If the script exits partway through, re-run it — completed stages are skipped automatically.

| Platform | Script |
|---|---|
| Linux | [`deploy/setup.sh`](deploy/setup.sh) |
| Windows | [`deploy/setup-windows.ps1`](deploy/setup-windows.ps1) *(stub — contributions welcome)* |
| macOS | [`deploy/setup-mac.sh`](deploy/setup-mac.sh) *(stub — contributions welcome)* |

---

## Tech Stack

- .NET 8 Web API (controllers style)
- Entity Framework Core 8 + Npgsql (PostgreSQL)
- BCrypt.Net password hashing
- JWT Bearer authentication

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL 14+

### Database

```sql
CREATE USER openplan_user WITH PASSWORD 'your_password';
CREATE DATABASE openplan_db OWNER openplan_user;
```

### Run

```bash
dotnet run --project OpenPlan.API
```

Migrations are applied automatically on startup. API is available at `http://localhost:5040`.

## Configuration

All settings live in `OpenPlan.API/appsettings.json` and can be overridden with environment variables (double-underscore as separator):

| Variable | Default | Description |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | see appsettings.json | PostgreSQL connection string |
| `Jwt__Secret` | *(must change)* | JWT signing secret — at least 32 characters |
| `Jwt__Issuer` | `openplan` | JWT issuer claim |
| `Jwt__Audience` | `openplan-clients` | JWT audience claim |
| `Cors__Origins` | `http://localhost:5041` | Comma-separated allowed CORS origins |

> Never use the default `Jwt__Secret` in production. Generate one with `openssl rand -base64 32`.

## Migrations

```bash
# Add a new migration
dotnet ef migrations add <Name> --project OpenPlan.API

# Apply manually
dotnet ef database update --project OpenPlan.API
```

## Documentation

| Document | Description |
|---|---|
| [API Reference](docs/api-reference.md) | All REST endpoints with request/response shapes |
| [Architecture](docs/architecture.md) | Design decisions, service structure, task model rules |
| [Admin API](docs/admin-api.md) | Admin-only endpoints: users, access control, versioning, backups |
| [Updater Service](docs/updater.md) | Linux updater daemon for version switching |
| [Setup & Self-Hosting](docs/setup.md) | Local dev and production deployment |

## Versioning

The current API version is recorded in [`version.json`](version.json) at the repo root. This file is read by the admin panel to display and compare versions. It must match the corresponding GitHub release tag exactly.

## License

[MIT](LICENSE)
