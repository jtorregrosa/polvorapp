# Design

## Context

See proposal.md for the motivation. The current state that shapes the approach:

- **Audit write side** (`add-identity-access`, D-audit):
  - `AuditEntry` lives in `PolvorApp.SharedKernel/Auditing` and maps to `audit.audit_entries`.
  - Every module context maps the table with `ExcludeFromMigrations`, so an entry commits in the
    module's own transaction.
  - `AuditDbContext` (module `AuditPrivacy`, migration order 0) owns the table.
  - Indexes already exist for the viewer: `occurred_at`; `(entity_type, entity_id)`;
    `(actor_user_id, occurred_at)` and `(comparsa_id, occurred_at)`, both filtered.
  - `IAuditTrail.Record` writes inside the caller's context; `IAuditLog.RecordAsync` writes alone.
    Downloads use the latter, and a file that cannot be audited is not sent (`503`).
  - `data` is free-form JSON, capped at 16 KB, and names that look like secrets are refused.
  - Every module audits under its own constant action codes. The full list is about 70 codes in
    seven modules.
- **Append-only today**:
  - `AppendOnlyAuditGuard`, a `SaveChangesInterceptor` on every context, catches tracked entries
    that are modified or deleted;
  - `AuditTrailRulesTests` greps `src/**` for `ExecuteUpdate`/`ExecuteDelete`/raw SQL on the table;
  - `OpenApiDocumentTests` forbids non-GET operations named "audit";
  - the database itself has no guard: there is one connection string and one role.
- **Personal data in audit `data`**: only two cases.
  - `UserUpdated` holds `previous.name` and `current.name`.
  - `SignInFailed` for an unknown account holds `attemptedEmail`.
  - Everything else is ids, counts and field names (registry D7, orders, distribution, exports).
