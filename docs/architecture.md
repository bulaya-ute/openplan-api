# Architecture

## Stack

- **.NET 8** Web API (controllers style)
- **EF Core 8** with Npgsql for PostgreSQL
- **BCrypt.Net** for password hashing
- **JWT Bearer** for authentication
- Migrations applied automatically on startup via `db.Database.Migrate()`

## Project Layout

```
OpenPlan.API/
  Controllers/       — HTTP layer: AuthController, TasksController, ProjectsController, AdminController
  Models/            — EF entities: User, TaskItem, Project, Admin, AccessControlEntry, AppSettings
  Data/              — AppDbContext
  DTOs/              — Request/response records (Auth, Tasks, Projects, Admin)
  Services/
    AuthService      — Registration, login, JWT issuance, first-admin elevation
    TaskService      — All task CRUD, tick logic, cascades
    TaskProgressService — Pure-static computed fields (progress, effectivePriority, etc.)
    ProjectService   — Project CRUD
    AdminService     — User management, access control, version info, backup/restore
  Middleware/
    AccessControlMiddleware — Per-request access check against AccessControlEntries
  Migrations/        — EF Core migrations
```

## Key Design Decisions

### Computed Fields, Not Stored Columns

`progress`, `effectivePriority`, `completedChildCount`, `totalChildCount`, and `nextChildTitle` are computed at read time by `TaskProgressService` — never persisted. `TaskService.MapToResponse` calls this before building the DTO.

### Recursive Subtree Loading

EF does not auto-eager-load the full task tree. `TaskService.LoadChildrenRecursiveAsync` issues one DB query per depth level to fully populate `TaskItem.Children`. Any code path that maps tasks must call this first.

### Tick Semantics

`POST /tasks/{id}/tick`:
- **Leaf** → complete directly
- **Sequential parent** → complete the next uncompleted child by `SortOrder`, then `TryAutoCompleteAncestorsAsync`
- **Parallel parent** → complete all children recursively, then `TryAutoCompleteAncestorsAsync`

### Auto-Complete Cascade

`TryAutoCompleteAncestorsAsync` walks up the `ParentId` chain. At each level, if all direct children are `Completed` or `Cancelled`, the parent is auto-completed recursively to the root.

### Sequential Progress

For sequential tasks, `TaskProgressService.GetSequentialChildren` includes children up to and including the first uncompleted one in the numerator, but adds placeholder zero-progress entries for subsequent children so their weight appears in the denominator — preventing progress from overstating completion.

---

## Admin System

### Role Model

Admin status is stored in a separate `Admins` table (PK = FK to `Users`). The first user to register is automatically elevated to admin. Admins can grant and revoke admin status for other users — they cannot view or modify other users' tasks.

### Access Control

`AppSettings` holds the current `AccessMode` (`Whitelist` or `Blacklist`). `AccessControlEntries` stores per-identifier allow/deny rules with a configurable `IdentifierType` (`UserId`, `Email`, `Username`).

`AccessControlMiddleware` runs on every authenticated request and rejects access even for holders of valid JWTs if the user is denied under the current mode. This means blocking a user takes effect immediately without waiting for token expiry.

Whitelist entries can reference email addresses that haven't registered yet, allowing an admin to pre-approve users before they create accounts. `UserId`-type entries only apply to existing users and are most useful for targeted blacklisting.

### Schema Versioning and Backups

Each API version carries a schema fingerprint derived from the ordered list of applied EF Core migration IDs (stored in `__EFMigrationsHistory`). This fingerprint is stored alongside every database backup.

Before restoring a backup the API checks that the fingerprint of the target version matches the backup's fingerprint and blocks the restore if they differ. Before switching to a new API version a warning is issued if the schema fingerprints differ, prompting the admin to take a backup first.

---

## Task Model Rules

Enums:
- `TaskType`: `Parallel` | `Sequential`
- `TaskPriority`: `P1` (highest) … `P4` (lowest)
- `ItemStatus`: `Scheduled` | `Active` | `Completed` | `Cancelled`

Invariants (enforced server-side):
1. **Sequential checkbox** — ticking a sequential parent completes its next uncompleted child, not the parent itself
2. **Parallel checkbox** — ticking a parallel parent completes all children recursively
3. **Sequential progress** — children after the first uncompleted one contribute weight to the denominator but 0 to the numerator
4. **`effectivePriority`** — minimum P-number across all uncompleted descendants (including self)
5. **Cancellation cascade** — cancelling a task recursively cancels all descendants
6. **`completedAt`** — set automatically by the server; never set from the client
