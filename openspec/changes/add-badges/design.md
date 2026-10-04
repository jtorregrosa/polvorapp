# Design

## Context

See `proposal.md` for the motivation and `specs/badges/spec.md` for the behaviour. The facts below
come from the current code and shape the approach.

- **Modules** (ADR-0001, `backend/src/Modules/README.md`): one module per capability, and other
  modules are read only through `.Contracts`. Adding a module touches `PolvorApp.slnx`,
  `Program.cs` (`AddModules`), the `Dockerfile` restore layer and the architecture tests.
- **Registry.** `IArquebusierRoster` gives `RosterArquebusier`: names, `NationalId`,
  `FederationId`, `Status`, `ComparsaId`, `ArquebusierLicenseFacts` (`Pending` |
  `Issued(Type, ExpiresOn, …)`) and `HasIdPhoto`.
  - `ListByComparsaAsync` already returns them in Spanish order.
  - `FindManyAsync` is unscoped and documented as "never ids taken from a request".
  - No contract returns photo bytes. The only reader is the registry's internal
    `ArquebusierPhotoAdministration.OpenAsync`, which retries once when a photo is replaced between
    reads and maps storage failures to `StorageUnavailable`.
  - Photos are stored as JPEG, up to 1200 × 1600 for the ID kind.
- **Catalogue.** `ICatalogDirectory`:
  - `FindComparsaAsync` / `FindComparsasAsync` give comparsa names;
  - `ReadFederationLogoAsync` gives `LogoImage(Png, Width, Height)` or null, and throws
    `StorageUnavailableException` when the storage is down.
- **Documents.** `Exports` owns QuestPDF (2026.9.1, Community licence), the embedded Geist fonts
  (`PdfSetup`, `ThrowOnMissingTextGlyphs`) and the deterministic metadata. Other modules render
  through `Exports.Contracts.IDocumentRenderer`, which has `RenderTable`, `RenderForm` and
  `RenderWorkbook`. `DocumentImage.FromPng` accepts PNG only, up to 4 MB and 4096 px a side.
  `DocumentFileStem` validates file names.
- **Images.** `SharedKernel.Images.IImageNormalizer` (SkiaSharp, in the host) decodes, turns
  upright, scales down and re-encodes without metadata, given `ImageRules`.
- **Downloads.**
  - The pattern is the one in `DistributionDocuments` and `ExportService`: check permission and
    state, build, render, `IAuditLog.RecordAsync` (`503` and no file when it fails), then
    `TypedResults.File`.
  - Rate policy `RateLimitPolicies.Exports` (30 per minute per user).
  - `Cache-Control: no-store` comes from the host.
  - The frontend has `apiDownloadPost(url, body)`, which sends the anti-forgery token, and
    `saveFile`.
- **Frontend.**
  - `components/app/DataTable` is built on TanStack Table v9 (`tableFeatures` with sorting and
    pagination) and has no selection. Its row click already ignores `[role="checkbox"]`.
  - `ArquebusiersPage` loads the whole scoped list and filters it on the client: `?comparsaId`,
    warnings and search. Each row has `licenseStatus` and `hasIdPhoto`.
  - `ComparsaDetailPage` has `moreActions` for Admins.

## Goals / Non-Goals

**Goals:**
- Badge rules, ordering, texts and content live in a `Badges` module. Rendering stays in `Exports`,
  next to the other PDFs and the fonts.
- The sheet prints at exact size. This is verified from the PDF's geometry, not by eye.
- A 200-badge document stays small and fast: photos are scaled to print size before embedding.
- Output is deterministic and covered by golden files built from synthetic data and generated
  images.

**Non-Goals:**
- A generic card or label layout engine: one badge layout, versioned.
- Previewing the PDF in the browser; the download is the preview.
- Server-side memory of selections; the selection lives in the page.

## Decisions

### D1. A `Badges` module without a schema

`Modules/Badges` (`PolvorApp.Badges`, `.Contracts`) owns the endpoint, the batch rules, the texts,
the badge data and the audit action. It references the contracts of `ArquebusierRegistry`,
`FederationCatalog`, `IdentityAccess` and `Exports`. It stores nothing, so it has no `DbContext`
and no migration. Its `.Contracts` project stays empty, as `Exports.Contracts` once did.

