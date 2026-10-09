# Design

## Context

See `proposal.md` for the motivation and `specs/` for the behaviour.

**Distribution module today** (`backend/src/Modules/Distribution/PolvorApp.Distribution`, schema
`distribution`):
- `DistributionDay` (xmin `Version`), `DistributionSlot` and `PickupProxy`, with cross-schema
  foreign keys to `orders.edition_entries` added in the migration.
- The lists are derived: `DistributionDocuments` reads `IOrderExports.ListValidatedAsync` and
  passes it through `DistributionLists` (pure) and `DistributionNumbering` (orders and numbers the
  rows). Nothing about a handover exists yet: there is no flask number and no `Handover`.
- Writes use `IAuditTrail.Record` in the same `SaveChanges`. Documents use `IAuditLog.RecordAsync`
  before sending. `DistributionWriteGuard` maps lock timeouts to `Busy` (503).
- Endpoints sit under `/api/distribution`, with a 64 KB request limit. Lists use the `Exports`
  rate limit (30/min/user). There are no idempotency keys anywhere.

**Auth (ADR-0004)**:
- a cookie session, 60 min idle and 12 h absolute;
- anti-forgery by double submit (`X-XSRF-TOKEN`);
- a 401 calls `setUnauthorizedHandler`, which `RequireSession` uses to `queryClient.clear()` and
  redirect to sign-in. A queue kept only in memory would therefore be lost.

**PWA (`vite.config.ts`)**:
- `generateSW` with `autoUpdate`, `skipWaiting` and `clientsClaim`;
- `navigateFallbackDenylist: [/^\/api\//]`, `runtimeCaching: []`, default `globPatterns` (js, css,
  html: no fonts);
- the CSP has `connect-src 'self'` and `worker-src 'self'`;
- there is no IndexedDB code, no `idb` and no query persister.

**Data**:
- Scenarios (CI) seeds the current edition's orders as `SUBMITTED`/`DRAFT`, so the current lists
  are empty; Full has validated current orders.
- The privacy contract test (`PersonalDataCoverageTests`) detects tables by personal column names,
  such as `holder_entry_id` and `proxy_entry_id`, and requires a participant for each one.

## Goals / Non-Goals

**Goals:**
- Capture must never need the network or a live session. Sync needs both.
- Sync is idempotent, decides each handover on its own, and never loses a capture silently.
- Keep the PWA invariant: no `/api` response in the service-worker cache.
- Add no new architecture. One small dependency at most.

**Non-Goals:**
- A generic offline framework, a query-cache persister, service-worker Background Sync (WebKit
  lacks it, and the cookie and anti-forgery token are page concerns), or CRDT-style merging.
- Offline reads of any other screen.

## Decisions

### D1. `Handover` lives in the Distribution module

Table `distribution.handovers`:

| Column | Notes |
|---|---|
| `id` | uuid, generated on the device: the idempotency key |
| `distribution_id` | FK to `distribution.distributions`, `RESTRICT`: a day with handovers cannot be deleted |
| `holder_entry_id` | FK to `orders.edition_entries`, `CASCADE` (BR-14 removal) |
| `collected_by` | `HOLDER` or `PROXY` (code) |
| `collector_entry_id` | the proxy's entry when `PROXY`, else null; FK to `orders.edition_entries`, `SET NULL`, so a removed proxy entry keeps the role |
| `distribution_number` | int, the copy shown at capture |
| `powder_kg` | smallint 1–2, the entry's kg at recording (BR-05), as `orders.edition_entries` |
| `rental_flask_number` | varchar(20), nullable |
| `traceability1`, `traceability2` | varchar(50), nullable |
| `collected_at` | timestamptz, device time |
| `recorded_at` | timestamptz, server time |
| `xmin` | `Version` |

Checks: `collected_by IN ('HOLDER','PROXY')`, `collected_by = 'PROXY' OR collector_entry_id IS
NULL`, `collector_entry_id <> holder_entry_id`, and texts never blank (trimmed or null), so a blank
flask number cannot take a slot of its own in the unique index. The collector index is partial (not
null).

Indexes:
- `ux_handovers_distribution_holder` on (`distribution_id`, `holder_entry_id`);
- `ux_handovers_distribution_flask`, a functional unique index on
  (`distribution_id`, `upper(rental_flask_number)`) where the number is not null.