- **Personal data elsewhere**:
  - `registry`: arquebusiers, owned weapons and photo rows. Images live in private storage under
    `registry/photos/`, and the hourly `StoredObjectSweeper` erases orphans.
  - `orders`: entries and loans hold the history copies. Their columns are already nullable "so a
    GDPR erasure (#15) can blank it". `arquebusier_id` and `owned_weapon_id` are
    `ON DELETE SET NULL`.
  - `distribution`: proxies hold entry ids only.
  - `identity`: users, tokens, claims and logins.
  - `catalog`: FiringChief assignments.
  - `notifications`: opt-outs, deliveries, and events (ids only).
- **Arquebusier deletion** (`ArquebusierAdministration.DeleteAsync`):
  1. locks the row `FOR UPDATE`;
  2. calls each `IArquebusierDeletionParticipant` on the same `DbTransaction`. Orders uses this to
     remove the current-edition entry while the orders are open;
  3. removes the row and records `ArquebusierDeleted`;
  4. deletes the images after the commit.
- **Lists** return bare arrays, and `DataTable` paginates on the client. Nothing pages on the
  server yet.
- Module boundaries (`ModuleDependencyRules`): a module references only the SharedKernel and other
  modules' `.Contracts`.

## Goals / Non-Goals

**Goals:**
- One personal-data contract that every module implements. A new table with personal data then
  has an obvious place to be described, exported and erased, and a test that fails when it is
  forgotten.
- Erasure that is atomic across modules and reuses the arquebusier deletion instead of
  duplicating it.
- A database guard that would also stop a future bug or a raw SQL mistake, not only EF code.
- Server-side keyset paging for the audit log only; other lists keep client paging.

**Non-Goals:**
- A generic paging convention for every list.
- Translating the keys and values of audit `data`. The details panel shows the field names as
  recorded, because they are technical data for Admins and not UI text.
- A low-privilege runtime database role (a go-live item, see Risks).

## Decisions

### D1. Module layout

All new backend code lives in `Modules/AuditPrivacy`:

| Folder | Content |
|---|---|
| `Viewer/` | `AuditQuery` (filters, keyset), `AuditActionCatalog`, endpoints (not `AuditLog/`: a namespace would clash with the `AuditLog` class) |
| `Retention/` | `AuditRetentionOptions`, `AuditPurge` (scoped), `AuditRetentionService` (`BackgroundService`), `PurgeAuditCommand` (`IHostCommand`, verb `purge-audit`) |
| `Maintenance/` | `AuditMaintenance`, the only class allowed to update or delete audit rows (D4) |
| `Privacy/` | `PersonLookup`, `PersonalDataExport`, `PersonalDataErasure`, `PersonalDataWorkbook` (ClosedXML), endpoints |

`AuditPrivacy.Contracts` gains `IPersonalDataParticipant` and its records (D5).
`IAuditActionSource`, `AuditActionDefinition` and `AuditRetentionClass` live in
`SharedKernel.Auditing`, next to `IAuditTrail`, which every module already references (D2).

**Alternative**: a new `Privacy` module. Rejected: `docs/mvp.md` names a single `audit-privacy`
capability, and modules map one-to-one to capabilities.

### D2. Action catalogue owned by each module

Each module registers an `IAuditActionSource` that lists its codes as
`AuditActionDefinition(Code, EntityType, AuditRetentionClass)`. The two classes are `Security`
(1 year) and `Standard` (5 years). Modules move their existing constants into these sources; the
constants stay where they are.

The `AuditActionCatalog` is the union of all sources. It is used to:
- validate the `action` and `entityType` filters (`400` for an unknown code);
- give the purge the list of `Security` codes (D3);
- serve `GET /api/audit-entries/actions`, which the action filter's select uses.

Tests:
- `AuditTrail` refuses to record a code missing from the catalogue (a programming error, like a
  secret-looking data property). Codes are built at run time in places (`"ComparsaOrder" + Past(move)`),
  so a static scan cannot see them all; the integration suite, which audits every action, enforces
  the catalogue instead;
- a frontend test fails when a catalogue code has no `audit:actions.<Code>` label in one of the three
  locales. It reads the codes from the generated client: the OpenAPI document lists the catalogue
  as enums of `AuditActionResponse`, refreshed by the same build step.

`Security` codes: `SignedIn`, `SignInFailed`, `LockedOut`, `RecoveryCodeUsed`,
`PasswordResetRequested`, `AdminBootstrapRefused`, `LoanLenderLookedUp` and `PersonLookedUp`.
Every other code, and any code not in the catalogue (a renamed one, for example), is `Standard`.
That errs on keeping longer.

**Alternative**: a hard-coded list in the viewer. Rejected: it drifts silently with each new
module.

### D3. Retention purge

`AuditRetentionOptions`:
- `Audit:RetentionYears`, default 5, minimum 5;
- `Audit:SecurityRetentionDays`, default 365, minimum 365.

Both are validated at start-up with `ValidateOnStart`. The message names the setting only.

`AuditRetentionService` runs `AuditPurge` once a day:
- first at start-up plus one minute, then every 24 hours, using `PeriodicTimer` with `TimeProvider`,
  like `NotificationScheduler`;
- in batches of 5,000 rows per transaction, so one long transaction never locks the table.

Each batch (no defined order, `FOR UPDATE SKIP LOCKED`, served by the `(action, occurred_at, id)`
index) deletes, through `AuditMaintenance` (D4):
- the `Security` codes older than `now - SecurityRetentionDays`;
- everything else older than `now - RetentionYears` (calendar years from `TimeProvider`).

After the last batch it records one `AuditEntriesPurged` entry, with no actor and data
`{security, standard}` (counts), and only when something was deleted.

The host command `purge-audit` runs the same purge for operators.

Several API instances are not expected (NFR-08); two purges at once skip each other's locked rows.
The two periods are purged independently: when one fails, the other still runs, what was deleted is
still recorded (or logged with its counts when the record itself fails), and the run then fails.
Both settings are bounded (5–100 years, 365–36500 days), so a cut-off date can always be computed.

### D4. Database append-only guard

The `audit` migration adds a trigger:

```sql
CREATE FUNCTION audit.guard_audit_entries() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF current_setting('polvorapp.audit_maintenance', true) IS DISTINCT FROM 'purge'
     AND current_setting('polvorapp.audit_maintenance', true) IS DISTINCT FROM 'redact' THEN
    RAISE EXCEPTION 'audit entries are append-only' USING ERRCODE = 'P0001';
  END IF;
  IF TG_OP = 'DELETE' AND current_setting('polvorapp.audit_maintenance', true) <> 'purge' THEN
    RAISE EXCEPTION 'audit entries are only deleted by the retention purge';
  END IF;
  IF TG_OP = 'UPDATE' AND (current_setting('polvorapp.audit_maintenance', true) <> 'redact'
     OR (NEW.id, NEW.occurred_at, NEW.actor_user_id, NEW.action, NEW.entity_type, NEW.entity_id,
         NEW.comparsa_id, NEW.trace_id)
        IS DISTINCT FROM
        (OLD.id, OLD.occurred_at, OLD.actor_user_id, OLD.action, OLD.entity_type, OLD.entity_id,
         OLD.comparsa_id, OLD.trace_id)) THEN
    RAISE EXCEPTION 'audit entries may only have their data redacted';
  END IF;
  RETURN COALESCE(NEW, OLD);
END $$;
```

The trigger runs `BEFORE UPDATE OR DELETE` for each row. A statement trigger refuses `TRUNCATE`
unconditionally.

`AuditMaintenance` is the only code that calls
`SELECT set_config('polvorapp.audit_maintenance', @mode, true)`. The setting is transaction-local
and its two modes are `purge` and `redact`.

`AuditTrailRulesTests` allowlists exactly the file `Modules/AuditPrivacy/.../Maintenance/AuditMaintenance.cs`.
A new rule forbids the string `polvorapp.audit_maintenance` anywhere else in `src/`.
`AppendOnlyAuditGuard` stays; it is the friendly error for tracked entities.

This is a guard against mistakes, not a security boundary: any SQL on the same role could set the
variable. The real boundary is a runtime role without `UPDATE` and `DELETE` on the table plus a
separate maintenance role, which is a go-live item (see Risks).

**Alternatives**:
- A separate low-privilege role now. Rejected for the MVP: it needs a second connection string,
  and changes to compose and CI, for a single-maintainer deployment.
- No trigger. Rejected: the trigger is what the identity design deferred to #15.

### D5. Personal-data participants

```csharp
public interface IPersonalDataParticipant
{
    string Category { get; }  // e.g. "registry", "orders", "distribution", "identity", "catalog", "notifications"
    Task<PersonalDataSummary> DescribeAsync(PersonalDataSubject subject, CancellationToken ct);
    Task<PersonalDataExportPart> ExportAsync(PersonalDataSubject subject, CancellationToken ct);
    Task<PersonalDataErasurePart> EraseAsync(PersonalDataSubject subject, DbTransaction tx, CancellationToken ct);
}
```

- `PersonalDataSubject` is either `Person(NationalId)` or `UserAccount(Guid)`. A participant that
  holds nothing for a kind of subject returns an empty part.
- `PersonalDataExportPart` holds sheets, each with a code, translated column keys and rows of cell
  values, plus files (name, content type, bytes).
- `PersonalDataErasurePart` holds counts by code, plus after-commit object keys to delete.

Participants and their duties:

| Participant | Person (DNI/NIE) | User |
|---|---|---|
| Registry | Record, weapons, photos (summary and export). Erase: delete through the deletion path (D6) | — |
| Orders | Entries matched by copy `national_id` or by the registered arquebusier's id, and loans with that `lender_national_id`. Erase: anonymise (D7) | — |
| Distribution | Proxies in which their entries take part (role only). Erase: remove them | — |
| Identity | — | Profile. Erase: anonymise (D8) |
| Catalog | — | Assignments. Erase: remove them |
| Notifications | — | Opt-outs and deliveries. Erase: remove them |
| AuditPrivacy | — | Activity (export). Erase: redact (D8) |

Participants run in a fixed registration order: registry, orders, distribution, identity,
catalog, notifications, audit. Each runs on the orchestrator's `DbTransaction`, as the
arquebusier deletion participants do (`UseTransaction` on its own context, or raw Npgsql).

A test lists every table of every module's model with a column classified as personal (a
`[PersonalData]`-style annotation in the EF model, `HasAnnotation("PolvorApp:PersonalData", true)`).
It fails when a module with such a table registers no participant. This is the "forgotten table"
guard.

