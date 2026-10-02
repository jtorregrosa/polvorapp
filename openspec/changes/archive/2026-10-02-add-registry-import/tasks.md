# Tasks

## 1. Research

- [x] 1.1 Check with Context7 (and `gh search code` for real-world usage) the APIs used in design D3, D4, D8 and D11:
  - ClosedXML 0.105: loading from a stream without recalculating, `CachedValue` and its `XLDataType` (number, text, date, error), date serials and the 1900 leap-year quirk, a list validation pointing to a hidden sheet, text and date number formats;
  - .NET 10 `ZipArchive`: reading `Length` and `CompressedLength` of entries without extracting;
  - ASP.NET Core: a binary file result described in OpenAPI so orval generates a `Blob` response;
  - `write-excel-file` 4 (Node): writing date, number and text cells into a buffer or file for Playwright `setInputFiles`.

  Record versions and any workaround in design.md. Verify: design.md updated, with no open question left.

## 2. Dependencies, uploads and rate limit

- [x] 2.1 Add `ClosedXML` 0.105.x to `backend/Directory.Packages.props` and reference it from `PolvorApp.ArquebusierRegistry`. Add it, with its license and transitive packages, to `docs/third-party-licenses.md`. Verify: `dotnet build` passes, the dependency scan in CI reports no new vulnerability, and the licences doc lists it.
- [x] 2.2 Write unit tests for `SharedKernel/Http/SpreadsheetUploads`:
  - a non-multipart body gives `required`;
  - a missing `file` part gives `required`;
  - an empty file gives `required`;
  - a part over 2 MB gives `tooLarge`;
  - a malformed body gives `required`;
  - the `comparsaId` form field is read.

  Then implement it, following `ImageUploads` (design D3). Verify: the tests pass.
- [x] 2.3 Add the `SpreadsheetImports` rate-limit policy (design D9). It is per user, a fixed window, 10 per minute by default, read from `RateLimits:SpreadsheetImports:PermitLimit`, and test hosts default it to 1000. Add `RATE_LIMIT_SPREADSHEET_IMPORTS_PER_MINUTE` to `compose.yaml` and `.env.example`, and document it in `docs/development.md`. Its throttling test lands with the preview endpoint in 5.3. Verify: the solution builds and the existing rate-limit tests pass.
- [x] 2.4 Review group 2 in parallel with `csharp-reviewer` and `security-reviewer`. Fix CRITICAL/HIGH findings.

## 3. Workbook reading

- [x] 3.1 Add the `ImportTemplateTexts` records (es-ES, ca-ES-valencia, en; design D8) with the sheet names, the column headers, the value labels of gender, status and license type, and the instruction texts (design D8). Write unit tests that:
  - every key exists in the three files;
  - no header or value label is empty;
  - no two columns share a normalised header in any culture.

  Verify: the tests pass.
- [x] 3.2 Write unit tests for the package guard (design D3):
  - a valid synthetic `.xlsx` passes;
  - non-zip bytes, a PDF renamed `.xlsx`, a macro-enabled package and a package without a workbook part are `invalid`;
  - a crafted zip whose entries decompress past 20 MB, and one whose headers understate the sizes, are `invalid`, and decompression stops at the limit;
  - a sheet part with a `DOCTYPE` is `invalid`;
  - a package with more than 200 entries is `invalid`.

  Then implement the guard. Verify: the tests pass.
- [x] 3.3 Write unit tests for header matching:
  - headers in each language, in any order, with different case, accents and spaces are recognised;
  - a missing required column gives `missingColumns` with its field keys;
  - a repeated column gives `duplicateColumns`;
  - unknown headers are listed as ignored;
  - a sheet with only a header gives `empty`;
  - 1001 data rows give `tooManyRows`, checked from the used range before reading the rows;
  - blank rows are skipped, and row numbers are the sheet's.

  Then implement `ImportWorkbookReader`. Verify: the tests pass.
- [x] 3.4 Write unit tests for `ImportCellReader` (design D4), covering every row of the conversion table:
  - date cells, `dd/mm/yyyy` and ISO text, and `05/13/1990` passing through as invalid;
  - number cells for `federationId` and `phone`, and a fraction being invalid;
  - DNI padding of `1234567L` to `01234567L`, a number-only DNI, and an NIE never padded;
  - enum labels in the three languages, codes, `A-PROF` and `A_PROF`, and an unknown label;
  - an error cell giving `cellError`;
  - a formula cell read by its cached value;
  - license assembly for none, pending, issued with the default expiry, a date without a type, and an expiry without an issue date.

  Then implement it, feeding `RegistryInput.Read` unchanged. Verify: the tests pass, and the existing `RegistryInputTests` and `NationalIdTests` pass unchanged.