The database enforces both rules under concurrency. A unique violation is mapped to
`alreadyHandedOver` or `flaskNumberTaken` (same pattern as `alreadyPlanned`).

The column names `holder_entry_id` and `collector_entry_id` keep the privacy test's detection
honest. `holder_entry_id` is a detected name, so the test forces `DistributionPersonalData` to own
the table.

- *Alternative: a column on `EditionEntry`.* Rejected. It would mix the Orders module with on-site
  facts, and the collector, the traceability codes and the times would need columns of their own.
  The data model already defines `Handover`.

### D2. Endpoints (Admin policy, anti-forgery as usual)

| Method and route | Purpose |
|---|---|
| `GET /api/distribution/distributions/{id}/capture` | Capture package: day, rows, proxies and existing handovers. `no-store`, audited first (503 if not), `Exports` rate limit. Rows come from the same `DistributionLists` and `DistributionNumbering` path, so the numbers equal the printed list. |
| `POST /api/distribution/distributions/{id}/handovers/sync` | Body `{ handovers: [...] }`, at most 100. Returns `{ results: [{ id, outcome, code?, existing? }] }`. New `HandoverSync` rate limit (60/min/user, configurable like the others). |
| `DELETE /api/distribution/handovers/{id}?version=` | Undo: audited, 409 `distribution.modified` when stale. |
| `GET /api/distribution/editions/{id}` (existing plan) | Gains `handedOverCount` and `holderCount` on the powder day, for the "N of M delivered" count. |

`DELETE /distributions/{id}` gains 409 `distribution.hasHandovers`.

Sync processing is sequential, one transaction per handover:
1. Load the day's eligible holders and proxies once per batch, from `IOrderExports` and the
   proxies.
2. For each item:
   1. An existing id with equal data is `alreadyRecorded`; with different data, `refused` with
      `distribution.handoverChanged`.
   2. Validate (D3) and insert with its audit record in one `SaveChanges`.
   3. A unique violation becomes the matching code, with `existing` loaded for
      `alreadyHandedOver`.
   4. A lock timeout or an audit failure becomes `refused`/`distribution.busy`, which the device
      retries.

A batch over the rate limit gets 429 as a whole: the limiter runs before the handler.

- *Alternative: one request per handover.* Rejected: 800 holders would exceed any sensible
  per-user limit, and one bad item would not block the others anyway.
- *Alternative: an all-or-nothing batch.* Rejected: one conflict would stall the whole queue.

### D3. Validation, server side (blocking) and mirrored on the device

The server checks, in order:
1. `IN_PROGRESS` edition, otherwise `editionNotInProgress`.
2. The day is `POWDER`, otherwise `captureNotPowder`.
3. The holder is in the derived powder list now, otherwise `notInList`.
4. The collector is the holder or the holder's holding `POWDER` proxy, otherwise `proxyNotValid`.
5. Flask number rules against `EditionEntry.Flask`: `flaskNumberRequired` or
   `flaskNumberNotRented`, then trim and 1 to 20 characters (400 naming the field in a single
   write; `refused` with `distribution.invalid` in a batch).
6. Traceability: at most 50 characters, trimmed, blank stored as null.

`powderKg` and `distributionNumber` come from the device's package. The server stores the
current kg of the entry, so a stale device cannot write an old quantity (BR-05). It accepts the
number copy as given, an int ≥ 1.

The device mirrors rules 4–6 and the per-day flask uniqueness against the package's handovers and
its own queue. That gives instant feedback offline; the server stays the authority.

### D4. Frontend: route outside `RequireSession`, IndexedDB store, sync engine

**Route.** `/distribution/capture/:distributionId` is mounted **outside** `RequireSession`. The
screen reads its owner and package from IndexedDB, so it renders offline and with an expired
session.
- Online, it checks `GET /api/account` in the background:
  - a different user clears the device and shows the "not allowed" page;
  - a 401 shows "Sign in to sync" and keeps capturing;
  - a FiringChief gets "not allowed".
- Without a package, it needs the network and an Admin session, and offers to prepare the device
  (the "not allowed" page for anyone else).
- Every other route keeps `RequireSession`.

**Store** (`features/distribution/offline/`). One IndexedDB database, `polvorapp-capture`, with
three object stores:
- `packages`, keyed by `distributionId`: `ownerUserId`, `downloadedAt`, `expiresAt` (+7 days),
  rows and the server handovers;
