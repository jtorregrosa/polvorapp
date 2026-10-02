# Design

## Context

See `proposal.md` for the motivation and `specs/arquebusier-registry/spec.md` for the behaviour.
The facts below come from the current code and shape the approach.

- **Validation is already a pure function.** `RegistryInput.Read(ArquebusierFields, today,
  statusRequired)` turns raw strings into an `ArquebusierInput` and reports every error at once,
  with the field keys and reasons the UI already translates (`registry:validation.*`).
  `NationalId.Parse` is pure as well. The #5 design reserved this seam for the import. The only
  gap is that spreadsheet cells are not strings: dates, numbers and errors need converting first.
- **Registration runs one row per transaction.** `ArquebusierAdministration.RegisterAsync` checks
  the scope, finds the comparsa through `ICatalogDirectory`, checks for duplicates with a private
  `DuplicateOfAsync`, records `ArquebusierRegistered` through `IAuditTrail` and saves inside
  `RegistryWriteGuard` (`lock_timeout` 5 s; unique-index violations are mapped through
  `RegistryProblems.ViolatedConstraint`).
- **Uploads read the multipart body by hand.** `ImageUploads.ReadFileAsync` reads it in memory
  with explicit `FormOptions` and per-endpoint `RequestSizeLimit`. The anti-forgery header is
  checked by middleware before the body is read. Rate-limit policies live in `RateLimitPolicies`
  and can be configured through `RateLimits:<Name>:PermitLimit`.
- **The request culture comes from `Accept-Language`.** The SPA's `apiFetch` sets that header from
  the UI language. A plain link would send the browser's language instead.
- **ClosedXML is chosen but not referenced yet** (ADR-0008, version 0.105.x on NuGet). No xlsx
  library exists in the frontend.
- **A guard test covers collection routes.** `RegistryScopeGuardTests` fails for any new route
  under `/arquebusiers` that is not classified, and its comment anticipates "an import".
- **Warnings have one owner.** `IComplianceRules.Evaluate(ComplianceFacts, referenceDate)` derives
  them; `ComplianceFacts` holds the birth date, the license, the course date and `HasIdPhoto`.

## Goals / Non-Goals

**Goals:**
- Reuse the registration's validation, duplicate checks, write guard and audit trail, so that a
  row and a form follow the same rules.
- Keep the uploaded workbook and its personal data in memory only: never on disk, in storage, in
  logs or in audit entries.
- Make reading a hostile `.xlsx` safe: size, zip-bomb, row and cell limits before ClosedXML
  builds its model.

**Non-Goals:**
- A generic import framework for other entities. Future imports (none planned in the MVP) can
  extract one when they exist.
- Streaming or background processing. At most 1000 rows fit comfortably in one request.

## Decisions

### D1. Stateless two-step flow: the confirmation uploads the file again

`POST /api/arquebusiers/import/preview` and `POST /api/arquebusiers/import` take the same
multipart form: `comparsaId` and `file`. Both run the same pipeline:
1. read the workbook;
2. validate the rows;
3. check the duplicates against the registry.

Only the import then writes, in one transaction. The browser keeps the `File` object between the
two calls.

*Alternatives considered*:
- Storing the parsed rows server-side under an import token for a while. This would put personal
  data in a table or cache with its own expiry, cleanup and erasure duties (SEC-08, SEC-09), only
  to save one upload of at most 2 MB. Rejected.
- A single call with a `dryRun` flag. It would mix two authorisation-equal but audit-different
  operations behind one route. Two routes are clearer in OpenAPI and in the scope guard test.

Validating again at the import is required anyway, because the registry may have changed since
the preview (spec: "Registry changed after the check").

### D2. Endpoints, status codes and payloads

All three endpoints are in an Admin-only sub-group,
`MapGroup("/import").RequireAuthorization(AuthorizationPolicies.Admin)`, under the `/arquebusiers`
group, like the comparsa logo endpoints. `RegistryScopeGuardTests` lists them as reviewed
collection routes.

| Route | Success | Failures |
|---|---|---|
| `GET /api/arquebusiers/import/template` | `200`, `.xlsx` file named `polvorapp-arquebusiers-template.xlsx` | `403` |
| `POST /api/arquebusiers/import/preview` | `200` `ArquebusierImportReport` | `400 validation` (`comparsaId`, `file`), `403`, `404 arquebusiers.comparsaNotFound`, `409 arquebusiers.comparsaInactive`, `429` |
| `POST /api/arquebusiers/import` | `200` `ArquebusierImportResult { comparsaId, importedCount }` | as above, plus `400 arquebusierImport.rowErrors` with `report`, `409 arquebusierImport.conflict`, `503 registry.busy` |

