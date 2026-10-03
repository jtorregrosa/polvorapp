# Design

## Context

See `proposal.md` for the motivation and `specs/exports/spec.md` for the behaviour. The facts
below come from the current code and shape the approach.

- **Modules** (`backend/src/Modules/README.md`, ADR-0001): a `.Contracts` project references only
  `PolvorApp.SharedKernel`; modules read each other through contracts that apply no scope (the
  caller enforces BR-12). `ComplianceInsights` and `Billing` are modules without data.
- **Orders.** `ComparsaOrders` owns `ComparsaOrder`, `EditionEntry` (with the history copy of the
  arquebusier and owned weapon) and `WeaponLoan` (with the lender's and the weapon's copy). Its only
  contracts today are `IParticipationHistory` and `OrderStatus`.
- **Registry.** `IArquebusierRoster.FindManyAsync` returns `RosterArquebusier` with
  `ArquebusierLicenseFacts` (`Pending` | `Issued(ExpiresOn, …)`), which has **no license type**.
  `LicenseType` (`AE`, `A_PROF`) is already a contract enum.
- **Catalogue.** `ICatalogDirectory` returns comparsa names and weapon model labels (the
  Federation's Spanish naming).
- **Audit.** `IAuditTrail.Record(context, record)` adds the entry to the caller's `DbContext`, saved
  with the change. An export changes nothing, so it has no unit of work to join.
- **Documents.** ClosedXML 0.105 is used by the registry import (template writer, `TypedResults.File`
  with `no-store`). QuestPDF is accepted (ADR-0008) and its Community license assessed
  (`docs/third-party-licenses.md`), but not referenced yet. It does not use system fonts by default
  (`Settings.UseSystemFonts = false`), and the runtime image is `aspnet:10.0-noble-chiseled-extra`,
  which has no fonts. The frontend already ships Geist (SIL OFL 1.1).
- **Frontend.** Downloads use `apiDownload` (`@/api/http`) and `saveFile` (`@/lib/download`), as the
  import template does. Routes: `editions/:editionId/orders`, `orders/:orderId`.
- **Rate limits.** Per-user policies in `SharedKernel.Security.RateLimitPolicies`, configured by
  `RateLimits__<Policy>__PermitLimit` (compose sets them from `RATE_LIMIT_*`).

## Goals / Non-Goals

**Goals:**
- One table model per export, rendered by one Excel writer and one PDF writer, so a recipient's
  real template later changes only its definition (ADR-0008).
- Deterministic files from synthetic data, checked by golden files.

**Non-Goals:**
- Storing generated files, background generation or a file queue: the files are small (≤ 1,000
  rows) and generated per request.
- A general report builder: the four definitions are code, not configuration.

## Decisions

### D1. An `Exports` module without a schema

`Modules/Exports` (`PolvorApp.Exports`, `PolvorApp.Exports.Contracts`) owns the definitions, the
writers and the endpoints. It reads orders, the registry and the catalogue through their contracts
and owns no table. The `.Contracts` project stays empty until a later change (#13 distribution
lists, #16 badges) needs to reuse the document pipeline.

*Alternative:* exports inside `ComparsaOrders`. Rejected: one module per capability, and the
orders module would grow document generation, fonts and a PDF dependency.

### D2. Contracts the exports read

- `ComparsaOrders.Contracts.IOrderExports` (new, unscoped):
  - `ListValidatedAsync(editionId)` → the `VALIDATED` orders, for the recipient exports;
  - `FindAsync(editionId, comparsaId)` → the comparsa's order in any status, or null when it is not
    prepared, for the comparsa list (a draft while not `VALIDATED`, maintainer decision);
  - both as `ExportedOrder(OrderId, ComparsaId, Status, Entries)` with `ExportedEntry` records: status, powder, caps, weapon source, the history copy (name, DNI/NIE,
    `federationId`), the owned weapon's copy (model id, number, guide), the rental model id, the
    flask, the loan's copy (lender kind, name, DNI/NIE, weapon model id, number, guide) and the
    arquebusier id (null once deleted).
  Its records override `ToString()` like `RosterArquebusier`, so they never print personal data.
- `ArquebusierRegistry.Contracts.ArquebusierLicenseFacts`: `Pending` and `Issued` gain
  `LicenseType Type`. Existing callers ignore it; the compliance tests keep passing.
- `SharedKernel.Auditing.IAuditLog` (new, next to `IAuditTrail`; implemented in `AuditPrivacy`):
  `RecordAsync(AuditRecord, CancellationToken)` saves one entry on its own `AuditDbContext` in its
  own transaction, for actions that write nothing (an
  export). It is the documented exception to "audit in the caller's transaction".

*Alternative for the audit:* an `ExportsDbContext` with an empty `exports` schema, only to join
`IAuditTrail`. Rejected: an empty schema and migration for one insert; `IAuditLog` also serves the
PDFs of #13 and #16.

### D3. Definitions as table models

```csharp
internal sealed record ExportColumn(string Header, ExportCellType Type);   // Text, Integer, Date
internal sealed class ExportTable(                                       // validates every row
    string FileStem, string Title, IReadOnlyList<string> Notices, string VersionLine,
    IReadOnlyList<ExportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyList<object?>? TotalRow);
internal interface IExportDefinition
{
    string Name { get; }                 // "powder-supplier", …
    string Version { get; }              // "provisional-1"
    bool Provisional { get; }
    ExportAudience Audience { get; }     // Recipient (Admin only) or Comparsa (Admin + own FiringChief)
    ExportTable Build(ExportData data, ExportTexts texts);   // pure: no I/O
}
```

An `ExportDataLoader` reads, once per request, the orders (D2), the comparsa names and model
labels (catalogue) and the arquebusiers still in the registry (`FindManyAsync`) into `ExportData`;
the definitions are pure functions of it, unit-tested without a database. Identity and owned
weapon come from the registry while the arquebusier is in it (as the order page shows them), from
the entry's copy otherwise; a loan's lender always from the loan's copy.

`Notices` holds, first, the draft notice of a comparsa list whose order is not `VALIDATED` (its
status in words), then the provisional notice; the file name gains `-draft` for a draft. The
constructor refuses a row whose cell count or cell types do not match the columns, naming the
column and never the value.
Four definitions implement it (`PowderSupplierExport`, `RentalCompanyExport`,
`ArmsAuthorityExport`, `ComparsaListExport`). Each reads the validated orders (D2), the catalogue
labels and, for the Arms Authority, the live license of the arquebusiers still in the registry
(`FindManyAsync`), and sorts with `SharedKernel.Text.SpanishOrder`. Rows carry typed values
(`int`, `DateOnly`, `string`); formatting belongs to the writers. Names are "Last name, First
name"; the weapon source and flask are translated words, not codes.

### D4. Writers

- `XlsxExportWriter` (ClosedXML): worksheet named after the definition; row 1 the provisional
  notice (merged across the columns) when provisional, row 2 the definition name and version, then
  the header row (bold, frozen, auto-filter), the data rows with typed cells (dates with the
  `dd/mm/yyyy` format) and the total row (bold). Column widths are fixed per type, not
  `AdjustToContents`, which measures text with fonts the container lacks.
- `PdfExportWriter` (QuestPDF): A4, landscape above six columns; header with the title, the
  provisional notice and the definition version; a table whose header repeats on every page; a
  footer "page x of y" and the generation date. Fonts: **Geist** regular and bold (OFL 1.1) as
  embedded resources of the module, registered once with `FontManager.RegisterFontFromEmbeddedResource`;
  `Settings.License = LicenseType.Community`, `UseSystemFonts = false` (deterministic output on any
  host). The OFL text ships next to the fonts.
- Both return `byte[]`; files are at most a few hundred KB.

### D5. API

All under `/api/exports`, signed-in users, `Cache-Control: no-store`, rate policy `Exports`:

| Route | Who | Answer |
|---|---|---|
| `GET /editions/{editionId}` | Admin | `ExportCatalogResponse`: each definition's name, version, provisional flag, audience |
| `GET /editions/{editionId}/recipients/{definition}/{format}` | Admin (`403` otherwise) | the file |
| `GET /editions/{editionId}/comparsas/{comparsaId}/{format}` | Admin, FiringChief in scope | the file (a draft while the order is not validated), or `404` (scope, draft edition for a FiringChief), `409 exports.notPrepared` |

`format` is `xlsx` or `pdf`; an unknown `definition` or `format` is `404`. File names:
`polvorapp-{year}-{definition}[-{comparsa-slug}][-draft]-provisional.{ext}`, with the comparsa slug from
its name (not personal data). Order of work per request: check permission and state → build the
table → render → `IAuditLog.RecordAsync` → return the file. A failure before the audit returns no
file; an audit failure is a `503` and returns no file.

### D6. Language

The recipient exports are always Spanish (`es-ES`): recipients are Spanish offices and companies.
The comparsa list follows the request's UI culture (`CurrentUICulture`), like the import template.
`ExportTexts` holds the headings, notices and words in the three languages, in the style of
`ImportTemplateTexts`.

### D7. Audit actions

`ExportDownloaded`, entity type `Export`, entity id the definition name; data `{ version, format,
editionId, editionYear, rows }`; for a comparsa list also `orderStatus` and the `comparsaId`. No names, DNI/NIE or file
contents.

### D8. Rate limit

`RateLimitPolicies.Exports`, per user, 30 a minute by default
(`RateLimits__Exports__PermitLimit`, compose `RATE_LIMIT_EXPORTS_PER_MINUTE`, raised for E2E runs).

### D9. Frontend

- `features/exports/pages/ExportsPage.tsx` at `editions/:editionId/exports` (Admins; a FiringChief
  gets the not-found page). It reads the catalogue (D5) and the orders overview (for the
  comparsas not validated). Sections:
  - a provisional `AlertBanner` (info);
  - a warning `AlertBanner` listing the comparsas not validated, by status;
  - one `SectionCard` per recipient export, saying what it contains, with "Excel" and "PDF"
    buttons;
  - a `SectionCard` with each comparsa that has an order and its two buttons, marked as a draft
    while the order is not validated.
- `features/exports/components/DownloadButtons.tsx`: the pair of buttons; `pending` while
  downloading, a translated error (`exports.notPrepared`, `429`, network) in an `AlertBanner`.
- `OrdersOverviewPage`: an "Exports" link in the header actions for Admins.
- `OrderPage`: "Download list" (`DownloadButtons`) in the record actions for an order in any
  status, saying "draft" while it is not `VALIDATED`.
- i18n: an `exports` namespace in the three locales (page, recipients, contents, buttons, errors).

### D10. Tests

- Unit: each definition built from synthetic `ExportedEntry` lists (sorting, filtering, columns,
  empty edition, entries no longer in the registry).
- Golden files (ADR-0008): for each definition and format, the generated file read back — Excel
  through ClosedXML into a text grid, PDF through PdfPig (Apache-2.0, test-only) into its text — is
  compared with a committed `.golden.txt`. The generation date is fixed through `TimeProvider`.
- Integration (Testcontainers): permissions, `404`/`409`/`429`, `no-store`, audit entries, a
  FiringChief's scope.
- Frontend: Vitest + Testing Library + axe for the page and the buttons.
- E2E: the Admin downloads an Excel and a PDF from the seeded edition; the FiringChief downloads
  their list as a draft from the seeded submitted order (read-only), and as final once the serial
  review cycle validates it.

## Risks / Trade-offs

- **[QuestPDF native library or fonts fail in the chiseled image]** → Fonts are embedded; the
  `images` CI job builds the image and an E2E test downloads a PDF from it. Task 1.1 verifies the
  native asset for `linux-x64` is in the publish output.
- **[A provisional file is sent to a recipient as final]** → The file name, its first line and the
  page say "provisional"; the definition version is in the document.
- **[Arms Authority data from the registry differs from the order's date]** → The license is read
  at export time, which is what the Authority needs (the current license); stated in the spec.
- **[Files hold personal data on the user's machine]** → Out of PolvorApp's control; downloads are
  audited, and the privacy notice (Q-50) must cover them.
- **[QuestPDF Community license terms change]** → Re-check on every major upgrade, as recorded in
  `docs/third-party-licenses.md`.

## Migration Plan

No database migration. Deploy the new images; the API change is additive. Rollback: redeploy the
previous images. When a recipient's template arrives, a follow-up change replaces that definition
(new version, `Provisional = false`) and its golden files.

## Open Questions

- The recipients' real layouts (Q-44): replace the provisional definitions when they arrive; the
  approach does not change.

### Research notes (task 1.1)

- **QuestPDF 2026.9.1** (Community license, as assessed): `Settings.License = LicenseType.Community`
  before the first document; `UseSystemFonts` is `false` by default, so fonts are registered with
  `FontManager.RegisterFontFromEmbeddedResource` (TTF/OTF/TTC only — not WOFF2). Tables repeat
  `table.Header(...)` on every page; `page.Size(PageSizes.A4.Landscape())`; footer
  `text.CurrentPageNumber()` / `text.TotalPages()`; `document.GeneratePdf()` returns `byte[]`.
  The `linux-x64` native asset is checked in the published image by the `images` CI job and the
  E2E PDF download (task 8.1).
- **Fonts**: the frontend's `@fontsource-variable/geist` ships WOFF2 only. The official `geist`
  npm package 1.7.2 (SIL OFL 1.1) has `dist/fonts/geist-sans/Geist-Regular.ttf` and
  `Geist-Bold.ttf` and `LICENSE.txt`; those two files and the license are embedded unmodified.
- **ClosedXML 0.105**: cells take `DateTime` (a `DateOnly` is converted), the repo's date format is
  `dd/mm/yyyy`; `Range(...).Merge()`, `SheetView.FreezeRows`, `Range.SetAutoFilter()`; column
  widths set by hand, as `ImportTemplateWriter` does. License types are written as the Federation
  does (`AE`, `A-PROF`), like the import template.
- **PdfPig 0.1.16** (Apache-2.0, test only): `PdfDocument.Open(bytes)`, `NumberOfPages`,
  `page.Width/Height`; the golden text is built from `page.GetWords()` grouped into lines by their
  baseline and ordered left to right, which does not depend on how glyph runs are emitted.
- **ASP.NET Core 10**: `TypedResults.File(bytes, contentType, fileName)` (as the import template),
  `RequireRateLimiting` on the route group, policies registered by the host from
  `RateLimits__<Policy>__PermitLimit`.

### Review notes (group 2)

`csharp-reviewer`, `database-reviewer`, `security-reviewer`: no CRITICAL/HIGH. Applied: the export
reads run in one repeatable-read transaction (a file never mixes states); `IOrderExports` documents
on each method the caller's BR-12 duty, that a non-validated order yields a draft, and that the
export is audited before the file is returned; `AuditLog` clears its tracker when the save fails;
tests for an edition without validated orders, another edition's order, a registered lender and
license equality by type. Kept: whole entry rows are read (≤ 1,000 per edition), and the entry
enums are public in `ComparsaOrders.Contracts` (their codes and OpenAPI names are unchanged).
`IAuditLog` lives next to `IAuditTrail` in `SharedKernel.Auditing` (not `AuditPrivacy.Contracts`),
where every module already finds the audit types.

### Review notes (group 3)

`csharp-reviewer`, `type-design-analyzer`, `security-reviewer`: no CRITICAL/HIGH; SEC-06 holds per
definition. Applied: `ExportTable` validates cell counts and types; the draft notice comes first,
as the spec's scenario says; a weapon model missing from the catalogue fails instead of leaving a
blank cell; totals are summed from typed values and grouped by comparsa id; a weapon without
details reads as its source word alone; an empty slug falls back to `comparsa`; one
`ProvisionalVersion` constant; tests for every code's word in the three languages, the notices and
version line in Valencian and English, slugs, first-name and accent order, registry identity over
the copy, and the one-order rule. To do in group 4: formula injection — the Excel writer writes
every text cell as a string and neutralises a leading `=`, `+`, `-`, `@`, tab or CR.

### Review notes (group 4)

`csharp-reviewer`, `silent-failure-hunter`: no CRITICAL; the HIGHs were silent blanks. Applied: an
arquebusier in the registry without a license reads "Sin licencia" for the Arms Authority (spec
updated; a deleted one stays empty); a recipient export refuses an entry whose weapon has no data
and caps boxes without a type (both impossible for a validated order, so they fail loudly instead of
under-reporting); a draft's weapon without details reads "Cesión: sin datos"; QuestPDF is set up when
the module is registered, so a broken font fails the start; no synthetic italic; text starting with an
apostrophe is protected in Excel too; `ThrowOnMissingTextGlyphs` is explicit (a name Geist cannot draw
fails the PDF, never prints wrongly; Latin Extended is covered by a test); tests for empty tables in
both writers; PDF lines grouped with a 2 pt tolerance. The Excel formula neutralising writes `''`
because ClosedXML turns one leading apostrophe into Excel's hidden quote prefix.

### Review notes (group 5)

`csharp-reviewer`, `security-reviewer`: no CRITICAL; one HIGH (no test of a recipient download's
audit entry), fixed. Applied: the redundant `no-store` endpoint filter is gone — the host's security
headers already set `Cache-Control: no-store` on every API answer, refusals included, now asserted on
403, 404 and 429; the file responses are declared with their content types in OpenAPI; the format is
checked before any I/O; tests for the catalogue of an unknown edition, mixed validated and submitted
orders, an unknown comparsa (same 404 as another comparsa's: no oracle), an unknown list format, and
that refusals are not audited. Kept: the file is rendered before it is audited (bounded by the rate
limit). Process note: the endpoint tests of 5.1–5.4 were written right after the service, not before
it; all scenarios are covered.

### Review notes (group 6, and the BR-12 guard)

`react-reviewer`, `a11y-architect`: no CRITICAL; two HIGHs fixed. A second click while a download
runs is ignored (both buttons could clear each other's state). The `Button` composite now joins its
"one moment" words to an `aria-label`, which otherwise hid them from screen readers (a fix for every
labelled pending button, not only these). Also: a polite status region announces "Downloaded:
<file>", the failure banner's title says what was not downloaded, and tests cover concurrency, the
fallback file name, a retry clearing the failure and the Valencian and English names. Kept: a download
started before leaving the page still completes. The full backend run found the BR-12 guard
(`ComparsaScopeGuardTests`) probing the new `/comparsas/{comparsaId}` export route without knowing
`editionId` and `format`: it was taught them, with an order of the FiringChief's comparsa, so the route
is checked like every other comparsa route (refused to another comparsa, reached by its own).

### Review notes (group 7)

`react-reviewer`, `a11y-architect`: no CRITICAL/HIGH. Applied: the recipient cards render as soon as
the catalogue arrives and the comparsa lists with the orders, independently; the way back uses
`PageHeader`'s `back` link (above the h1, as elsewhere), with a generic label before the year is
known; long comparsa names wrap; unused keys removed; tests for the not-found edition, every order
validated, no order prepared, the order status badges, and a FiringChief's order page. The order
list is a section of the order page rather than a record action: it carries its own status region
and error banner. The exports page is under `RequireAdmin`, so a FiringChief sees the usual
"not allowed" page (spec scenario aligned).

### Verification (task 8.2)

`verification-loop`: **PASS**.

- **Backend**: build and `dotnet format --verify-no-changes` clean; 1,855 tests, all green except
  one pre-existing intermittent test (`OrderPreparationTests.The_order_offers_the_entry_choices_without_ownership_guides`,
  which compares the order of two weapons created at the same instant; it passes alone and in its
  class twice; not touched by this change — follow-up). `PolvorApp.Exports` 98.9 % line coverage;
  `OrderExports`, `AuditLog` and the changed contracts 100 %. `contracts/openapi.json` additive only.
- **Frontend**: build, `tsc`, ESLint (0 warnings), Prettier and `check-i18n` clean; 2,099 tests,
  95.7 % line coverage overall.
- **E2E** (rebuilt compose stack, seeded, `RATE_LIMIT_EXPORTS_PER_MINUTE=300`): 264 then 266 passed
  twice in a row; the PDF is generated inside the chiseled Linux image with the embedded fonts. One
  run hit the known intermittent `select.spec.ts` on Firefox, unrelated; it passed on the reruns.
- **Security grep**: three routes, all behind the signed-in default, the recipient ones behind the
  Admin policy; logs carry ids only; audit data has no personal values; file names are ASCII slugs;
  `no-store` from the host on every answer.
- **`pr-test-analyzer`**: every scenario covered; applied the per-user rate-limit check. Not added
  (low value): an unauthenticated 401 (covered by the host's global rule tests), an Admin on a draft
  edition.
- **`e2e-runner`**: no CRITICAL/HIGH; applied a role-based locator for the missing comparsas, a
  FiringChief opening the exports page directly, and the final list no longer reading "draft".

Follow-ups: the intermittent `OrderPreparationTests` ordering test; replace the provisional
definitions when the recipients' templates arrive (Q-44).