**Alternative**: AuditPrivacy queries every schema directly. Rejected by ADR-0001 and
`ModuleDependencyRules`.

### D6. Erasing a person: the orchestration

`PersonalDataErasure.ErasePersonAsync(nationalId, reference)` runs these steps:

1. Parse the DNI/NIE (BR-01), or return `400`.
2. Open a transaction on `AuditDbContext`'s connection at `READ COMMITTED`.
3. Lock first, in the same order as the arquebusier deletion so the two cannot deadlock:
   - the registry participant locks the arquebusier `FOR UPDATE` and returns its id and owned
     weapon ids;
   - before deleting, the orders participant collects the ids of the person's entries **by
     arquebusier id and by copy `national_id`**, after the edition in progress `FOR SHARE` and
     with their orders locked `FOR UPDATE` by id. A DNI corrected in the
     registry leaves the old DNI in older copies, so the arquebusier id is what finds them.
4. Run the registry erase, which reuses the deletion core. `ArquebusierAdministration.DeleteAsync`
   is split into `DeleteCoreAsync(arquebusier, transaction)`, called both by the endpoint and by the
   participant. The core includes the existing `IArquebusierDeletionParticipant` calls, so the
   current-edition entry is removed while the orders are open, exactly as BR-14 says. It records
   `ArquebusierDeleted` with `data.source = "gdprErasure"`.
5. The orders participant anonymises the collected entries that still exist and the matching loans
   (D7).
6. The distribution participant deletes the proxies of the erased entries.
7. If every part is empty, roll back and return `404`.
8. Record `PersonalDataErased` (`EntityType = "PersonalDataRequest"`, `EntityId = null`) with
   `{subject: "person", reference, counts: {...}}` through `IAuditTrail`, in the same transaction.
9. Commit, then delete the after-commit photo keys. A failure there is logged by key only and left
   to the sweeper.

A lock timeout or deadlock becomes `privacy.busy` (`503`, retryable), like `registry.busy`.

### D7. Anonymised entries and loans

The orders migration adds:
- `erased_at timestamptz NULL` to `orders.edition_entries` and `orders.weapon_loans`;
- a check: `erased_at IS NULL OR (arquebusier_id IS NULL AND first_name IS NULL AND last_name IS NULL AND national_id IS NULL AND federation_id IS NULL AND owned_weapon_number IS NULL AND owned_weapon_guide_number IS NULL)`, and the equivalent for loans (with `lender_owned_weapon_id`, `weapon_number` and `ownership_guide_number`);
- partial indexes `ix_edition_entries_national_id` and `ix_weapon_loans_lender_national_id`, both
  `WHERE ... IS NOT NULL`.

Anonymising sets the copy columns to `NULL` and `erased_at = now`.
- An entry keeps `owned_weapon_model_id`, so the model is still counted.
- A loan keeps `weapon_model_id`, `lender_kind` and `lender_comparsa_id`. It loses
  `lender_owned_weapon_id`, through the existing `SET NULL` when the registry deletes the weapon.

Effects elsewhere:
- **Reads**: the order DTO gains `erased: boolean`. Entries with `erased_at` render as "Erased
  person".
- **Writes**:
  - `EntryInput` and the loan paths refuse an erased entry with `409 orders.entryErased`;
  - changing the weapon source of a borrower whose loan is erased deletes that loan, as any
    source change does;
  - the lender lookup and pre-fill already skip people who are not in the registry, and erased
    entries have no arquebusier, so they need no new rule.
- **Totals and billing** read quantities, not identities, so they are unchanged.
- **Exports and distribution**: `ExportRows` and the distribution list query filter
  `erased_at IS NULL` for per-person rows, and keep the total queries unfiltered. `ProxyRules`
  adds an `entryErased` check before `notInOrder`. Because that check reads before the write's
  transaction, the proxy write also locks the two entries `FOR SHARE` after the comparsa's proxy
  lock (`IEditionEntries.AnyErasedForWriteAsync`): an erasure committed meanwhile is refused with
  the same `entryErased`, and a later one waits, then deletes the new proxy with the others.

### D8. Erasing a user

The identity participant:
1. Locks the user.
2. Refuses with `409` in two cases: the target is the caller (`privacy.selfErasure`), or the user
   is already erased (`privacy.alreadyErased`).
3. Sets:
   - `Name` to the fixed marker `"—"` (the UI shows the translated "Erased user" whenever
     `erasedAt` is set);
   - `Email` and `UserName` to `erased-{id:N}@erased.invalid` (unique and never deliverable),
     with their normalised forms;
   - `Active = false` and `ErasedAt = now`;
   - `PasswordHash = null` and `TwoFactorEnabled = false`;
   - a new security stamp, which ends sessions and remembered devices.
4. Deletes the user's tokens, claims and logins.
5. Returns the former normalised email and name for redaction. They stay in memory only and are
   never logged or returned.

`ErasedAt` is a new nullable column on `identity.users`. The status becomes `ERASED` when it is
set, before the `DEACTIVATED` check. Reactivating, editing, resending invitations and resetting
two-factor authentication refuse an erased user with `409 identity.userErased`.

The catalog participant deletes the assignments. The notifications participant deletes the
opt-outs and deliveries.

The audit participant redacts through `AuditMaintenance` in `redact` mode:
- `SignInFailed` entries whose `data->>'attemptedEmail'` equals the former normalised email get
  `jsonb_set(data, '{attemptedEmail}', '"[erased]"')`;
- no other entry holds a name or email after the D9 clean-up. A regression test writes every user
  action and asserts that no name or email appears in its `data`.

Entries keep `actor_user_id` and `entity_id`. The viewer shows the actor as "Erased user".