**File problems** use the existing `validation` problem with `errors.file = <reason>`. The reasons
are:
- `required`, `tooLarge` and `invalid`, which already exist for photos;
- the new `empty`, `tooManyRows`, `missingColumns` and `duplicateColumns`.

`missingColumns` and `duplicateColumns` add a `columns` extension listing the field keys
(`nationalId`, …). The UI translates them with the template headers.

**`ArquebusierImportReport`** has these fields:
- `rowCount`, `validCount`, `errorRowCount`, `warningRowCount`;
- `ignoredColumns`: the header texts as read;
- `rows`: only the rows with an error or a warning. Each row has `rowNumber`, `lastName`,
  `firstName`, `errors` and `warnings`. Each error is `{ field, reason }`, and `warnings` are
  `ComplianceWarning` codes.

**Error reasons** extend the registration reasons with:
- `duplicateInFile`;
- `taken`, for `nationalId` and `federationId` that are already registered;
- `cellError`, for a cell with an error value.

**Rejected rows.** When rows have errors, `400 arquebusierImport.rowErrors` carries the full
report as the `report` extension, so the page can show it without a new preview.

**Race at commit.** A unique-index violation at commit (a race) rolls everything back and answers
`409 arquebusierImport.conflict`. The UI then asks the Admin to check the file again.

**Responses.** Preview and import responses send `Cache-Control: no-store`.

*Alternative considered*: `422` for row errors. The project answers every blocking validation
with `400` (`ProblemResults.Invalid`), and the UI already maps `400` with `errors`. A distinct code
in the problem keeps that convention.

### D3. Safe workbook reading before ClosedXML

`ImportWorkbookReader` reads the upload, applying these limits in order:
1. **Upload size.** A request limit of 2 MB plus multipart framing is set with
   `RequestSizeLimit`. `SpreadsheetUploads.ReadAsync` in `SharedKernel/Http` shares the in-memory
   form reading of `ImageUploads` (`MultipartForms`). It returns a `SpreadsheetUpload` with the
   file and the text fields, or the reason `file: required | tooLarge`.
2. **Package check** (`WorkbookPackage`). ClosedXML and the Open XML SDK build whole documents in
   memory and recurse over nested elements: a 2 KB part nested a few thousand levels deep
   overflows the stack and kills the API process, which no `catch` can stop (found in the group 3
   security review). So before ClosedXML sees the file, the bytes are opened as a `ZipArchive` and
   every part is read once:
   - more than 200 entries, no `[Content_Types].xml`, a content type other than a plain workbook
     (`xlsm`, `xlsb`, templates) → refused;
   - every part is decompressed through a stream that counts the real bytes, because the sizes in
     zip headers are written by the sender, and fails past 5 MB in total. A 1000-row template holds
     well under 1 MB of XML, and ClosedXML's model is many times larger;
   - every `.xml`, `.rels` and `.vml` part is read with a forward-only `XmlReader`, which never
     recurses: `DtdProcessing.Prohibit` and no resolver (no entity expansion in any encoding),
     nesting deeper than 64 → refused, and more than 5000 `row`, 100000 `c` or 20000 `si`
     elements of the SpreadsheetML namespace → refused, so the row limit is enforced before the
     load.

   Every refusal is the file reason `invalid`, logged at Information with a short code
   (`tooDeep`, `tooManyRows`…), never content.
3. **ClosedXML load.** The workbook is loaded with `new XLWorkbook(stream)`. Formulas are not
   recalculated, and `CachedValue` is read (spec: the value saved in the file); a formula saved
   without its value reads as an error cell. Any exception while loading becomes `invalid` and is
   logged at Warning with the exception types, never the message, which can quote content. Only
   the load is caught: a bug in the row reading surfaces as a `500`.
4. **Sheet and rows.** Only the first worksheet is read. The used range must not exceed
   1001 rows (`tooManyRows`), checked before iterating, and only the 13 known columns are
   converted.
5. **Headers.** Headers are normalised: trimmed, case-folded, accents removed with
   `NormalizationForm.FormD` (combining marks dropped). They are then matched against the header
   texts of the three cultures.