*Alternative:* a badge endpoint inside `ArquebusierRegistry`. Rejected: `badges` is its own
capability in `docs/mvp.md`, and the registry would start depending on `Exports` and the catalogue
logo.

### D2. Rendering: a badge-sheet document in `Exports.Contracts`

- `IDocumentRenderer` gains `RenderedDocument RenderBadgeSheet(DocumentBadgeSheet sheet)` (PDF only).
- `DocumentBadgeSheet`:
  - `FileStem` and `Title` (the PDF's title, without personal data). The layout version is a constant
    of the badges module, recorded in the audit entry;
  - `HeaderWord`, `HeaderLine` (the Federation name) and `Logo` (`DocumentImage?`);
  - the field labels;
  - the `Badges`: 1 to 200 `DocumentBadge` items. Each has `Photo` (`DocumentImage?`) and six
    `(label, value?)` fields in a fixed order. A null value is an empty line.

  The badges module decides content, order and words. `Exports` decides geometry.
- A new `Writers/PdfBadgeSheetWriter` lays out the sheet, in millimetres (QuestPDF `Unit.Millimetre`):
  - **Page**: A4 portrait with no page margin. The cards form a 2 × 5 grid with no gap, centred on
    the page (left margin 19.4 mm, top margin 13.55 mm). Cards share their cutting lines, so one cut
    serves two cards.
  - **Crop marks**: a 0.25 pt line, 5 mm long, starting 2 mm outside the grid, on each vertical and
    horizontal cutting line, in the page margins. On a partly empty last page, marks are drawn only
    for the lines that bound a printed card.
  - **Card**:
    - a header band 12 mm high in Federation green (`#1B5E3A`, a constant of the writer and
      provisional until the Federation confirms its colour). The band reaches the card edges.
    - The band's contents stay in the 3 mm safe area: the logo at its left (max 9 mm high, keeping
      its shape), the header word in Geist Bold, and the Federation name in small type below it.
    - Under the band, the photo slot: 20 × 26.67 mm at 3 mm from the left edge.
    - The six labelled fields come next, in two text sizes (label 5 pt, value 7 pt bold).
    - The seal area is about 22 × 22 mm at the bottom right, with no border (the reference badge
      shows a free area).
  - **Long values**: `ScaleToFit` down to 5 pt, then a second line. QuestPDF's `ShowEntire` keeps
    any value from being cut silently: a value that still does not fit fails the render with
    `DocumentRenderingException`, never with a cut text.
  - **Missing photo**: a 0.5 pt grey frame of the slot's size.
  - **Empty field**: a thin line where the value would be.
  - **Metadata**: title "PolvorApp badges" and fixed dates, as `PdfSetup.Metadata` already does.
- `DocumentImage` gains `FromJpeg` and an informational `Format`; the property `Png` becomes `Content`.
  `FromJpeg` reads the size from the frame header and accepts only what the writers draw as is: a
  complete file (end-of-image marker), baseline, extended or progressive (SOF0–SOF2), 8-bit, grey or
  colour (1 or 3 components); same 4 MB and 4096 px limits. QuestPDF draws JPEG natively.
- `DocumentBadge` turns blank values into null (an empty line). Labels shrink like values, so a long
  translated label never overflows.

*Alternative:* QuestPDF referenced from `Badges`. Rejected for the same reasons as add-distribution
D3: a second font setup, process-wide settings, and two places that own PDF output.
*Alternative:* a generic "cards" contract. Rejected (YAGNI): one layout is needed. The contract is
named for badges and versioned, so it can be generalised later if a second card appears.

### D3. ID photos: a registry contract, scaled for print