### D9. Fewer personal values in the audit

From now on, `UserUpdated` records `{changedFields: ["name"], previous: {role, locale}, current:
{role, locale}}`.

The audit migration scrubs the existing rows before it creates the trigger:
`UPDATE audit.audit_entries SET data = (data #- '{previous,name}') #- '{current,name}' WHERE action = 'UserUpdated'`.
The order matters: the trigger does not exist yet, and migrations are excluded from the
architecture rule.

### D10. Audit query and keyset paging

`GET /api/audit-entries` takes these query parameters, all optional:
`from`, `to` (dates), `actorUserId` (a GUID, or the literal `none`), `comparsaId`, `entityType`,
`entityId` (only with `entityType`), `action`, `cursor` and `limit` (1–100, default 50).

It returns
`{ items: AuditEntryResponse[], nextCursor: string | null }`. The ordering is
`occurred_at DESC, id DESC`, and the cursor is the base64url of `(occurred_at ticks, id)` taken from
the last item. The next page uses
`WHERE occurred_at <= @t AND (occurred_at, id) < (@t, @id)`: the plain bound lets every filtered index
use the range, and the row comparison breaks ties. The migration adds `(occurred_at, id)`,
`(action, occurred_at, id)`, `(entity_type, entity_id, occurred_at, id)`, and `id` to the filtered actor
and comparsa indexes, all ascending (PostgreSQL scans them backwards for the newest-first order), and
drops the indexes they replace. An entry committed late with an older time than a page's cursor is not
on a later page; that is inherent to keyset paging and harmless here (entries are written in the
transaction of their change).

Dates are interpreted as whole days in Europe/Madrid: `from` 00:00 inclusive to the day after `to`,
exclusive. The conversion uses the shared time zone helper.

`AuditEntryResponse` returns:
- `id`, `occurredAt`, `action`, `entityType`, `entityId`, `comparsaId`, `traceId`, `data` (raw
  JSON);
- `actor: { id, name, erased } | null`;
- `comparsaName`;
- `recordExists: boolean`.

The page's actors and comparsas are resolved in one batch each through `IUserDirectory`, which
gains `GetManyAsync(ids)` returning `ErasedAt`, and through `ICatalogDirectory`.

`recordExists` is resolved for the linkable types only, in one batch per type, through a small
`IAuditRecordResolver` that each owning module registers. A record type without a resolver
returns `false`, and the UI then shows no link.

`GET /api/audit-entries/actions` returns the catalogue: `{code, entityType}[]`, sorted.

Both endpoints sit in the Admin group. The OpenAPI test still holds, because both are GET.

### D11. GDPR endpoints

All of them are in an Admin group under `/api/privacy`, rate limited by a new `privacy` policy
(10 requests per minute per user). The DNI/NIE travels in request bodies only.

| Operation | Route | Response |
|---|---|---|
| `LookUpPerson` | `POST /people/lookup` `{nationalId, reference?}` | `PersonSummary` (registry, entries by edition with `editionStatus`, `orderStatus`, `ordersOpen` and `erased`, loans as lender, `found`) and `warnings[]` |
| `ExportPersonData` | `POST /people/export` `{nationalId, reference}` | ZIP, `application/zip`, `Content-Disposition` with a neutral name `polvorapp-personal-data-{yyyyMMdd}.zip`. The name never holds the DNI/NIE. |
| `ErasePersonData` | `POST /people/erasure` `{nationalId, reference}` | `{counts}` |
| `ExportUserData` | `POST /users/{id}/export` `{reference}` | ZIP |
| `EraseUserData` | `POST /users/{id}/erasure` `{reference}` | `{counts}` |

Auditing:
- the lookup records `PersonLookedUp` with `{found, reference?}`;
- an export records `PersonalDataExported` with `{subject, reference, sheets: {code: rows}, files}`
  through `IAuditLog` before the ZIP is sent. A failure gives `503 privacy.auditUnavailable`;
- an erasure records `PersonalDataErased` in its transaction.

Problem codes:
- `privacy.notFound` (404);
- `privacy.selfErasure`, `privacy.alreadyErased` and `privacy.lastAdmin` (409);
- `privacy.busy`, `privacy.auditUnavailable` and `privacy.storageUnavailable` (503);
- field errors on `nationalId` (`required`, `invalid`, `checkLetter`);
- field errors on `reference` (`required`, `invalid`, `tooLong`). A reference is invalid when it
  holds an email or a valid DNI/NIE, however it is spaced.

The ZIP is built in memory with `System.IO.Compression`. It holds `personal-data.xlsx` and
`photos/{kind}.jpg`, and is at most a few MB (three photos of at most a few hundred KB each). The
workbook reuses the Exports module's cell neutralisation through `Exports.Contracts`. That needs a
new `IDocumentRenderer.RenderWorkbook(sheets)` overload, because the existing `RenderTable` writes a
single sheet. "About this data" texts come from the `privacy` resources in the Admin's culture.

### D12. Frontend

- **Feature folder** `src/features/audit-privacy/`:
  - `AuditLogPage`:
    - follows the List template with a `FilterBar`: `DateInput` from and to, and `FilterSelect`s
      for user, comparsa, area and action;
    - keeps the filters in the address (`search-filters.ts`);
    - uses `useInfiniteQuery` over the generated `listAuditEntries`;
    - shows the rows in a `DataTable` with `paginated={false}`, plus a "Show more" `Button`;
    - opens an `EditSheet` in read mode (`AuditEntrySheet`) with a `DescriptionList` of the
      entry and its data fields.
  - `PrivacyRequestsPage`:
    - follows the Settings template: a `SectionCard` holding the lookup form, then the result
      `SectionCard`s (`DescriptionList`, a `DataTable` of entries by edition, `StatusBadge`s);
    - offers "Download data" and "Erase data". Each opens a `ConfirmDialog` with the reference
      `TextInput` and its help. Erasure lists the warnings and repeats the verb "Erase".
  - `UserPrivacyActions`: the two entries under the user detail page's "More actions".