*Alternatives considered*:
- The Open XML SDK SAX reader (`OpenXmlReader`). It streams better, but needs our own shared
  strings, date-style and error handling. ClosedXML is already the ADR choice, and the files are
  small.
- `ExcelDataReader`. It would be a new overlapping library and would need an ADR.

### D4. Cell conversion into `ArquebusierFields`

`ImportCellReader` turns each cell into the raw string that `RegistryInput.Read` expects:

| Cell kind | Conversion |
|---|---|
| Date columns, date cell | `DateOnly` → `yyyy-MM-dd`; before 1900-01-01 → `invalid` |
| Date columns, number cell | `invalid`: a year or an amount must not become a date in 1905 |
| Text columns, number, date or logical cell, or text over 512 characters | `invalid` |
| Date columns, text | `dd/MM/yyyy` or `yyyy-MM-dd` exact, invariant culture; anything else is passed through unchanged, so `RegistryInput` reports `invalid` |
| `federationId`, `phone`, number cell | Whole number → invariant digits; a fraction → `invalid` |
| `nationalId` | Text as is; a number cell → digits only, which fails as `invalid` (no letter, spec scenario); `^\d{1,7}[A-Za-z]$` after normalisation → left-padded to 8 digits |
| `gender`, `status`, license type | Lookup in a table of the codes plus each culture's template labels (case- and accent-insensitive); no match → passed through, so `RegistryInput` reports `invalid` |
| Error cell | `cellError` for that field |
| Blank | `null` |

The license is assembled as the spec describes (none, pending, issued) into `LicenseFields`. A
date without a type adds `license.type: required`, and an expiry without an issue date adds
`license.issuedOn: required`. Validation then calls `RegistryInput.Read(fields, today,
statusRequired: false)` unchanged, so BR-01, BR-03 and every field rule come from one place.

