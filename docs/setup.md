# Setup & Self-Hosting

## Local Development

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL 14+

### 1. Database

```sql
CREATE USER openplan WITH PASSWORD 'openplan';
CREATE DATABASE openplan OWNER openplan;
```

Or update `ConnectionStrings:DefaultConnection` in `OpenPlan.API/appsettings.json`.

### 2. Run

```bash
dotnet run --project OpenPlan.API
```

Migrations apply automatically on startup. API is at `http://localhost:5000`.

### 3. Manual Migrations

```bash
dotnet ef database update --project OpenPlan.API
dotnet ef migrations add <MigrationName> --project OpenPlan.API
```

---

## Environment Variables

Override any `appsettings.json` value with environment variables using `__` as separator:

| Variable | Default | Description |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | see appsettings.json | PostgreSQL connection string |
| `Jwt__Secret` | *(must change)* | JWT signing secret — at least 32 characters |
| `Jwt__Issuer` | `openplan` | JWT issuer |
| `Jwt__Audience` | `openplan-clients` | JWT audience |
| `Cors__Origins` | `http://localhost:5173` | Comma-separated allowed CORS origins |

> **Security:** Generate a strong JWT secret with `openssl rand -base64 32`. Never commit secrets to source control.

---

## Production (Linux + systemd)

### 1. Publish

```bash
dotnet publish OpenPlan.API -c Release -o /opt/openplan-api
```

### 2. systemd Unit

Create `/etc/systemd/system/openplan-api.service`:

```ini
[Unit]
Description=OpenPlan API
After=network.target postgresql.service

[Service]
Type=simple
User=openplan
WorkingDirectory=/opt/openplan-api
ExecStart=/opt/openplan-api/OpenPlan.API
Restart=always
RestartSec=5
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=Jwt__Secret=<your-secret>
Environment=ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=openplan;Username=openplan;Password=<password>
Environment=Cors__Origins=https://app.yourdomain.com,https://admin.yourdomain.com

[Install]
WantedBy=multi-user.target
```

```bash
systemctl daemon-reload
systemctl enable --now openplan-api
```

### 3. Reverse Proxy (Nginx)

```nginx
server {
    listen 443 ssl;
    server_name api.yourdomain.com;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
```

### 4. Updater Service

See [updater.md](updater.md) for setting up the version-switching daemon used by the admin panel.