- `queue`, keyed by handover id: payload, state (`pending`, `conflict`), conflict code and
  `existing`, `ownerUserId`;
- `meta`: last sync and owner.

Access goes through `idb`, Jake Archibald's ~1 KB promise wrapper, if Context7 confirms it fits
React 19 and Vite 8 builds (task 1.1). Raw IndexedDB is the fallback. Neither overlaps a fixed
library, so no ADR is needed. Store functions are pure async modules, tested with
`fake-indexeddb` under Vitest.

**Sync engine** (`useHandoverSync`):
- **Triggers:** `online` events, opening the screen, "Sync now", and a 30 s interval while online
  with pending items.
- **Sending:** it fetches the anti-forgery token when needed and sends batches of 100.
- **Results:** it applies each outcome to the queue:
  - `recorded` and `alreadyRecorded` move the item into the package's server handovers;
  - `refused` marks it as a conflict, except `busy`, which stays pending with backoff.
- **Session expiry:** a 401 stops the run without touching the queue. The global 401 handler
  clears only the in-memory query cache, never IndexedDB. Because the capture route is outside
  `RequireSession`, the screen stays where it is.
- **Connectivity:** "online" means `navigator.onLine` plus the last request succeeding. A network
  `TypeError` counts as offline.

**Ownership and clearing (SEC-14)**:
- `signOut` checks the queue first. With pending items, a `ConfirmDialog` states the count;
  confirming clears the database, then signs out.
- On session load, a different `userId` from the stored owner clears the packages and hides that
  owner's queue (kept for 7 days, keyed by owner).
- App start purges packages past `expiresAt` and queues older than 7 days.
- "Close capture" clears the package once nothing is pending.

### D5. PWA: the shell opens offline, still no API caching

- `workbox.globPatterns` gains `woff2`, `svg`, `png` and `webmanifest`, so fonts and icons render
  offline.
- `navigateFallback: 'index.html'` (the generateSW default) serves the shell for
  `/distribution/capture/*`. The denylist stays `/api/`.
- `runtimeCaching` stays empty, so the platform requirement "no `/api` response cached" still
  holds. The data comes from IndexedDB, filled by an explicit download.
- The CSP needs no change.

The other screens already fail their queries offline and show their error states. The new
platform scenario pins that behaviour with a test, with no new code beyond a translated "cannot
reach the server" message if one is missing.

### D6. Lists filled from handovers

`DistributionLists` takes the day's handovers (a dictionary by holder entry) and fills the flask
number, the traceability columns and "collected by" (`holder` or `proxy`). Its header adds "N
handovers recorded". The numbering is unchanged: it stays derived (spec *Global numbering*).

### D7. Screens

- **Distribution page, powder day card (Admin, edition in progress):**
  - "N of M delivered";
  - "Prepare for offline capture", which downloads the package and stores it;
  - "Open capture" when the device has the package.
- **Capture screen:**
  - a status bar: online/offline, pending, conflicts, last sync, "Sync now", and the package's
    downloaded and cleared dates;
  - a searchable list grouped by slot and comparsa, each row with its number, name, kg, flask and
    a state tag (text plus icon, `Tag` composite);
  - a conflicts section.
- **Handover panel:** `EditSheet`, a bottom sheet on phones, with React Hook Form + Zod. The
  collector is a radio (holder or proxy). The flask number field shows only for rented flasks.
- **Composites:** only `components/app/` composites (ADR-0009). New strings go in
  `distribution.json` in all three locales.

### D8. Security, GDPR and audit

- **Authorisation:** every endpoint is Admin-only, enforced server-side (BR-12). FiringChiefs get
  403. The capture route's client guard is cosmetic; the API is the gate.
- **Audit:**
  - package download: `distribution.capturePackageDownloaded` with the distribution and row
    count, before sending;
  - each recorded handover: `distribution.handoverRecorded`;
  - each undo: `distribution.handoverUndone`.

  Entries carry the distribution, the entry, the flask number and whether a proxy collected. They
  carry no names or DNI/NIE (SEC-05, NFR-12).
- **New SEC-14 (`docs/compliance.md`):** personal data on a device is limited to:
  - the day's powder list (name, DNI/NIE, kg, flask, proxy), downloaded on purpose by an Admin
    and audited;
  - kept in IndexedDB, never in the service-worker cache, and owned by that Admin;
  - cleared on sign-out (confirmed when unsynced), on a user switch, on closing the capture, and
    after 7 days at most.

  Device encryption and screen lock are the Admin's responsibility. This is stated in the
  record of processing (SEC-10).
