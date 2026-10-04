# Tasks

## 1. Research

- [x] 1.1 Check with Context7, and `gh search code` for real-world use, what designs D2, D3 and D8
  rely on:
  - QuestPDF 2026.9.1:
    - absolute millimetre layout on a margin-less A4 page (`Layers`, `TranslateX/Y` or
      `Unconstrained`);
    - drawing thin lines for crop marks;
    - `ScaleToFit` and `ShowEntire` on text;
    - JPEG images (`Image(byte[])`) with `FitArea`;
  - PdfPig 0.1.16: reading path and rectangle geometry, and image placement, for the size
    assertions;
  - TanStack Table v9: `rowSelectionFeature`, controlled `rowSelection`,
    `getIsAllPageRowsSelected` / `getIsSomePageRowsSelected`.

  Record versions and any workaround in design.md. Verify: design.md is updated, with no new open
  question.

## 2. Document pipeline (Exports and registry contracts)

- [x] 2.1 Write unit tests for `DocumentImage.FromJpeg` (design D2):
  - it accepts a generated JPEG and reads its width and height;
  - it refuses a PNG, truncated bytes, more than 4 MB and more than 4096 px a side.

  Then implement it, and let `PdfFormWriter` draw either kind. Verify: the tests pass and the
  existing form golden files are unchanged.
- [x] 2.2 Write unit tests for the `DocumentBadgeSheet` / `DocumentBadge` validation:
  - 1 to 200 badges, exactly the six fields, non-empty labels;
  - the `FileStem` rule;
  - `ToString()` prints no field values.

  Then add the contract types and `IDocumentRenderer.RenderBadgeSheet`. Verify: the tests and the
  architecture tests pass.
- [x] 2.3 Write PdfPig tests for `PdfBadgeSheetWriter`, then implement the writer (design D2):
  - A4 portrait;
  - 2 × 5 cards of 85.60 × 53.98 mm (± 0.05 mm) at the computed positions;
  - words, photo and logo at least 3 mm inside each card;
  - crop marks on the cutting lines only, also on a partly empty last page;
  - one image per photo plus the logo per card;
  - the empty photo frame and the empty field lines;
  - a 100-character surname printed whole;
  - 11 badges on 2 pages;
  - metadata without personal data.

  Verify: the tests pass twice with identical bytes.
- [x] 2.4 Write golden-file tests for a synthetic badge sheet, then confirm the writer passes them:
  - with and without logo;
  - a missing photo and a pending license;
  - accented and Valencian names;
  - generated images only.

  Verify: the golden files pass twice, and contain no real data.
- [x] 2.5 Move the Federation names out of `DistributionTexts` into `Exports.Contracts.FederationNames`
  (design D5), with no behaviour change. Verify: the distribution golden files are unchanged.
- [x] 2.6 Write Testcontainers + MinIO tests for `IIdPhotoReader.ReadAsync` (design D3):
  - it returns the JPEG, or null without an ID photo;
  - a photo replaced between the reference read and the object read is retried;
  - a missing object gives null and a Warning log;
  - `StorageUnavailableException` when the storage is down.

  Then extract the shared reader from `ArquebusierPhotoAdministration` and implement the contract.
  Add the Admin-only exception to `FindManyAsync`'s XML doc. Verify: the new tests and every
  existing photo test pass.
- [x] 2.7 Update the modules README: the badge-sheet rendering contract, `IIdPhotoReader` (unscoped,
  Admin-only callers), JPEG `DocumentImage` and `FederationNames`. Verify: the README matches the
  code.
- [x] 2.8 Review group 2 in parallel with `csharp-reviewer`, `type-design-analyzer` and
  `security-reviewer` (photo access contract). Fix CRITICAL/HIGH findings.

## 3. Badges module and endpoint

- [x] 3.1 Create `PolvorApp.Badges` and `.Contracts` (design D1):
  - `BadgesModule`, the coded enum `BadgeLanguage` and the problem codes;
  - the audit action `BadgesDownloaded`, registered.

  Register the module in `Program.cs`, `PolvorApp.slnx` and the `Dockerfile` restore layer. Add an
  architecture test that only the registry and the badges module reference `IIdPhotoReader` (group 2
  review). Verify: `dotnet build` passes, and `ModuleRegistrationTests`, `CodedEnumsTests` and the
  architecture tests pass.
- [x] 3.2 Write unit tests for `BadgeBatch` (design D4), then implement it:
  - exactly one of comparsa and selection (`batch`);
  - the language (`language`);
  - 1–200 ids (`arquebusierIds`);
  - duplicates removed;
  - ordering by comparsa name, then Spanish order of surname and name.

  Verify: the tests pass.
- [x] 3.3 Write unit tests for `BadgeTexts` and the badge builder, then implement them:
  - the spec's labels and header words in es-ES, ca-ES-valencia and en, and the Federation name per
    language;
  - expiry as `dd/MM/yyyy` from an issued license, an empty line for pending or none, an expired
    date printed;
  - names, comparsa and identifiers untranslated;
  - no other personal data.

  Verify: the tests pass.
- [x] 3.4 Write Testcontainers + MinIO endpoint tests for `POST /api/badges/sheet` (spec: Badge
  batches, Badge language, Badge access and document handling):
  - Admin gets the PDF; FiringChief `403`; no session `401`;
  - comparsa batch with `ACTIVE` and `RESERVE`, including an inactive comparsa;
  - selection across comparsas;
  - errors: `400` per field and `arquebusierIds[i]` for a deleted arquebusier, `404 badges.notFound`,
    `409 badges.nothingToPrint` and `badges.tooMany` (201 arquebusiers);
  - photos scaled to at most 300 × 400 in the PDF;
  - `503 storage.unavailable` for the logo and for a photo;
  - the file names of both batch kinds;
  - `no-store`;
  - `429` under the `Exports` limit.

  Then implement the endpoint, the photo scaling and the read concurrency (design D3, D6). Verify:
  the tests pass.