DNI padding stays import-only (the #5 design left it to #8). The form keeps asking for 8 digits,
because there the user can see and fix the value.

### D5. Duplicates in one query, batch registration in one transaction

After reading the rows, these checks run:
- **Duplicates inside the file**: a pass over the valid values, grouping by `nationalId` and by
  `federationId`.
- **Duplicates against the registry**: one query
  `WHERE national_id = ANY(@ids) OR federation_id = ANY(@fids)` that returns only the matching
  values, never the rows' comparsa. It replaces the per-row `DuplicateOfAsync`, which stays for
  the single registration.

`ArquebusierImporter.ImportAsync(comparsaId, content, ct)` then runs inside `RegistryWriteGuard`
and `BeginWriteAsync`:
1. it takes a transaction-scoped advisory lock (`RegistryLocks.LockImportsAsync`), so imports run
   one at a time. Without it, two imports of the same people insert hundreds of rows in key order,
   which differs between requests (UUIDv7 keys of the same millisecond), and can deadlock; with it,
   the second import sees the first one's rows and answers `400` with them as `taken`;
2. it runs the duplicate query again within the transaction;
3. it adds every `Arquebusier` (through the same `ArquebusierAdministration.New` as a manual
   registration) and records the audit entries (D7);
4. it calls `SaveChangesAsync` once and commits.

A unique violation at commit (a manual registration that won the race) maps to `409
arquebusierImport.conflict`. A lock timeout or deadlock, found anywhere in the exception chain
(`RegistryLocks.IsRetryable` now walks the inner exceptions, because the execution strategy wraps
the `DbUpdateException`), maps to `503 registry.busy`. The change tracker is cleared on failure.
The write guard's `lock_timeout` bounds every wait. One thousand inserts in one `SaveChanges` is
well within PostgreSQL and EF batching limits.

*Alternative considered*: calling `RegisterAsync` once per row. That gives N transactions, so the
import is not all-or-nothing, and N comparsa lookups. Rejected by the maintainer decision.

### D6. Warnings in the report

For each valid row, the report builds `ComplianceFacts` with `HasIdPhoto: true`. It evaluates them
with `IComplianceRules` on `FederationCalendar.Today`, and keeps the warnings except
`ID_PHOTO_MISSING` and `LICENSE_PHOTOS_MISSING`. Setting `HasIdPhoto: true` and filtering by code
keeps the rule order of the compliance module. The page shows one fixed note that imported
arquebusiers have no photos. Rows with errors carry no warnings, because their data is not reliable.

### D7. Audit entries

Each arquebusier gets the existing `ArquebusierRegistered` entry, with
`data = { source = "import" }`, so the audit viewer (#15) can tell imports from manual
registrations. One `ArquebusiersImported` entry is added with:
- entity type `Comparsa` and the comparsa id;
- `ComparsaId` set;
- `data = { count }`.

All entries are added to the same `DbContext` before the single `SaveChangesAsync`, so they commit
or roll back with the arquebusiers (SEC-05). There are no names, IDs, dates, file name or row
numbers in the entries. Each entry is small, so the 16 KB audit data cap is never reached.

### D8. Template generation

`ImportTemplateWriter` builds the workbook with ClosedXML on each request, in the request culture.
The workbook has:
- **Sheet 1 (`Arquebusiers` translated)**:
  - bold header row, frozen;
  - text format (`@`) for the DNI/NIE and phone columns;
  - `dd/mm/yyyy` date format for the four date columns;
  - list validations over rows 2–1001 for gender, status and license type. The lists point to a
    hidden sheet `Lists`, holding the translated values and the codes `AE` and `A-PROF`.
- **Sheet 2 (instructions translated)**: one row per column with whether it is required, its
  accepted values and formats, and the limits (2 MB, 1000 rows, `.xlsx` only, one comparsa per
  file).

The texts live in the registry module as one `ImportTemplateTexts` record per language
(`Import/ImportTemplateTexts.cs`), chosen by the request culture. A record rather than resource
files lets the compiler check that no language misses a text, and keeps them in one reviewable
file. A unit test checks that every text is present and that no two columns, genders or statuses
share a normalised label across the three languages. The reader's header and value tables are
built from the same records, so the template and the reader cannot drift. The module namespace is
`Import`, because the analyzers reserve `Imports` (CA1716).

*Alternative considered*: a static template committed under `wwwroot`. It would need three binary
files edited by hand, and they would drift from the reader's labels.

### D9. Rate limiting and request limits

A new policy `RateLimitPolicies.SpreadsheetImports` (`spreadsheet-imports`) allows 10 requests per
minute per user. It applies to preview and import, which parse a workbook. It is configurable as
`RATE_LIMIT_SPREADSHEET_IMPORTS_PER_MINUTE` in `compose.yaml` and documented with the others. The
template download uses `personal-data-writes`. It is not a write, but it builds a file per request,
and the existing per-user budget suffices. Nginx `client_max_body_size 11m` already covers 2 MB.

A fixed window does not bound parallel parses, so the import service also lets at most 2 workbooks
be read at a time across the API (a `SemaphoreSlim`, waiting at most 5 s). A request that cannot get
a slot answers `503 registry.busy`, which the UI already explains as "try again".

### D10. Frontend

**Route and entry point**:
- `/arquebusiers/import`, under `RequireAdmin` in `routes.tsx`, with the breadcrumb
  `nav.arquebusiers` › `registry:import.title`;
- an "Import" secondary action in the Arquebusiers `PageHeader` for Admins.

**Page** `features/arquebusier-registry/pages/ArquebusierImportPage.tsx` uses the form page
template (`FormLayout`) with three sections:
1. **Comparsa**: `SelectInput` with the active comparsas.
2. **File**:
   - a template download button. `apiFetch` only returns JSON bodies, so a new `apiDownload(url)`
     in `src/api/http.ts` fetches the file. It sends the same credentials and `Accept-Language`,
     reports a `401` to the session handler and throws `ApiProblemError` like `apiFetch`, and
     returns the `Blob` with the server's file name. The page saves it through an object URL;
   - a new `FileField` composite (see below);
   - the "Check file" button.
3. **Report**, once there is one:
   - `StatCard`s for read, valid, with errors and with warnings;
   - a `NoticeBanner` for ignored columns and for the "no photos" note;
   - a `DataTable` of the reported rows, with columns row, name and problems. Each problem shows
     the translated column header and reason, and warnings use `StatusBadge` with the existing
     `warning` map. A `StatFilter` "Only errors" toggles the rows.

   The `ActionBar` holds "Import N arquebusiers", disabled while there are errors. It opens a
   `ConfirmDialog` that names the comparsa and the count.

**Data and state**:
- File-level problems are shown in an `AlertBanner`.
- `useArquebusierImportPreview` and `useArquebusierImport` wrap the generated orval mutations.
- Changing the comparsa or the file discards the report, and a check answered after such a change
  is not shown. Each report is shown afresh (a new `key`): its outcome banner comes first and is
  focused, so it is announced and in view, and its "only errors" filter starts off.
- A refused import (`rowErrors`) closes the dialog first; the new report then replaces the old one
  and its outcome is focused.
- On success:
  - `invalidateQueries` runs on the arquebusier list, the compliance summary and the navigation
    count;
  - the page navigates to `/arquebusiers?comparsaId=<id>` with a `SaveNotice` "{{count}}
    arquebusiers imported";
  - on `400 arquebusierImport.rowErrors`, the report from the problem replaces the current one.

**`FileField` composite** (`components/app/FileField.tsx`) is a file picker used as a control
inside `FormField`:
- a visible button opens the hidden `<input type="file">` and carries the field's label, help and
  error wiring (only those: a button cannot be "required");
- it shows the chosen file name and size, and offers a clear action;
- type and size are checked by the page's Zod schema with `fileProblem` (`file-rules.ts`), which
  returns `wrongType` or `tooLarge` for the page to word, and the page re-validates the field on
  every change, so the problem shows at once at the field and in the summary, before any upload.

It gets stories and an axe test, as `catalogue.test.tsx` requires. `PhotoUpload` is not reused,
because it is image-specific (decode, crop).

**Translations**:
- `registry:import.*` holds the page texts, the reasons (`duplicateInFile`, `taken`, `cellError`,
  `empty`, `tooManyRows`, `missingColumns`, `duplicateColumns`), the column headers and the problem
  codes;
- `registry:problems` gains `arquebusierImport.rowErrors` and `arquebusierImport.conflict` in
  `problems.ts`;
- `ui:fileField.*` holds the composite texts.

All of them are added in es-ES, ca-ES-valencia and en, and checked by `npm run check-i18n`.

### D11. Tests and synthetic workbooks

Backend tests build workbooks in memory with ClosedXML (`ImportWorkbookBuilder` in the test
project). Synthetic people only, such as `Arcabucero Sintético` and valid DNIs computed from
numbers, as in the existing tests. The tests cover:
- unit tests for cell conversion, header matching and the zip checks, with a crafted
  zip whose headers understate its sizes;
- integration tests against PostgreSQL for preview, import, the Admin-only access, the audit
  entries, the duplicates and the race. The race test runs two imports in parallel, as the
  existing "Concurrent duplicates" test does.

E2E tests (Playwright) generate the workbook at run time with `write-excel-file` (MIT, dev
dependency only, about 30 KB, one dependency `fflate`). Each run uses fresh synthetic identities
and deletes them after the test through the API. The flow runs only in the desktop Chromium
project, so parallel browser projects never import the same people.

*Alternatives considered*:
- A committed `.xlsx` fixture. Its fixed identities would collide between parallel projects and
  reruns, and a binary file is hard to review for personal data (SEC-11).
- `exceljs`. It is larger and has more transitive dependencies.

A test-only frontend dependency does not overlap ADR-0008, which governs the files the app
produces, so no ADR is needed.

### Research notes (task 1.1)

- ClosedXML 0.105.1 is the current release:
  - `new XLWorkbook(stream)` does not recalculate unless `LoadOptions.RecalculateAllFormulas` is
    set;
  - `IXLCell.CachedValue` returns an `XLCellValue` with its `Type` (`Blank`, `Boolean`, `Number`,
    `Text`, `Error`, `DateTime`, `TimeSpan`) and the `Get*` accessors;
  - list validations take a range of another sheet, and sheets can be hidden;
  - `"@"` sets the text format;
  - a date-formatted cell reads as `XLDataType.DateTime`, honouring the 1904 date system; an
    unformatted number reads as `Number` and is refused in date columns (group 3 review).
- `ZipArchive` reports `Length` and `CompressedLength` from the central directory, which an
  attacker controls. That is why D3 counts the decompressed bytes instead.
- `apiFetch` rejects any 2xx body that is not JSON, so the template uses `apiDownload` (D10). The
  OpenAPI description declares the binary response, and the generated hook is not used.
- `write-excel-file` 4.1.1 (MIT, one dependency, `fflate`) in Node:
  `writeXlsxFile(rows).toBuffer()`. Cells take `{ value, type: Date | Number | String, format }`,
  and a `Date` cell needs a `format`.

## Risks / Trade-offs

- [ClosedXML loads the whole workbook into memory] → The 2 MB upload cap, the zip entry count and
  decompressed-size checks, and the 1001-row check bound memory to a few tens of MB per request.
  At most 2 workbooks are read at a time, the rate limit allows 10 per minute per Admin, and there
  are about 2 Admins.
- [Legacy sheets record the course as yes/no (`CURSO`), not a date] → The import keeps the course
  as a date (data model). Rows without a known date import without a course and show
  `COURSE_MISSING`, which the Federation can see and fix. Guessing a date would invent personal
  data.
- [Dates typed as `mm/dd/yyyy` by an English-locale Excel] → Only `dd/mm/yyyy` and ISO text are
  accepted. Ambiguous text is an error rather than a silent swap. Date cells, the template's
  default, are unaffected by locale.
- [Padding short DNIs could accept a mistyped DNI] → The check letter is still verified on the
  padded number, so a wrong digit almost always fails. Padding only adds zeros that Excel or a
  person dropped.
- [All-or-nothing makes one bad row block a comparsa's whole load] → This is a maintainer decision.
  The report lists every error at once, so one round of fixes usually suffices.
- [Two endpoints repeat the same parsing] → The pipeline is one service. The cost is one extra
  parse of a ≤ 2 MB file per import.
- [The report returns names to the browser] → Only Admins, who can see every arquebusier, receive
  it. The data is the Admin's own upload, and the response is `no-store`. Logs record counts and
  reasons only.

## Migration Plan

- No database migration and no data change. The feature is additive behind the Admin policy.
- Deploy as usual. The new rate-limit variable has a default, so existing `.env` files keep
  working.
- Rollback: redeploy the previous image. Imported arquebusiers are ordinary registry records and
  stay valid.
- The real initial load happens in production by Admins, comparsa by comparsa. Real workbooks
  never enter the repository or non-production environments (SEC-11).

## Verification (task 9.3)

**Result: PASS.**

| Check | Outcome |
|---|---|
| Build | `dotnet build` with 0 warnings; `tsc -b` clean |
| Lint | `eslint . --max-warnings 0` clean; analyzers clean |
| Backend tests | 1280 passing before the last coverage additions (6 more since, all passing); line coverage 95.5 % (gate 80 %) |
| Frontend tests | 1751 passing (31 on the import page), with the 80 % coverage gate; `check-i18n` complete in the three locales |
| E2E (Playwright) | Full suite 238 passed; the import spec passes on desktop Chromium and its read-only 360 px check on mobile |
| Security grep | No secrets, no workbook files, no `console.log`; logs and audit entries hold no personal values (tested) |

**Reviews per task group** (fixes applied before moving on):
- **Group 3, security.** A HIGH finding: deep nesting in a part overflowed the stack inside the Open XML SDK and killed the process. It led to the streaming XML pre-scan in `WorkbookPackage` (depth, document types, row, cell and string counts, 5 MB budget).
- **Group 3, cell conversion.** Plain numbers are no longer accepted as dates, a formula without a value is now a cell error, and text columns accept text only.
- **Group 5.** Workbook reads are bounded to 2 at a time (`ImportSlots`), and a slot is held only while loading.
- **Group 6.** Imports run one at a time under an advisory lock, which avoids deadlocks between overlapping imports. `RegistryLocks.IsRetryable` now walks the exception chain, because a lock timeout during a save arrived wrapped twice and gave a 500. The commit uses no request token.
- **Groups 7 and 8, accessibility.** Focus returns to the button after clearing a file, and the chosen and removed file is announced. The report outcome comes first and is focused on every check and after a refused import. Errors and warnings differ by icon and spoken prefix. `DataTable` gains `rowHeader`. The "only errors" filter is announced.
- **Group 8, `useNotice`.** It dropped the query string when clearing a handed notice, so the list opened after an import lost its comparsa filter. Fixed and tested.
- **`pr-test-analyzer`.** Its gaps are now covered: a federationId already registered (check, import and the race at commit), a heterogeneous import asserted field by field, an existing arquebusier left untouched, the rate limit shared by check and import, every file reason worded on the page, the query invalidation after an import, and the 360 px layout.

**Follow-ups (LOW, not blocking):**
- Assert the template's text and date formats on a far row, not only row 2.
- Extend the wrong-kind cell tests to DNI/NIE, status and license type.
- Add a test for the comparsa deleted between check and import (foreign key, 404).
- Check the oversized-body path under Kestrel, since the in-memory test server does not enforce the request limit.
- The Windows working tree keeps CRLF endings, so `prettier --check` warns locally; git normalises them to LF and CI is unaffected.

