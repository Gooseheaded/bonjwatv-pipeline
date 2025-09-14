# Webapp Admin — Creator Mappings UI

Admin UI for managing creator canonicalization mappings and triggering backfills.

## Navigation
- Admin → Creator Mappings (`/Admin/CreatorMappings`)

## Pages (Razor Pages)
- `Pages/Admin/CreatorMappings/Index.cshtml`
  - Table: `source`, `canonical`, `notes`, `updated_at`.
  - Actions: Edit, Delete (inline), Create (button), Reapply (button), Try‑it widget.
  - Search input (client‑side debounced, calls API with `q`).
  - Pagination controls (page/pageSize).

- `Pages/Admin/CreatorMappings/Create.cshtml`
  - Fields: `source` (required), `canonical` (required), `notes` (optional).
  - Server‑side validation summary; show 409 conflict for duplicate `source`.

- `Pages/Admin/CreatorMappings/Edit.cshtml`
  - Same fields as Create; includes `id` hidden.
  - Show last updated timestamp and editor.

- `Pages/Admin/CreatorMappings/Delete.cshtml` (or inline modal on Index)
  - Confirm deletion; show warning about reapply requirement to affect existing rows.

## Client Layer
- Add `ICreatorMappingsClient` with implementation `HttpCreatorMappingsClient` using `CATALOG_API_BASE_URL`.
- Methods:
  - `Task<Paged<Mapping>> ListAsync(string? q, int page, int pageSize)`
  - `Task<Mapping> CreateAsync(NewMapping m)`
  - `Task<Mapping> UpdateAsync(Guid id, NewMapping m)`
  - `Task DeleteAsync(Guid id)`
  - `Task<ReapplyResult> ReapplyAsync(string scope = "all")`
  - `Task<string?> ResolveAsync(string source)` (optional: local resolve by fetching list and applying normalization)

## Try‑it Widget
- Input: `source` string.
- On change/click: call `ResolveAsync` or locally normalize and match against current page results; show resolved `canonical` or "no mapping".

## Permissions
- Gate all pages by existing admin check (`ADMIN_USER_IDS`).
- Show friendly 403 if unauthorized.

## UX Notes
- Keep list snappy; optimistic UI for create/edit/delete; fall back to server messages on conflicts.
- Toasts for success/error; spinner for Reapply with result counts.

## Telemetry
- Log admin actions (create/update/delete/reapply) with actor and result.