- **Users**:
  - the `ERASED` status maps in `StatusBadge` to the neutral "inactive" tone;
  - the "Erased user" name comes from `identity:users.erased`;
  - other actions are hidden for an erased user.
- **Other pages**:
  - "View history" is a `Link`-styled action in the `RecordHeader` of the arquebusier and user
    detail pages, for Admins, to `/audit-log?entityType=Arquebusier&entityId=…`;
  - the orders page shows erased entries with `orders:entry.erased` and without edit actions;
  - the distribution page's proxy pickers skip erased entries.
- **Navigation**: `audit-log` (icon `ScrollText`) and `privacy` (icon `ShieldCheck`), `roles:
  ['ADMIN']`, under the Admin entries.
- **Routes**: both pages sit under `RequireAdmin`, with breadcrumbs `nav.auditLog` and
  `nav.privacy`.
- **Downloads**: `src/api/http.ts` gains `apiDownloadPost(url, body)`, the POST variant of
  `apiDownload`, so the DNI/NIE stays out of URLs.
- **i18n**: new namespaces `audit` and `privacy` in `es-ES`, `ca-ES-valencia` and `en`, registered
  in `src/i18n/index.ts` and `i18next.d.ts`.
  - `audit`:
    - `page.*`, `filters.*`, `columns.*` and `record.*`;
    - `actions.<Code>` for every catalogue code;
    - `entityTypes.<Type>`;
    - `actor.system`, `actor.anonymous` and `actor.erased`;
    - `details.*`, `viewHistory` and `errors.*`.
  - `privacy`:
    - `page.*`, `lookup.*` and `summary.*`;
    - `reference.label` and `reference.help`;
    - `export.*` and `erase.*`, including `erase.warnings.*` and the outcome `erase.counts.*`
      (the workbook's "About this data" texts live in the backend resources);
    - `errors.privacy.*` and `fields.*`, like the other namespaces' problem texts.
  - `common`: `nav.auditLog` and `nav.privacy`.
  - `ui`: `status.user.ERASED`, where the other status labels are.
  - `identity`: `users.erased` and `errors.users.erased`.
  - `orders`: `entry.erased`, `loan.lenderErased`, `lentOut.borrowerErased` and
    `errors.orders.entryErased`.
  - `distribution`: `fields.entryErased`, where the other proxy field reasons are.

### D13. Security and GDPR

- **Admin only**: every route is in the Admin group, and a FiringChief gets `403`. BR-12 needs no
  scope check, because Admins see everything.
- **DNI/NIE handling (NFR-12)**:
  - only in request bodies;
  - never logged; the request logging middleware already logs neither bodies nor queries;
  - never audited or used as a file name;
  - the lookup is rate limited and audited like the lender lookup (SEC-03).
- **Data minimisation in exports (SEC-06)**: the other party of a loan or a proxy appears only by
  role. The ZIP is never stored and is sent with `no-store`.
- **Erasure**:
  - atomic and audited in the same transaction;
  - the photo images go after the commit, with the sweeper as fallback (SEC-08);
  - storage versioning must stay off, an existing SEC-02 go-live check.
- **Audit retention (SEC-05)**: 1 year and 5 years. The privacy notice (Q-50) must state it.
- **Accountability**: the request reference identifies the written request without identifying
  the person.

### D14. Tests

- **Database**:
  - the trigger refuses `UPDATE`, `DELETE` and `TRUNCATE`;
  - the `purge` mode deletes and cannot update;
  - the `redact` mode updates `data` only and cannot change another column or delete;
  - the D9 migration scrub;
  - the D7 check constraints and indexes.
- **Unit**:
  - cursor encoding and filter validation;
  - retention cut-offs at the edges (365/366 days, 5 years ± 1 day);
  - the workbook builder (sheets, neutralisation);
  - `AuditActionCatalog` retention classes.
- **Integration**: per endpoint, with Testcontainers PostgreSQL and MinIO:
  - Admin and FiringChief;
  - each `400`, `404`, `409`, `429` and `503`;
  - audit entries free of DNI/NIE and names;
  - erasure atomicity, with a participant forced to fail;
  - the race between an erasure and an arquebusier deletion;
  - photos erased after the commit;
  - totals unchanged;
  - exports and distribution leaving erased entries out.
- **Architecture**:
  - the allowlisted maintenance file and the `audit_maintenance` string rule;
  - every recorded action is in the catalogue;
  - every personal-data table has a participant.
- **Frontend**: Vitest with MSW for both pages, the user actions, the erased entry row and
  `apiDownloadPost`, with axe assertions.
- **E2E** (Playwright, `serial-state/privacy.spec.ts`):
  - an Admin filters the audit log and opens an entry;
  - an Admin looks up a synthetic DNI, downloads the ZIP and erases the person, and the lookup
    then finds nothing;
  - a FiringChief gets "not allowed" on both pages.

## Risks / Trade-offs

- **The session variable is not a security boundary** → Documented in D4. `docs/compliance.md`
  gets a go-live item: run the API with a role without `UPDATE`/`DELETE`/`TRUNCATE` on
  `audit.audit_entries`, and give the purge and redaction a maintenance role.
- **Erasure is irreversible, and the warning is the only safeguard** (maintainer decision: never
  blocked) → The confirmation lists every non-closed edition affected and needs a reference. The
  daily encrypted backups (SEC-07) still hold the data until they rotate. The privacy notice must
  say so.
- **An erasure during an edition in progress removes the person from the lists owed to the Arms
  Authority** → An accepted consequence of the decision. The warning names the editions and their
  lists.
- **Old DNIs in copies after a registry correction** → Handled: entries are collected through the
  arquebusier id before deletion. A former arquebusier whose DNI was corrected and who has since
  been deleted is found only by each DNI they had. The lookup by the old DNI finds them.
- **Catalogue drift** (a module records a code missing from the catalogue) → The architecture test
  fails, and unknown codes default to the 5-year class.
- **Audit `data` grows new personal fields in future changes** → The D8 regression test, plus the
  `audit-privacy` participant checklist in the modules README.
- **A participant forgets a table** → The `PersonalData` annotation test in D5.
- **Large activity sheet for a long-lived Admin** → At about 60 users and 5 years the row count stays
  far below Excel's limits. The sheet is capped at 100,000 rows, with a note in "About this data".

## Migration Plan

1. Migrations, in module migration order:
   - `audit` (order 0): the composite index, the `UserUpdated` scrub, then the trigger;
   - `identity`: `erased_at`;
   - `orders`: `erased_at`, the checks and the indexes.

   All are additive, apart from the scrub of existing name values.
2. Deploy. The purge first runs one minute after start-up. Before go-live it removes nothing,
   because no entry is old enough.
3. Rollback:
   - the migrations' `Down` drops the trigger, columns and indexes;
   - scrubbed names cannot be restored, which is intended;
   - erased data cannot be restored, except from backups (SEC-07).

## Research notes (task 1.1)

Versions in use: PostgreSQL 18.6, Npgsql and Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, EF Core
10.0.12, ClosedXML 0.105.1, `@tanstack/react-query` 5.104.0 and Orval 8.38.0.

- **`set_config(name, value, true)`** applies only until the end of the current transaction
  (PostgreSQL 18 docs, §9.28.1). **`current_setting(name, true)`** returns `NULL` for a custom
  setting that was never set, instead of raising an error. So the trigger needs no prior
  `ALTER DATABASE ... SET`, and a pooled connection never keeps the mode after its transaction.
  `AuditMaintenance` therefore always calls `set_config` inside an explicit transaction. Outside
  one, `is_local` would last only for the implicit single-statement transaction.
- **`BEFORE TRUNCATE ... FOR EACH STATEMENT`** triggers are used in the wild exactly for audit
  tables (`gh search code`: `prevent_audit_mutation`, `audit_events_no_truncate`). Row triggers
  cannot fire on `TRUNCATE`, so the design's separate statement trigger is required.
- **Keyset paging**: Npgsql translates `EF.Functions.LessThan((a, b), (x, y))` into a PostgreSQL
  row-value comparison `(a, b) < (x, y)`, which the composite index `(occurred_at DESC, id DESC)`
  serves. It is widely used for keyset feeds (`gh search code`), so no raw SQL is needed in
  `AuditQuery`.
- **Batched purge**: EF's `ExecuteDelete` cannot be batched with other statements and returns no
  rows. The purge needs `set_config` in the same transaction anyway, so `AuditMaintenance` uses
  raw SQL:
  `DELETE FROM audit.audit_entries WHERE id IN (SELECT id ... ORDER BY occurred_at LIMIT 5000)`,
  one transaction per batch.
- **TanStack Query v5 `useInfiniteQuery`** needs `initialPageParam`, and `getNextPageParam`
  returns `undefined` or `null` to stop. `hasNextPage` and `fetchNextPage` drive "Show more".
- **Orval 8**: a per-operation override
  `override.operations.ListAuditEntries.query = { useInfinite: true, useInfiniteQueryParam: 'cursor' }`
  generates `useListAuditEntriesInfinite`, and leaves every other operation unchanged.
- ClosedXML's `XLWorkbook.Worksheets.Add(name)` for several sheets (sheet names at most 31
  characters, without `[]:*?/\`), and `System.IO.Compression.ZipArchive` over a `MemoryStream`
  with `leaveOpen: true`, are standard APIs and need no workaround.

## Open Questions

- The exact wording of "About this data" (controller contact, DPO) depends on Q-50. The
  provisional texts use placeholders that point to the privacy notice, and are replaced at go-live
  without a spec change.

## Review notes (groups 2–3)

- **Group 2** (`csharp-reviewer`, `database-reviewer`, `security-reviewer`): no CRITICAL. Fixed:
  - the HIGH finding: an `(action, occurred_at, id)` index for the purge and the action filter;
  - the purge batch takes no order, uses `FOR UPDATE SKIP LOCKED`, and runs two fixed statements;
  - action codes are constants in each module's `*AuditActions` class, which the call sites use, so
    the catalogue is their single source;
  - `AuditTrail` also checks the entity type;
  - `id` added to the filtered actor and comparsa indexes;
  - the triggers are `ENABLE ALWAYS`, compare every column but `data` through `to_jsonb`, and raise
    SQLSTATE `PA001`;
  - the maintenance mode is reset after each statement;
  - retention settings are bounded (5–100 years, 365–36500 days);
  - each period is purged independently, and what was deleted is recorded or logged with its counts;
  - the catalogue is built at start-up;
  - the architecture rules also catch `UPDATE ONLY`, `MERGE`, trigger tampering and replication
    mode, and exempt only the audit module's migrations;
  - the compliance text now states that a low-privilege role needs a maintenance connection that is
    not built yet.

  Kept, with reasons:
  - the test-only `AuditTrail` constructor without a catalogue: it is internal and only tests reach
    it;
  - no test of a purge phase that fails half-way: injecting the failure would need a seam the code
    does not otherwise want;
  - the failed sign-in email is kept as is for a year: spec "Security events are audited", and the
    privacy notice must say so.
- **Group 3** (`csharp-reviewer`, `database-reviewer` with `EXPLAIN ANALYZE` on 1,000,000 synthetic
  rows, `type-design-analyzer`). Every filter and cursor combination tested ran in under 10 ms, except a
  long entity history with a cursor, which took 77 ms. Fixed:
  - the HIGH finding: `to=9999-12-31` gave a 500. Days are now limited to the years 2000–2999;
  - the entity index is now `(entity_type, entity_id, occurred_at, id)`, which brought that case to
    0.1 ms;
  - query parameters are camelCase in the contract;
  - the `JsonDocument` is no longer leaked;
  - `AuditFilter` can no longer hold a user together with "no user", or a record id without its
    type. It is built only through `Parse` or `Create`, which guard the period and the page size;
  - empty filter values count as absent, and GUID record ids are read in their stored form;
  - declared action codes are validated, and resolvers must name declared, distinct types (checked at
    start-up);
  - the purge takes the retention class instead of a boolean.

  Kept, with reasons:
  - a partial index for entries without a user: they took 2–4 ms;
  - a composite comparsa+action index: it took 8.7 ms, and that combination is rare.
- **Group 4** (`csharp-reviewer`, `security-reviewer`, `silent-failure-hunter`): no CRITICAL findings.
  Fixed:
  - the two HIGH findings:
    - an export notes and logs a photo whose stored file is missing instead of omitting it;
    - after the commit, the erasure deletes stored files within a 10 s budget, catches every error,
      logs the exception and returns how many files are left to the sweep;
  - the erasure runs in a service scope of its own, so no context stays enlisted in a finished
    transaction;
  - every participant enlists in its erase step too;
  - the state shared between the steps lives in `PersonalDataErasure`: `LoanIds`, and `UserFound`
    instead of `FormerEmail` as the "user found" flag;
  - the orders participant:
    - finds a registered lender's loans through their weapons in the lookup and the export too;
    - selects again under the order locks before anonymising;
    - treats a missing edition as an integrity error;
  - the registry participant checks the DNI/NIE again after its lock;
  - erasing the last active Admin is refused (`privacy.lastAdmin`), under the same advisory lock as
    other Admin changes;
  - an erased user cannot be assigned again, and loses `last_sign_in_at`;
  - the user `UPDATE` must touch one row;
  - the activity export keeps the newest 100,000 rows and notes when it is capped;
  - the purge logs each failure with its exception, and always records (or logs) what it deleted,
    even when stopping;
  - records that hold personal values override `ToString`;
  - the coverage test lists every personal table with the participant that handles it.

  For group 5:
  - map lock timeouts and deadlocks (`55P03`, `40P01`) to `privacy.busy`, and
    `PersonalDataErasureRefusedException` to 409;
  - refuse a reference that looks like a DNI/NIE or an email (`400 reference`);
  - answer `404` for the export of an erased user;
  - groups 5 and 6 land in the same pull request, so no erasure is reachable before erased entries
    are read-only and left out of documents.

  Accepted:
  - a pickup proxy or a notification delivery created concurrently with an erasure can outlive it as
    an id-only row;
  - a failed sign-in typed later with the erased address records it again for a year, which the
    privacy notice states.

  Follow-up, not built: fail start-up outside local environments when the connection string sets
  `Include Error Detail`, so a constraint violation cannot log row values.
- **Group 5** (`csharp-reviewer`, `security-reviewer`, `silent-failure-hunter`): no CRITICAL findings.
  Fixed:
  - the two HIGH findings:
    - a failed audit is now logged by action, with no reference or person;
    - a missing `PrivacyTexts` key fails the export instead of printing its key. A test builds every
      sheet, column and note in the three languages, and checks that sheet names are valid and
      unique;
  - bodies are limited to 4 KB;
  - an export or erasure that finds nobody is audited as a `PersonLookedUp` with `found: false`, so
    exports cannot probe unaudited;
  - the reference refuses any valid DNI/NIE however it is spaced, and any email; references such as
    `REQ-2030-0000123-A` are accepted;
  - the lookup has its own request type with an optional reference;
  - the export audit records its notes;
  - a photo read that fails mid-stream becomes `503 privacy.storageUnavailable`;
  - busy and storage failures are logged;
  - the participation column is translated by column, not by value;
  - sheet names are cleaned and made unique;
  - ZIP entries are stored uncompressed, with the export's time;
  - the proxies count key is a contract constant;
  - a comparsa missing from the catalogue is logged and shown as unknown.

  Kept, with reasons:
  - an erasure whose audit cannot be saved answers 500: it is rolled back, as the spec requires;
  - the `nationalId` errors keep `checkLetter`, and the reference keeps `tooLong`, for clearer UI
    messages;
  - the rate limit stays a fixed window: enumerating DNI/NIEs at 10 per minute is not feasible;
  - the busy and storage-unavailable paths have no endpoint test: forcing a lock timeout or a
    storage failure mid-stream would need fault injection the host does not have; both paths are
    small and logged.
- **Group 6** (`csharp-reviewer`, `database-reviewer`): no CRITICAL findings. Fixed:
  - an erased entry has no issues, so it never blocks submitting or validating its order (a rental
    model no longer offered, for example);
  - the lent-out list says `borrowerErased`, so the lender's comparsa sees a weapon lent to an
    erased person without a name;
  - proxy candidates leave erased entries out, and a proxy erased after it was authorised counts as
    missing in the distribution lists;
  - the comparsa list leaves out "lent by" for an erased lender and keeps the weapon;
  - the orders erasure adds the entries and loans found again under the lock to the erasure, so the
    distribution participant removes their proxies too;
  - the proxy write locks both entries `FOR SHARE` after the comparsa's lock (D7), closing the race
    between a registration checked before an erasure and written after it;
  - the lock-order comments and D6/D7 now say which rows are locked and how.

  Kept, with reasons: the `UPDATE ... RETURNING` counts are what this erasure changed, so a second
  erasure of the same person counts nothing, which is the intended audit. The `FOR SHARE` race
  itself has no concurrent test; the lock read is tested on its own.
- **Catalogue in the contract** (task 7.1): instead of a separate generated `actions.json` (D2),
  an OpenAPI document transformer makes `AuditActionResponse.code` and `entityType` enums of the
  declared catalogue. The generated client lists them, the frontend label test reads them, and the
  CI contract drift check fails when a module adds a code without regenerating. Recorded entries
  keep plain strings, because they may hold codes no longer declared.
- **Group 7** (`react-reviewer`, `typescript-reviewer`, `a11y-architect`): no CRITICAL findings.
  Fixed:
  - a failed "Show more" keeps the loaded entries and the button, with the error above it; only a
    failed first page replaces the table;
  - `ConfirmDialog` forgets an earlier failure when it closes, so a controlled dialog reopened from
    "More actions" starts clean (a shared composite fix, with its test);
  - focus: "Show more" moves it to the first new entry, removing the record filter to the filters,
    "New lookup" to the DNI/NIE field, and a lookup to its result (the summary section, or the
    "nothing held" banner);
  - record links are underlined at rest, since the same column also holds plain text;
  - an emptied "From" stays in the address as `from=` and means no lower bound; an inverted period
    is said at "To" (`FilterDate` gained `error`) and nothing is asked; the date fields' clear
    buttons are named after the field (`DateInput.clearSubject`);
  - the user filter names erased users "Erased user"; a value from the address that a list does not
    have yet stays shown; a failed filter list says so; option lists sort with the UI language;
  - editing the DNI/NIE after a lookup hides its result, so no action can apply to another person;
    clearing also resets the lookup and erasure requests, and an erasure drops the audit log pages
    read so far (they may hold data that is now redacted);
  - `PrivacyRequestDialog` returns the request's result to `onDone`, mutations use
    `responseData`, `apiDownload` shares `refuse()`, and `apiDownloadPost` has a test of the
    anti-forgery refresh and retry;
  - `DetailSheet`'s body is a named, focusable scroll region, with one Close; the details trigger
    names the action and the time; the entry's details are worked out only while open;
  - unused texts removed; the summary has `h3`s; row and list keys cannot collide.

  Kept, with reasons:
  - recorded data field names stay as recorded (`firstName`, `changed`): they are the audit's own
    vocabulary, shown in the monospaced face, and translating them would hide what was stored;
  - Enter in the reference field only checks it: confirming stays an explicit button press, the
    same for both dialogs;
  - the reference is typed again for each request: each one is audited on its own, and keeping it
    would carry a reference over to an unrelated request;
  - the record filter banner names the record type, not the record: the id means nothing to a
    reader and the record's name may be erased;
  - a half-typed date is not sent: the field keeps the last complete date in the address.
- **Group 8** (`doc-updater`): the glossary named classes that do not exist; the code terms are now
  `PersonalDataRequests` (and the audit entity type `PersonalDataRequest`) and `PersonalDataPackage`,
  and erased entries and erased users have separate rows.
- **Verification (group 9)**:
  - build, types, lint, format, i18n and the OpenAPI contract are clean; `openspec validate --strict`
    passes;
  - coverage: `AuditPrivacy` 91.2 % and the modules' `Privacy/` participants 95.3 % of lines;
    `features/audit-privacy` 96.1 % (frontend total 95.5 %);
  - security grep: the DNI/NIE travels only in POST bodies (never a route or query), nothing logs
    it, no code outside `AuditMaintenance` changes audit rows, and both endpoint groups require the
    Admin policy; a test now captures every log line of lookups, exports and erasures and finds no
    DNI/NIE, reference or name;
  - `e2e/serial-state/privacy.spec.ts` (5 tests) passes on its own and inside a full run.
  - final suites: backend 2340 of 2341 (`PhotoAuditTests` failed once under load, as in earlier
    runs), architecture 37 of 37, frontend 2319 of 2319 (two order and registry page tests timed
    out once while the backend suite ran, and pass on their own).
  - Review findings fixed (`e2e-runner`, `pr-test-analyzer`):
    - a reference with a DNI/NIE split by `/`, `_` or `,` was accepted: every mark that is not an
      ASCII letter or digit is now ignored when looking for one;
    - an erased user's placeholder email (`erased-…@erased.invalid`) showed on the users list and
      page: it is now shown as not given;
    - new tests: an erasure (person and user) whose audit cannot be recorded changes nothing; two
      Admins erasing each other at once leave one; two erasures of one person at once erase once;
      an erased entry is left out of a real comparsa list but counted in its totals and its audit
      rows; every action constant is in the catalogue (reflection); the `listsChange` warning; a
      session opened before a user's erasure ends with it;
    - the E2E opens an arquebusier's history through "View history", proves the action filter
      on a log that holds other actions, cancels an erasure, checks the erasure's audit entry
      (reference in, DNI/NIE and name out), that no request URL carried the DNI/NIE, the
      navigation entries, a user export, and the erased user's "View history"; it cleans up what it
      registered even when it fails, and uses its own DNI and federation id range.
  - Follow-ups, not done here (each is a test of behaviour already implemented):
    - an erasure blocked by a held row lock answering `503 privacy.busy`, and an erasure racing an
      arquebusier deletion;
    - the 4 KB request size limit (`413`), and the privacy rate limit shared by export and
      erasure and kept per user;
    - the user export's audit entry and activity sheet in detail; redaction negative controls;
    - the after-commit photo deletion failing (`filesPending > 0`, the sweeper);
    - erased entries in the orders dashboard and billing, the lender lookup and the pre-fill;
      a holder-role proxy removal; borrower names absent from an external owner's export;
    - retention edge cases (every security code, leap years, two purges at once);
    - frontend messages for `429`, `privacy.notFound` after a stale lookup and `alreadyErased`.
  - Outside the change, seen in the full E2E runs on this Windows machine (none touches code of
    this change, and each passes on its own except the last):
    - `photos-firefox` "crops with the buttons only" and `dates-firefox` select timed out under
      the full run's load; a failed dependency project skips `serial-state`;
    - `serial-state/notifications.spec.ts` "milestone reminder" failed once on axe `target-size`
      for the edit panel's close button, and on its own cannot run here: it starts `docker` from
      Node, which Windows cannot resolve (`ENOENT`); it runs in CI on Linux.
- **Full suite after group 6** (before its review fixes): 2319 of 2322 passed. `NotificationPreferenceTests`,
  `PhotoAuditTests` and `EntryEditingTests` failed once under load and pass on their own.
- **Full suite after group 4**: 2275 of 2278 passed. `PhotoAuditTests`, `EntryEditingTests` and
  `OrderPreparationTests` failed once under load (review agents were using Docker at the same time)
  and pass on their own.
- **Full suite after group 2**: 2233 of 2235 passed. `NotificationPreferenceTests` and
  `EntryEditingTests` failed once under load and pass on their own (intermittent, like the known
  `OrderPreparationTests`).