- `ArquebusierRegistry.Contracts.IIdPhotoReader.ReadAsync(Guid arquebusierId, CancellationToken)`
  returns an `IdPhoto` (a class that refuses anything but JPEG bytes of at most 4 MB) or null when
  there is no ID photo. The read is bounded: an object announcing or holding more than 4 MB, or not a
  JPEG, is logged as an error and treated as missing.
  - It is unscoped: only Admin-only callers may use it. The XML doc says so, and an architecture test
    allows only the registry and the badges module to reference it (group 2 review).
  - It reuses the registry's open-and-retry logic, extracted from
    `ArquebusierPhotoAdministration.OpenAsync` into a shared internal reader. The photo endpoint
    keeps its behaviour.
  - It throws `StorageUnavailableException` when the storage is down.
  - A reference whose object is missing (a race with a replacement after the retry, or an orphan)
    returns null and logs a Warning with the arquebusier id. The badge then shows the empty frame,
    because the registry itself no longer holds a readable photo.
- The badges module scales each photo with `IImageNormalizer` and `ImageRules`:
  - `FixedAspect` 3:4 and max 300 × 400 px, which is about 380 dpi at 20 × 26.67 mm, above the
    300 dpi of NFR-15;
  - JPEG output, input caps as the registry's.

  This gives about 30 KB per photo and a 200-badge PDF of a few MB.
- Photos are read and scaled one after the other: the registry's reader shares the request's
  database context, and the image pipeline (`SkiaImageNormalizer`) handles one image at a time for
  the whole process anyway. Its input cap for print is 2 MP, as stored ID photos are at most
  1200 × 1600. When the pipeline stays busy beyond its wait (`ImageProcessingBusyException`), the
  sheet answers `503 badges.busy` like a missing slot (group 3 review).
- At most two badge sheets are built at a time per process (`BadgeSlots`, a `SemaphoreSlim(2)` as
  `ImportSlots`). A request that cannot get a slot within 10 s answers `503 badges.busy` (retryable).
- **Unreadable photos** (maintainer decision, group 3 review): when the roster says an arquebusier
  has an ID photo but the reader returns none (image gone, oversized, not a JPEG) or the pipeline
  refuses it, the sheet is refused with `409 badges.photoUnreadable` and the problem's
  `arquebusierIds` in print order; nothing is audited. A failure while the image streams is wrapped
  as `StorageUnavailableException` (503), as the GDPR export and the logo reader do.

*Alternative:* embed the stored 1200 × 1600 JPEG as is. Rejected: about 300 KB × 200 = 60 MB PDFs
and the same in memory.

### D4. Batch rules and ordering

`BadgeBatch` (pure, unit-tested) validates the request. The spec's blocking rules are checked in
this order: `batch`, `language`, `arquebusierIds` (1–200, then each known), then the comparsa.

- **Comparsa**: `ICatalogDirectory.FindComparsaAsync` (404 when unknown), then
  `IArquebusierRoster.ListByComparsaAsync` (already in Spanish order), then the 200 cap
  (`409 badges.tooMany`) and the empty check (`409 badges.nothingToPrint`).
- **Selection**:
  - duplicates are removed, keeping the first position;
  - `IArquebusierRoster.FindManyAsync` reads the rest. Request ids are acceptable here because the
    endpoint is Admin-only and an Admin's scope is the whole Federation. The contract's XML doc
    gains that exception;
  - a missing id answers `400` naming `arquebusierIds[i]` (its index in the request);
  - comparsa names come from `FindComparsasAsync`;
  - the order is comparsa name, then last name, then first name, all with `SpanishOrder`.
- **Badge values**:
  - `expiresOn` comes from `ArquebusierLicenseFacts.Issued`, as `dd/MM/yyyy`; `Pending` or none
    gives an empty line;
  - `federationId` is written without separators;
  - `nationalId` is the stored normalised value.

### D5. Texts and language

- `BadgeTexts` has `Spanish`, `Valencian` and `English` records (as `DistributionTexts`): the header
  word, the labels and the Federation name.
- The Federation names are the ones `DistributionTexts` prints. They move to the constants
  `Exports.Contracts.FederationNames.Spanish` and `.Valencian`, so the strings cannot drift; each
  texts record picks one (English uses the Spanish proper name).
- `BadgeTexts.For(BadgeLanguage)` picks the language from the request body, never from
  `CurrentUICulture`, because the Admin chooses it (Q-49 interim).