- **GDPR:**
  - `DistributionPersonalData` exports the handovers where the person is holder or collector,
    each shown by role only (SEC-09);
  - erasure keeps the handovers, which hold no identity, linked to the anonymised entries;
  - a removed entry cascades its handover;
  - a collector who is removed becomes null while `collected_by` stays `PROXY`, so the handover
    still reads "proxy" by role;
  - `HandledBy` gains `distribution.handovers`.
- **Retention:** handovers follow the edition entries' retention (SEC-08).

### D9. Seed and tests

- **Seed:** the past edition gets synthetic handovers with invented flask numbers, so lists,
  exports and the GDPR export have data. The current edition gets none.
- **Backend:** xUnit with Testcontainers covering:
  - the package (rows equal the list, audit before sending, 409 and 403 cases);
  - sync (idempotency, per-item outcomes, concurrent devices racing on one entry and one flask
    number through two parallel requests, rate limit, busy);
  - undo, day deletion blocked, lists filled, privacy export, coverage test.
- **Frontend:** Vitest with `fake-indexeddb` for the store, the engine (outcomes, 401 keeps the
  queue), ownership and clearing; Testing Library and axe for the screens.
- **E2E (Playwright, `serial-state` project, because it validates an order and records
  handovers):**
  1. An Admin validates Norte's order through the API.
  2. The Admin prepares the device, goes offline (`context.setOffline(true)`) and reloads the
     capture route: it opens from the precache.
  3. The Admin records handovers, including a flask conflict on the device.
  4. The Admin goes online and syncs.
  5. A second browser context syncs a conflicting handover and sees the conflict.
  6. The Admin downloads the powder list and sees the values filled in.

  The spec also covers the sign-out warning, axe on the screens, and the 360 px project. Undo
  restores the data for later specs.

### Research notes (task 1.1, 2026-10-09)

**`idb`** (Context7 `/jakearchibald/idb`, High reputation):
- `openDB<Schema>(name, version, { upgrade })` with a typed `DBSchema`;
- the `get`/`put`/`delete`/`getAll`/`clear` shortcuts;
- multi-store `db.transaction([...], 'readwrite')` with `tx.done`.

It is about 1.2 KB brotli'd. Adopted.

**`fake-indexeddb`**: `import 'fake-indexeddb/auto'` in the store tests gives jsdom a working
IndexedDB. Each test deletes the database to stay isolated.

**vite-plugin-pwa `generateSW`** (Context7 `/vite-pwa/vite-plugin-pwa`):
- the default `globPatterns` is `**/*.{js,css,html}`;
- a custom list MUST keep `js`, `css` and `html`, or the worker fails with `non-precached-url
  index.html`;
- `navigateFallback` serves `index.html` for navigations not in the denylist.

The list becomes `**/*.{js,css,html,woff2,svg,png,ico,webmanifest}`.

**Npgsql EF Core**:
- `HasIndex(...).IsUnique().HasFilter(...)` covers filtered indexes, but there is no fluent API
  for expression indexes;
- `ux_handovers_distribution_flask` on (`distribution_id`, `upper(rental_flask_number)`) WHERE
  not null is therefore created with `migrationBuilder.Sql`, as the cross-schema keys already
  are;
- violations map through the existing `DistributionProblems.Violates(exception,
  PostgresErrorCodes.UniqueViolation, indexName)` pattern, as `alreadyPlanned` does.

**Browser and Playwright**:
- `navigator.storage.persist()` returns `Promise<boolean>` and is missing in jsdom and some
  WebViews, so it is guarded;
- Playwright's `context.setOffline(true)` also cuts service-worker network requests, so a reload
  proves the precache serves the shell.

## Risks / Trade-offs

- **[Personal data on an unattended device]** → Bounded by SEC-14: one day's list, owner-bound,
  cleared on sign-out, on a user switch and on closing, 7 days at most. Nothing goes into the
  service-worker cache. The capture route offline shows data without a live session by design,
  because the session idles out after 60 min on the field. The device lock is the safeguard, and
  the docs and the record of processing say so.
- **[Clock skew on devices]** → `collected_at` is informational, and `recorded_at` is the server
  truth. Ordering never depends on device time.