- [x] 3.5 Add `ImportWorkbookBuilder` to the test project. It builds synthetic workbooks in memory with ClosedXML, with synthetic names and DNIs computed from numbers (SEC-11). Make the tests of 3.2–3.4 use it. Verify: no test reads a workbook file from disk, and the tests pass.
- [x] 3.6 Review group 3 in parallel with `csharp-reviewer`, `security-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 4. Template

- [x] 4.1 Write unit tests for `ImportTemplateWriter` (design D8):
  - for each culture, the first sheet has the 13 headers in spec order, a frozen header row, text format on DNI/NIE and phone, and date format on the four date columns;
  - list validations cover rows 2–1001 for gender, status and license type;
  - the instructions sheet lists every column and the limits;
  - there are no data rows and no formulas outside the validations;
  - reading the generated template back with `ImportWorkbookReader` recognises every column and reports `empty`.

  Then implement it. Verify: the tests pass.
- [x] 4.2 Write endpoint tests for `GET /api/arquebusiers/import/template`:
  - an Admin gets `200` with the xlsx content type and file name;
  - `Accept-Language: ca-ES-valencia` gives Valencian headers, and `en` English ones;
  - a FiringChief gets `403`;
  - signed out gives `401`;
  - no audit entry is recorded.

  Then add the Admin-only `/import` sub-group and the endpoint, and list the route in `RegistryScopeGuardTests` (design D2). Verify: the tests pass.
- [x] 4.3 Review group 4 with `csharp-reviewer`. Fix CRITICAL/HIGH findings.

## 5. Validation report and preview

- [x] 5.1 Write integration tests (PostgreSQL) for the duplicate checks (design D5):
  - two rows with the same `nationalId` both get `duplicateInFile`, and the same for `federationId`;
  - a row whose `nationalId` exists in another comparsa gets `taken`, and the report carries no data of the other arquebusier;
  - one query serves 1000 rows.

  Then implement the batch duplicate query. Verify: the tests pass.
- [x] 5.2 Write unit tests for the report (design D6):
  - the counts;
  - only rows with problems are listed;
  - the field keys and reasons match the registration's;
  - a 17-year-old with an expired license and no course lists `LICENSE_EXPIRED`, `COURSE_MISSING` and `UNDER_AGE` in rule order, without the photo warnings;
  - a row with errors lists no warnings;
  - the reference date is today in Europe/Madrid, using a fake `TimeProvider`.

  Then build `ArquebusierImportReport`. Verify: the tests pass.
- [x] 5.3 Write endpoint tests for `POST /api/arquebusiers/import/preview`:
  - a clean file gives `200` with the report and stores nothing;
  - every file reason answers `400` with `errors.file` (and `columns` where it applies);
  - a missing `comparsaId` answers `400`, an unknown one `404 arquebusiers.comparsaNotFound`, and an inactive one `409 arquebusiers.comparsaInactive`;
  - a FiringChief gets `403` before the body is read, even for their own comparsa;
  - a request without the anti-forgery header answers `400 antiforgery.invalid`;
  - the response has `Cache-Control: no-store`;
  - no audit entry is recorded;
  - with `RateLimits:SpreadsheetImports:PermitLimit` at 2, the third preview answers `429`, and another Admin is unaffected;
  - the logs hold no names or national IDs for a file with errors;
  - with both workbook-reading slots taken, a preview answers `503 registry.busy` (design D9).

  Then implement the endpoint (design D2). Verify: the tests pass.
- [x] 5.4 Review group 5 in parallel with `csharp-reviewer`, `security-reviewer`, `database-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 6. Import and audit

- [x] 6.1 Write integration tests for `ArquebusierAdministration.ImportAsync` (design D5):
  - 40 clean rows create 40 arquebusiers of the comparsa, `ACTIVE` by default, with licenses and course as read, and without weapons or photos;
  - a FiringChief of that comparsa sees them;
  - a registration made between the preview and the import makes the import answer `400 arquebusierImport.rowErrors` with that row `taken`, and nothing is stored;
  - two parallel imports with the same `nationalId` leave at most one stored, and the other gets `400` or `409 arquebusierImport.conflict`;
  - importing the same file twice gives `400` with every row `taken`;
  - a lock timeout gives `503 registry.busy`.

  Then implement it. Verify: the tests pass.
- [x] 6.2 Write tests for the audit entries (design D7):
  - 3 imported arquebusiers give 3 `ArquebusierRegistered` entries with `source: import` and one `ArquebusiersImported` entry with the comparsa and `count: 3`;
  - no entry holds a name, nationalId, federationId, date, file name or row number;
  - a rejected import records nothing.

  Then record the entries. Verify: the tests and `AuditTrailRulesTests` pass.
- [x] 6.3 Write endpoint tests for `POST /api/arquebusiers/import`:
  - `200` with `comparsaId` and `importedCount`;
  - every failure in the design D2 table;
  - FiringChief `403`;
  - `no-store`;
  - the rate limit.

  Then add the endpoint, the problem codes and `RegistryScopeGuardTests` entries. Verify: the tests pass.