- `BadgeLanguage` is a coded enum (`es-ES`, `ca-ES-valencia`, `en`), registered with
  `CodedEnumsTests`.
- Geist covers every glyph needed (á é í ó ú ü ñ ç à è ò ï l·l « »). `ThrowOnMissingTextGlyphs`
  catches a name it cannot draw, and the endpoint answers `500` with a logged id. A test covers
  Valencian and accented synthetic names.

### D6. API

`POST /api/badges/sheet`:
- **Access**: Admin only (`AuthorizationPolicies.Admin`), rate policy `Exports`, request body limit
  16 KB. Without a session the platform's anti-forgery check may answer `400` before authentication
  answers `401`, as on every other unsafe route.
- **Body**: `{ comparsaId?: uuid, arquebusierIds?: uuid[], language: "es-ES" | "ca-ES-valencia" | "en" }`.
- **Responses**:
  - `200 application/pdf`, with `Content-Disposition` from `TypedResults.File`;
  - `400` with field errors (`batch`, `language`, `arquebusierIds`, `arquebusierIds[i]`);
  - `404 badges.notFound`;
  - `409 badges.tooMany` / `badges.nothingToPrint` / `badges.photoUnreadable` (with `arquebusierIds`);
  - `503 storage.unavailable` / `badges.auditUnavailable` / `badges.busy`;
  - `429`.
- **Why `POST`**: a selection of 200 UUIDs does not fit a URL comfortably, and `apiDownloadPost`
  already exists. The request changes nothing.
- **File names** (`DocumentFileStem`, `FileSlug`):
  - `polvorapp-badges-{comparsa-slug≤40}-{yyyyMMdd}.pdf` for a comparsa batch;
  - `polvorapp-badges-selection-{count}-{yyyyMMdd}.pdf` for a selection;
  - the date is `FederationCalendar.Today`.

The incomplete-badge counts are not a separate endpoint. The sheet derives them from the registry
list rows it already has (`hasIdPhoto`, `licenseStatus` null or `PENDING`). Whether the Federation
logo is missing comes from the existing `GET /api/federation`. The server stays the authority for
the document; the counts are advice.

### D7. Audit

- `BadgesAuditActions.BadgesDownloaded`, entity type `Badges`, recorded with `IAuditLog.RecordAsync`
  after rendering and before returning.
- The data is `{ batch: "COMPARSA" | "SELECTION", comparsaId?, language, count, arquebusierIds,
  version }`, plus the audit entry's `comparsaId` for a comparsa batch so the comparsa's history
  shows it.
- It holds no names, DNI/NIE or images.
- The label `audit:actions.BadgesDownloaded` goes in the three locales, which the frontend's audit
  label test enforces.

### D8. Frontend

