# API Reference

All endpoints are under `/api/v1/`. All endpoints except `/auth/register` and `/auth/login` require a JWT in the `Authorization: Bearer <token>` header.

Responses are JSON. Enums are returned as strings (e.g. `"Parallel"`, `"P1"`, `"Scheduled"`).

---

## Auth

### `POST /auth/register`

Register a new account. Blocked if access mode is `Whitelist` and the username is not on the whitelist.
The first user to register is automatically granted admin privileges.

**Request**
```json
{ "username": "user@example.com", "password": "secret", "displayName": "Alice" }
```

**Response `200`**
```json
{
  "accessToken": "eyJ...",
  "userId": "uuid",
  "username": "user@example.com",
  "displayName": "Alice",
  "isAdmin": false
}
```

---

### `POST /auth/login`

**Request**
```json
{ "username": "user@example.com", "password": "secret" }
```

**Response `200`** — same shape as register.

---

## Tasks

### Task object

```json
{
  "id": "uuid",
  "ownerId": "uuid",
  "projectId": "uuid | null",
  "parentId": "uuid | null",
  "title": "string",
  "description": "string | null",
  "taskType": "Parallel | Sequential",
  "weight": 1.0,
  "priority": "P1 | P2 | P3 | P4",
  "effectivePriority": "P1 | P2 | P3 | P4",
  "status": "Scheduled | Active | Completed | Cancelled",
  "startAt": "ISO 8601",
  "dueAt": "ISO 8601",
  "completedAt": "ISO 8601 | null",
  "sortOrder": 0,
  "progress": 0.6,
  "completedChildCount": 3,
  "totalChildCount": 5,
  "nextChildTitle": "string | null",
  "createdAt": "ISO 8601",
  "updatedAt": "ISO 8601",
  "children": []
}
```

**Computed fields** (derived at read time, not stored):
- `effectivePriority` — minimum P-number across all uncompleted descendants
- `progress` — weighted completion ratio (0.0–1.0)
- `completedChildCount` / `totalChildCount` — direct children counts
- `nextChildTitle` — next uncompleted child title (sequential tasks only)
- `children` — fully loaded recursive subtree

---

### `GET /tasks?view=<view>`

Returns root tasks filtered by view. Each task includes its full subtree.

**Views:** `today`, `upcoming`, `inbox`, or any other string returns all root tasks.

---

### `GET /tasks/project/{projectId}`

Returns root tasks for the given project with full subtrees.

---

### `GET /tasks/{id}`

Returns a single task with its full subtree.

---

### `POST /tasks`

```json
{
  "title": "string",
  "description": "string (optional)",
  "projectId": "uuid (optional)",
  "parentId": "uuid (optional)",
  "taskType": "Parallel",
  "weight": 1.0,
  "priority": "P4",
  "startAt": "ISO 8601",
  "dueAt": "ISO 8601",
  "sortOrder": 0
}
```

---

### `PUT /tasks/{id}`

All fields optional. Status change side effects:
- `Completed` → sets `completedAt` to now, cascades auto-complete upward
- `Cancelled` → recursively cancels all descendants

---

### `POST /tasks/{id}/tick`

Advances task completion:
- **Leaf** → `Completed`
- **Sequential with children** → completes next uncompleted child by `SortOrder`
- **Parallel with children** → completes all children recursively

---

### `DELETE /tasks/{id}`

Deletes task and all descendants. **Response `204`**

---

## Projects

### Project object

```json
{
  "id": "uuid",
  "ownerId": "uuid",
  "name": "string",
  "color": "#6366f1",
  "isArchived": false,
  "sortOrder": 0,
  "createdAt": "ISO 8601",
  "updatedAt": "ISO 8601"
}
```

### `GET /projects` · `POST /projects` · `PUT /projects/{id}` · `DELETE /projects/{id}`

Standard CRUD. POST/PUT accept `{ "name": "string", "color": "#hexcolor" }`.

---

## Admin

All admin endpoints require the authenticated user to have admin privileges. Returns `403` otherwise.

See [admin-api.md](admin-api.md) for full admin endpoint reference.