- **[An order changes after the package was downloaded]** → The server re-derives eligibility at
  sync (`notInList`) and stores the current kg. The device shows the conflict, and the Admin can
  re-download the package.
- **[Two devices give the same flask number to different people]** → The unique index makes the
  second sync refuse it. Both handovers are shown so the Admin can fix the number. This rule is
  blocking by design.
- **[Lost captures if the browser storage is evicted]** → An installed PWA's storage is rarely
  evicted. The app also requests `navigator.storage.persist()` on preparing the device and shows
  the result. The pending count stays visible.
- **[Precache size grows with fonts]** → About 120 KB of WOFF2 more, cached once.
- **[Scenarios seed has no validated current orders]** → The E2E validates one order in
  `serial-state`, so other specs are not affected.

## Review notes (task 6.6, 2026-10-09)

Fixed after the parallel reviews of the capture screen:

- A sync only runs for a session checked to be the package's owner: never while the session is
  being checked or cannot be, and never under another Admin's cookie (SEC-14). A 401 during a sync
  re-checks the session, so the screen asks to sign in.
- The screen renders from the device without waiting for the session, which is checked with a
  timeout (captive portals).
- The handover panel keeps the kind it was opened as, and what it opened on, until it closes: a
  sync that records the handover meanwhile no longer swaps the form under the Admin. Focus returns
  to the holder's row when the button that opened the panel (a conflict) is gone.
- "Remove handover" is confirmed; a conflict sent again replaces the old one in one transaction.
- A handover captured during a sync run is sent by a run that follows it; a run that timed out is
  tried again while the device says it is online.
- The capture screen has its own notice region; connectivity changes, new conflicts and the
  number of search matches are announced; the slot groups are no longer landmarks.
- The session-ended event is sent before leaving the signed-in pages, so their listeners clear the
  packages.

Accepted, with the reason:

- Signed out or offline, the device's package is readable by whoever holds the unlocked device
  (D4, SEC-14): a session that idles out on the field must lose nothing. The device lock is the
  safeguard; the maintainer kept the 7-day lifetime.
- Signing out clears the device after the server ends the session, not before: clearing first
  would lose the pending handovers when signing out fails offline.

Follow-ups, not blocking:

- "Sync now" uses native `disabled`; an `aria-disabled` pattern would keep focus on it when the
  last handover syncs.
- The distribution page's "N of M delivered" refreshes with the plan query, not on return from the
  capture screen.
- `DayCapture` offers "Open capture" for a package past its lifetime until the housekeeping runs.

## Verification notes (task 8.2, 2026-10-09)

- The E2E spec passed twice in a row on the compose stack, with the rest of `serial-state`. The
  local full suite is green except `badges` (the local database holds the `Full` dataset, so the
  seeded person is on another page; CI uses `Scenarios`) and one Firefox photo test that passed on
  a retry.
- Fixed from `pr-test-analyzer`: a handover whose audit entry cannot be stored was answered
  `invalid` (a conflict on the device) instead of `busy`; the audit entry is now saved apart in the
  same transaction, and a test covers it. The offline "undo needs the connection" state and closing
  the capture (allowed and blocked) gained tests.
- Fixed from `e2e-runner`: the cleanup leaves the capture screen before undoing, validating Norte is
  idempotent, the service-worker wait is longer, pending handovers are checked across an offline
  reload, and the sign-out step cannot end the session the cleanup needs.
- Fixed from `doc-updater`: the owner's pending handovers are no longer purged after 7 days (the
  spec only clears another Admin's); licences of `idb` and `fake-indexeddb`; NFR-03, `mvp.md` and
  SEC-14 wording.
- Backend: 96.8 % line coverage; the full suite passed except 7 tests of other modules that hit
  the Testcontainers PostgreSQL's shared-memory limit (`53100`) and passed on a rerun. A test now
  covers the number a handover keeps when a later list renumbers the holder.
- Follow-ups, not blocking: tests for the remaining `notInList` variants and for storage failures
  while capturing; an E2E pass at 360 px.

## Migration Plan

- An additive migration creates `distribution.handovers` and changes the cross-schema FKs. Roll
  back by dropping the table, since nothing else reads it.
- The service worker updates itself (`autoUpdate`). Old clients keep working, because none has
  capture data before this release.

## Open Questions

- Q-43: the meaning of traceability 1/2. It stays free text and can be constrained later without
  changing the data shape.