- **`DataTable` selection** (design-system delta):
  - optional props `rowSelection` (controlled `Record<string, boolean>` keyed by `getRowId`),
    `onRowSelectionChange` and `getRowLabel` (the checkbox's accessible name). The selection is
    plain controlled state computed over the current page's rows rather than TanStack's
    `rowSelectionFeature`: it must keep ids of rows outside `data` and needs no other table API;
  - the header checkbox acts on the current page, with an indeterminate state;
  - the stacked phone rows get the same checkbox;
  - the selected count goes through a polite live region;
  - selected rows use the `selected` surface token plus a left accent border (not colour alone).
  - Tested with Testing Library and axe, with a story and a catalogue entry.
- **`ArquebusiersPage`** (Admins only):
  - the selection state lives in the page and is cleared when the page unmounts;
  - a selection bar shows "N selected", "Clear" and "Print badges". Above 200 the action is
    disabled, with the reason;
  - with `?comparsaId` set and nothing selected, a header action prints that comparsa's badges.
- **`ComparsaDetailPage`** (Admins): "Print badges" in the `RecordHeader` actions, shown when the
  comparsa has arquebusiers (the panel needs a trigger of its own; a menu item could not open it).
- **`features/badges/components/BadgeSheet`** (`DetailSheet`, whose trigger gains `disabled`; the
  panel changes nothing, so not `EditSheet`):
  - shows the batch (the comparsa name, or "N arquebusiers from M comparsas");
  - offers the language as `RadioCards` (es / ca / en, defaulting to the user's locale);
  - shows a warning `Alert` with the incomplete counts and the missing logo, and the 100 % scale
    note;
  - downloads through `apiDownloadPost` + `saveFile`, with the problem codes mapped to translated
    reasons (`badges.tooMany` suggests a selection).
  - For a comparsa batch opened from the comparsa page, it reads the registry list filtered by
    `comparsaId` for the counts.
- **Group 5 review**: arquebusiers the registry no longer has leave the selection when the panel
  closes, so the panel and its explanation never vanish under the user; a refusal focuses its
  banner; the disabled trigger is `aria-disabled` (still focusable) and described by the limit
  text, which is a polite live region; "Saved" is visible; reopening the panel starts clean.
- **i18n**: a new `badges` namespace in the three locales, registered in `i18n/index.ts` and
  `i18next.d.ts`. The `ui` keys for the table selection go in the `ui` namespace.

### D9. Tests

- **Unit**:
  - `BadgeBatch` (rules, order, dedup, indexes);
  - badge values (expiry formatting, empty lines);
  - `BadgeTexts` in three languages;
  - `DocumentImage.FromJpeg`;
  - `DocumentBadgeSheet` validation (1–200 badges, six fields).
- **Renderer** (PdfPig):
  - page count and size (595.28 × 841.89 pt);
  - each card's frame at 85.60 × 53.98 mm (± 0.05 mm) at the computed positions;
  - words inside the 3 mm safe area;
  - one image per photo plus the logo;
  - crop mark paths on the cutting lines only;
  - a 100-character `lastName` printed in full;
  - an 11-badge sheet on 2 pages.
- **Golden files**: the text of a sheet in each language, with and without logo, from synthetic
  data and generated images. They must give identical output on two runs.
- **Integration** (Testcontainers, MinIO):
  - `403` for a FiringChief and `401` without a session;
  - every `400`, `404` and `409`;
  - `503` on storage down (logo and photo), on audit failure and when busy;
  - `429`;
  - `no-store`;
  - file names;
  - the audit entry's data;
  - a photo replaced between list and read is still printed (retry).
- **Frontend**: `DataTable` selection, `BadgeSheet` and the page actions with MSW, and axe.
- **E2E**: see tasks.

## Risks / Trade-offs

- **[The printer scales the page ("fit to page") and the cards come out small]** → The sheet
  reminds the user to print at 100 %. The page margins (≥ 13 mm) fit any desktop printer, so the
  print dialog has no reason to shrink.
- **[The Federation green or the layout differs from the current badge]** → The colour and the
  layout version are constants in one writer. Changing them is a styling fix after the
  Federation's feedback, without an OpenSpec change. The manual regression includes one print on
  cardstock checked with a ruler and a sleeve.
- **[Q-49 answered "Spanish only"]** → Default the language to es-ES or drop the choice: a small
  follow-up change.
- **[A 200-badge request is heavy]** → Photos scaled to 300 × 400, at most 4 reads in flight, two
  documents at a time per process, and the per-user rate limit.
- **[Badges carry DNI/NIE and photos, the most sensitive printed output]** →
  - Admin only, generated per request and never stored, `no-store`;
  - audited with ids only, file names without personal data;
  - no data beyond §4 (SEC-06).
  - The privacy notice (Q-50) must mention the badges.
- **[A photo silently missing because of a storage glitch]** → A storage failure answers `503`;
  a photo held but unreadable answers `409 badges.photoUnreadable` naming the arquebusiers. Only an
  arquebusier without an ID photo gets the empty frame.
- **[The selection in the browser goes stale while the registry changes]** → The server validates
  each id at generation. A deleted arquebusier answers `400` naming it, and the UI removes it from
  the selection and says why.
- **[`ShowEntire` fails on an extreme value]** → The registry caps names at 100 characters and the
  comparsa name is bounded. A test proves the longest allowed values fit with the scaling and the
  second line.

## Research (task 1.1)

Checked on 2026-10-04 with Context7 and the installed packages:

- **QuestPDF 2026.9.1**:
  - **Geometry**: the grid is built with fixed `Width` / `Height` / `Padding` in
    `Unit.Millimetre`. The crop marks are drawn as one page-sized SVG layer (`Layers` with
    `.Svg(size => …)`, a `viewBox` in millimetres), which gives exact vector lines without
    positioning many small elements.
  - **Text that does not fit**: `ScaleToFit` scales down the available space and iterates, so it is
    used only per field value. `ShowEntire` throws `DocumentLayoutException` when content does not
    fit, which `PdfSetup.Generate` already turns into `DocumentRenderingException`.
  - **Images (workaround)**: QuestPDF re-encodes images to the document's `ImageRasterDpi`, 288 by
    default. That would downscale the 300 × 400 photo of D3 to about 227 × 302 px, below the
    300 dpi the spec requires. Badge photos are therefore drawn with `.UseOriginalImage()`: they are
    already scaled and re-encoded by D3. The logo keeps the default.
- **PdfPig 0.1.16**: `Page.Paths` (`PdfPath.GetBoundingRectangle()`, `LineWidth`, `IsStroked`) and
  `IPdfImage.Bounds`, `WidthInSamples` and `HeightInSamples` give the card frames, the crop marks
  and the embedded photo size for the tests.
- **TanStack Table 9.2.4**: `rowSelectionFeature` with controlled `state.rowSelection` and
  `onRowSelectionChange`, plus `getIsAllPageRowsSelected`, `getIsSomePageRowsSelected`,
  `getToggleAllPageRowsSelectedHandler` and `row.getToggleSelectedHandler`. The page-level toggle
  changes only the current page's rows. Selected keys of rows that are not in `data` (filtered out)
  stay in the controlled state.

## Verification (task 6.2, 2026-10-04)

- **Build, types, lint, format**: `dotnet build`, `dotnet format --verify-no-changes`, `tsc -b`,
  `eslint --max-warnings 0`, Prettier and `check-i18n` pass; Storybook builds.
- **Tests**: backend 2423 API tests and 39 architecture tests pass (full suite), plus the tests added
  after it; frontend 2366 Vitest tests pass. Coverage of the changed code: backend 96.2 % of lines
  (badges module, document pipeline, registry photo reader), frontend 93–100 % per changed file
  (95.6 % overall).
- **E2E**: `e2e/badges.spec.ts` passes on desktop-chromium and mobile-360. Of three full-suite runs,
  one was fully green and two had a single, different Firefox-only failure in specs this change
  does not touch (`insights.spec.ts` under parallel load, `photos.spec.ts` file chooser timeout);
  both pass on `--repeat-each=3`. Full runs must be launched from a shell with `docker` on the PATH
  (the notifications spec calls `docker compose run`).
- **Security grep**: Admin-only route, no image files added, no `console.log`, logs and audit data
  hold ids and counts only, `no-store` asserted, file names without personal data.
- **Reviews** (per group): csharp-reviewer, type-design-analyzer, security-reviewer,
  silent-failure-hunter, react-reviewer, a11y-architect, e2e-runner, pr-test-analyzer. No
  CRITICAL finding; every HIGH finding was fixed (JPEG kinds the writers cannot draw, unreadable
  photos now blocking, mid-stream storage failures as 503, busy image pipeline as 503, near-miss
  clicks on the selection cell, phone page selection, error focus and the unknown-ids panel).

**Follow-ups (not blocking)**

- Firefox E2E flakiness under full parallel load (file chooser timeout, insights race): pre-existing
  specs; consider fewer workers for the Firefox projects.
- Print a proof on cardstock and confirm the Federation green and layout (manual regression in
  `docs/mvp.md`); Q-49 may simplify the language choice.
- An assertion that the PDF embeds its fonts (QuestPDF always embeds the registered Geist fonts;
  no test reads the font dictionary yet).

## Migration Plan

No database migration. Deploy as usual. Rollback means reverting the release, since nothing is
stored.

## Open Questions

- Q-49 (labels' language) stays open with the Federation. The interim choice covers either answer
  without changing this design.
- The exact Federation green and whether the seal area needs an outline: confirm with the Federation
  on the first printed proof. Both are writer constants.