- [x] 6.4 Regenerate `contracts/openapi.json` and the orval client. Check that both POSTs generate a `FormData` body and that the template generates a `Blob` response. Document spreadsheet uploads (limits, zip guard, no storage) in `backend/src/Modules/README.md`. Verify: `OpenApiDocumentTests`, the contract check and `npm run typecheck` pass.
- [x] 6.5 Review group 6 in parallel with `csharp-reviewer`, `security-reviewer`, `database-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 7. Frontend: `FileField` composite

- [x] 7.1 Write Vitest tests for `FileField` (design D10):
  - the button opens the file chooser and the label names it;
  - a chosen file shows its name and size;
  - clearing it calls `onChange(null)`;
  - a wrong extension or type and a file over `maxBytes` show the translated error and do not call `onChange`;
  - the error is linked to the control for screen readers;
  - axe passes in both themes.

  Then implement it with stories (empty, chosen, error, disabled), add the `ui:fileField.*` keys in the three locales, and register it in the catalogue test. Verify: the tests, `npm run lint` and the Storybook axe checks pass.
- [x] 7.2 Document `FileField` in `docs/design/README.md`. Verify: the doc names its props and stories.
- [x] 7.3 Review group 7 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 8. Frontend: import page and translations

- [x] 8.1 Write tests for the Arquebusiers page and the routing:
  - an Admin sees the "Import" action and a FiringChief does not;
  - a FiringChief opening `/arquebusiers/import` sees the "not allowed" page.

  Then add the route and the action. Verify: the tests pass.
- [x] 8.2 Write tests for `ArquebusierImportPage` with MSW:
  - only active comparsas are offered;
  - the template download calls the API with the UI language and saves the file;
  - "Check file" is disabled until a comparsa and a file are chosen;
  - a clean report enables the import, whose confirmation names the comparsa and the count;
  - a report with errors lists every problem in words with its column header and keeps the import disabled;
  - "Only errors" filters the rows;
  - warnings allow the import;
  - each file reason shows its message, including the missing columns by header;
  - changing the comparsa or the file discards the report;
  - a `400 arquebusierImport.rowErrors` replaces the report;
  - a `409 arquebusierImport.conflict` asks to check again;
  - `429` and `503` show their messages;
  - success navigates to the list filtered by the comparsa and announces the count, after invalidating the list, the compliance summary and the navigation count;
  - axe passes, and the layout works at 360 px.

  Then implement the page and the hooks (design D10). Verify: the tests pass.
- [x] 8.3 Add the `registry:import.*` keys and the new problem codes in `problems.ts` to es-ES, ca-ES-valencia and en. Verify: `npm run check-i18n` passes, and the page shows no raw keys in the three languages.
- [x] 8.4 Describe the import page pattern (check, report, confirm) in `docs/design/patterns.md`. Verify: the pattern names the composites it uses.
- [x] 8.5 Review group 8 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 9. End-to-end, documentation and verification

- [x] 9.1 Add `write-excel-file` as a frontend dev dependency and list it in `docs/third-party-licenses.md`. Write `e2e/registry-import.spec.ts` (design D11), which builds workbooks at run time with fresh synthetic identities and deletes the imported arquebusiers afterwards:
  - an Admin downloads the template;
  - a file with an invalid DNI and a repeated federationId shows both errors and cannot be imported;
  - a clean file with warnings is checked, confirmed and imported, the list opens filtered by the comparsa with the count announced, and an imported arquebusier's detail shows their license;
  - the seeded FiringChief sees no "Import" action and gets the "not allowed" page;
  - axe runs on the page with a report.

  Verify: the spec passes in the compose stack on desktop Chromium, and is skipped in the other projects.
- [x] 9.2 Update the docs:
  - `docs/use-cases.md`: UC-09 note (Admin, PolvorApp template, one comparsa per file, all-or-nothing, only new arquebusiers);
  - `docs/data-model.md`: import note, DNI padding on import;
  - `docs/compliance.md`: SEC-05 (preview and template not audited, import audited) and SEC-11 (real workbooks only in production);
  - `docs/mvp.md`: status of #8, set when the change is archived, as for the earlier changes;
  - the Purpose of `openspec/specs/arquebusier-registry/spec.md`, to mention UC-09 at archive.

  Verify: the documents match the specs, and no real name or ID appears.
- [x] 9.3 Run `verification-loop`: build, types, lint, backend and frontend tests with coverage of at least 80 % on the new import code, a security grep (no personal values in logs or audit data, no workbook files committed) and a diff review. Run `e2e-runner` and `pr-test-analyzer` on the change. Verify: the PASS report, with the findings and follow-ups recorded in design.md.
