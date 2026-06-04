# Admin API Reference

All endpoints are under `/api/v1/admin/`. Require `Authorization: Bearer <token>` with an admin-privileged account. Return `403` for non-admin users.

---

## Users

### `GET /admin/users`

List all registered users.

**Response `200`**
```json
[
  {
    "id": "uuid",
    "username": "user@example.com",
    "displayName": "Alice",
    "isAdmin": false,
    "adminAddedAt": "ISO 8601 | null",
    "adminAddedBy": "uuid | null",
    "createdAt": "ISO 8601"
  }
]
```

---

### `POST /admin/users/{id}/grant-admin`

Grant admin privileges to a user.

**Response `200`**

---

### `DELETE /admin/users/{id}/revoke-admin`

Revoke admin privileges from a user. Cannot revoke your own admin status.

**Response `200`**

---

## Access Control

### `GET /admin/settings`

Returns the current access control configuration.

**Response `200`**
```json
{
  "accessMode": "Whitelist | Blacklist",
  "entries": [
    {
      "id": "uuid",
      "identifierType": "UserId | Email | Username",
      "identifierValue": "string",
      "listType": "Whitelist | Blacklist",
      "addedAt": "ISO 8601",
      "addedBy": "uuid"
    }
  ]
}
```

---

### `PUT /admin/settings/mode`

Switch access control mode.

**Request**
```json
{ "accessMode": "Whitelist | Blacklist" }
```

**Response `200`**

---

### `POST /admin/access-control`

Add an access control entry.

**Request**
```json
{
  "identifierType": "UserId | Email | Username",
  "identifierValue": "string",
  "listType": "Whitelist | Blacklist"
}
```

> Note: `UserId`-type whitelist entries only match existing users and cannot pre-approve registrations. Use `Email` or `Username` for pre-registration whitelisting.

**Response `200`** — the created entry.

---

### `DELETE /admin/access-control/{id}`

Remove an access control entry.

**Response `204`**

---

## Version Management

### `GET /admin/version`

Returns the current API version and schema fingerprint.

**Response `200`**
```json
{
  "version": "0.1.0",
  "schemaHash": "sha256:abc123...",
  "migrations": ["20260601042437_InitialCreate"]
}
```

---

### `GET /admin/version/available`

Fetches available releases from the GitHub API for `openplan-api`.

**Response `200`**
```json
[
  {
    "tag": "v0.2.0",
    "publishedAt": "ISO 8601",
    "notes": "Release notes..."
  }
]
```

---

### `POST /admin/version/switch`

Trigger a version switch via the updater daemon. The updater handles fetching, building, migrating, and restarting.

**Request**
```json
{ "targetVersion": "v0.2.0" }
```

**Response `200`** if the updater acknowledged the request. The API will restart; the client should poll until the new version is live.

> If the target version has a different schema hash than the current one, the response includes a `schemaWarning` field. The admin must acknowledge by sending `{ "targetVersion": "v0.2.0", "acknowledgeSchemaChange": true }`.

---

## Database Backups

### `GET /admin/backups`

List all database backups.

**Response `200`**
```json
[
  {
    "id": "uuid",
    "filename": "backup-2026-06-04T12-00-00.sql.gz",
    "apiVersion": "0.1.0",
    "schemaHash": "sha256:abc123...",
    "createdAt": "ISO 8601",
    "sizeBytes": 102400
  }
]
```

---

### `POST /admin/backups`

Create a new database snapshot (`pg_dump`). Stored with metadata (API version + schema hash).

**Response `200`** — the created backup record.

---

### `POST /admin/backups/{id}/restore`

Restore a backup. Blocked if the current API version's schema hash does not match the backup's schema hash.

**Response `200`** on success · `409` on schema hash mismatch (with current and backup hashes in the response body).

---

### `DELETE /admin/backups/{id}`

Delete a backup file and its metadata record.

**Response `204`**