- [x] 3.5 Write tests for the audit (design D7), then implement it:
  - one `BadgesDownloaded` entry per file, with the batch, comparsa, language, count and ids, and no
    names or DNI/NIE;
  - none for a refusal;
  - `503 badges.auditUnavailable` and no file when the audit fails.

  Also test `503 badges.busy` when both document slots stay taken. Verify: the tests pass.
- [x] 3.6 Regenerate `contracts/openapi.json` and check `OpenApiDocumentTests`. Add the route to the
  Admin-only route guard test if one lists them. Verify: the tests pass and the diff is additive.
- [x] 3.7 Update the docs:
  - `docs/data-model.md` §4: the batches, reserves included, the 200 cap, the empty frame and line,
    the language choice, the layout constants;
  - `docs/use-cases.md`: UC-30 notes;
  - `docs/open-questions.md`: the interim answer to Q-49 and the maintainer decisions dated
    2026-10-04;
  - `docs/compliance.md`: badges as a document (data, Admin only, audit, not stored; the privacy
    notice must mention them);
  - `docs/glossary.md`: `ArquebusierBadge` notes.

  Verify: the docs match the spec, and nothing from `docs/sources/` appears.
- [x] 3.8 Review group 3 in parallel with `csharp-reviewer`, `security-reviewer` (SEC-05/06,
  personal data in a document) and `silent-failure-hunter` (missing photos, storage and audit
  failures). Fix CRITICAL/HIGH findings.

## 4. Data table selection (design-system)

- [x] 4.1 Write Vitest + Testing Library + axe tests for the selection in `DataTable` (design-system
  delta), then implement it with TanStack Table's row selection:
  - a labelled checkbox per row and per phone item;
  - the header checkbox for the current page, mixed when partial;
  - the selection kept across pages, sorting and filtering through controlled state;
  - a checkbox click does not open the record;
  - the count announced;
  - the selected state shown beyond colour;
  - tables without selection unchanged.

  Verify: the tests and axe pass, and the existing `DataTable` tests pass unchanged.
- [x] 4.2 Add the story and catalogue entry for the selectable table, the `ui` keys in the three
  locales, and the row selection note in `docs/design/patterns.md`. Verify: Storybook builds,
  `npm run check-i18n` passes, and the note matches the component.
- [x] 4.3 Review group 4 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH
  findings.

## 5. Badge screens

- [x] 5.1 Regenerate the API client and add the `badges` namespace in es-ES, ca-ES-valencia and en,
  registered in `i18n/index.ts` and `i18next.d.ts`, with the `audit:actions.BadgesDownloaded`
  label. Verify: `npm run typecheck`, `npm run check-i18n` and the audit label test pass.
- [x] 5.2 Write tests (MSW, axe) for `BadgeSheet`, then implement it (design D8):
  - the batch summary for a comparsa and for a selection;
  - the language defaulting to the user's;
  - the incomplete counts (no photo; no issued license) and the missing-logo warning;
  - the 100 % scale note;
  - the download through `apiDownloadPost` with the file name from the response;
  - the translated reasons for every problem code, with `badges.tooMany` suggesting a selection and
    `badges.photoUnreadable` naming the arquebusiers concerned (from the list rows) so their photo can
    be uploaded again or they can be left out.

  Verify: the tests and axe pass.
- [x] 5.3 Write tests for `ArquebusiersPage`, then implement them:
  - Admins see the checkboxes, the selection bar ("N selected", "Clear", "Print badges"), the 200
    limit with its reason, and the comparsa action when filtered by one comparsa with nothing
    selected;
  - a `400` on a deleted arquebusier removes it from the selection and says so;
  - FiringChiefs see no checkbox and no badge action;
  - the page works at 360 px without page-wide sideways scrolling.

  Verify: the tests and axe pass.
- [x] 5.4 Write tests for "Print badges" in `ComparsaDetailPage`'s header actions (Admins only, the
  sheet with that comparsa's counts, nothing for a comparsa without arquebusiers), then implement it. Update `docs/design/patterns.md` (badge
  sheet). Verify: the tests pass and the note matches the screen.
- [x] 5.5 Review group 5 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH
  findings.

## 6. End-to-end and verification

- [x] 6.1 Write `e2e/badges.spec.ts` on the seeded stack:
  - an Admin prints a seeded comparsa's badges from the comparsa page in `ca-ES-valencia`
    (Playwright `download`: file name pattern, `%PDF-` header);
  - an Admin selects two arquebusiers of different comparsas in the list across pages and prints
    them;
  - the seeded FiringChief sees no badge action;
  - axe on the list with selection and on the sheet.

  Verify: the spec passes on the compose stack, and the full suite passes twice in a row.
- [x] 6.2 Run `verification-loop`:
  - build, types and lint;
  - backend and frontend tests, with coverage of at least 80 % on the `Badges` module, the new
    `Exports` and registry contracts, `features/badges` and the `DataTable` selection;
  - a security grep: Admin-only route, no personal data in logs, audit data or file names,
    `no-store`, no image file added by the change;
  - a diff review.

  Run `e2e-runner` and `pr-test-analyzer` on the change. Verify: the PASS report, with the findings
  and follow-ups recorded in design.md. Add to `docs/mvp.md`'s manual regression: one sheet printed
  on cardstock at 100 %, measured with a ruler and tried in an ID-1 sleeve. `docs/mvp.md` marks #16
  done when the change is archived.
