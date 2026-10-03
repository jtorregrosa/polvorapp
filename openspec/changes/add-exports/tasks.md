# Tasks

## 1. Research

- [x] 1.1 Check with Context7 (and `gh search code` for real-world usage) what design D4–D10 rely on:
  - QuestPDF (current version): `Settings.License`, `FontManager.RegisterFontFromEmbeddedResource`, tables with a repeated header, page numbers, landscape pages, and that the native asset for `linux-x64` is in the `dotnet publish` output of the chiseled image;
  - ClosedXML 0.105: typed cells, date number formats, merged notice row, frozen header, auto-filter, and fixed column widths without font measurement;
  - PdfPig: extracting a PDF's text in reading order for the golden-file tests;
  - ASP.NET Core 10: returning a file with `TypedResults.File` and a rate-limit policy on a group.

  Record versions, licenses (QuestPDF Community, PdfPig Apache-2.0, Geist OFL 1.1) and any workaround in design.md. Verify: design.md updated, with no open question left beyond Q-44.

## 2. Contracts in other modules

- [x] 2.1 Write tests that the registry's license facts carry the license type (`AE`, `A_PROF`) for pending and issued licenses, through `IArquebusierRoster.FindManyAsync` and `IArquebusierFacts`. Then add `LicenseType Type` to `ArquebusierLicenseFacts.Pending` and `Issued` (design D2). Verify: the new tests and the existing registry and compliance tests pass.
- [x] 2.2 Write Testcontainers tests for `IOrderExports` (design D2): `ListValidatedAsync` returns only the `VALIDATED` orders of the edition; `FindAsync` returns one comparsa's order in each status, and null without an order; each entry with its copy, owned weapon copy, rental model, flask and loan copy; an entry of a deleted arquebusier with a null id and its copy; `ToString()` prints no personal data. Then implement it in `ComparsaOrders`. Verify: the tests pass.
- [x] 2.3 Write tests for `IAuditLog.RecordAsync` (design D2): it stores one entry with the actor, action, entity and data in its own transaction, and a failure throws. Then add it to `SharedKernel.Auditing` and implement it in `AuditPrivacy`. Document it in `backend/src/Modules/README.md` as the exception to auditing in the caller's transaction. Verify: the tests pass.
- [x] 2.4 Add the `Exports` rate-limit policy (design D8) with its configuration, `.env.example`, compose and CI E2E setting; its `429` is tested end to end with the endpoints (5.4). Verify: `ConfigurationValidationTests` pass.
- [x] 2.5 Review group 2 in parallel with `csharp-reviewer`, `database-reviewer` and `security-reviewer`. Fix CRITICAL/HIGH findings.

## 3. Module and definitions

- [x] 3.1 Create `PolvorApp.Exports` and `.Contracts` (design D1): `ExportsModule`, `ExportTable`, `ExportColumn`, `IExportDefinition`, `ExportAudience`, the coded enum `ExportFormat` (`XLSX`, `PDF`) and `ExportTexts` in three languages (design D6). Register the module in `Program.cs`, `PolvorApp.slnx` and the `Dockerfile`. Verify: `dotnet build` passes, and the architecture tests, `ModuleRegistrationTests` and `CodedEnumsTests` pass.
- [x] 3.2 Write unit tests for `PowderSupplierExport`: one row per comparsa with a validated order, kg and caps by type, the total row, Spanish order of comparsas, an empty edition, and no personal data in any cell. Then implement it. Verify: the tests pass.
- [x] 3.3 Write unit tests for `RentalCompanyExport`: rows only for weapon or flask rentals, the model label and flask size words, empty cells for what is not rented, sorting by comparsa and person, entries no longer in the registry from their copy. Then implement it. Verify: the tests pass.
- [x] 3.4 Write unit tests for `ArmsAuthorityExport`: rows only for `ACTIVE` entries with a weapon; owned (number, guide), rental (model, empty number) and loan (lender's name and DNI/NIE and the weapon's copy, registered and external); license type and expiry from the registry; empty license columns for an entry no longer in the registry. Then implement it. Verify: the tests pass.
- [x] 3.5 Write unit tests for `ComparsaListExport`: every entry of the comparsa's order with its columns, the order's totals and status, in each of the three languages; the draft notice for `DRAFT`, `SUBMITTED` and `RETURNED` orders and none for a `VALIDATED` one. Then implement it. Verify: the tests pass.
- [x] 3.6 Review group 3 in parallel with `csharp-reviewer`, `type-design-analyzer` and `security-reviewer` (SEC-06 per definition). Fix CRITICAL/HIGH findings.

## 4. Writers and golden files

- [x] 4.1 Add Geist regular and bold (OFL 1.1) as embedded resources with their license text, and the QuestPDF package to `Directory.Packages.props` and the module; set the Community license and register the fonts once at startup (design D4). Add PdfPig to the test project. Update `docs/third-party-licenses.md` (QuestPDF in use, PdfPig, Geist in the backend). Verify: `dotnet build` passes and a smoke test renders a one-page PDF.
- [x] 4.2 Write golden-file tests for `XlsxExportWriter`: for each definition built from a synthetic data set, the workbook read back into a text grid (notice, version, header, typed cells, total row, date format) equals its committed `.golden.txt`. Then implement the writer. Verify: the tests pass.
- [x] 4.3 Write golden-file tests for `PdfExportWriter`: for each definition, the PDF's extracted text equals its committed `.golden.txt` (header repeated on every page of a two-page table, page numbers, landscape above six columns, provisional notice). Fix the date with `TimeProvider`. Then implement the writer. Verify: the tests pass, twice in a row with identical output.
- [x] 4.4 Review group 4 in parallel with `csharp-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 5. API

- [x] 5.1 Write Testcontainers endpoint tests for `GET /api/exports/editions/{editionId}` (design D5): the four definitions with name, version, provisional flag and audience for an Admin; `403` for a FiringChief. Then implement it. Verify: the tests pass.
- [x] 5.2 Write endpoint tests for the recipient exports: an Admin gets each definition in `xlsx` and `pdf` with its content type, file name and `no-store`; a FiringChief gets `403`; an unknown definition or format and an unknown edition get `404`; an edition without validated orders gets a file with no rows. Then implement them. Verify: the tests pass.
- [x] 5.3 Write endpoint tests for the comparsa list: an Admin for any comparsa; a FiringChief for their comparsa; `404` for another comparsa and for a draft edition for a FiringChief; a draft file (`-draft` name, draft notice) for a `DRAFT`, `SUBMITTED` or `RETURNED` order and a final one for a `VALIDATED` order; `409 exports.notPrepared` for a comparsa without an order; the file in the request's language. Then implement it. Verify: the tests pass.
- [x] 5.4 Write tests that each returned export records one `ExportDownloaded` audit entry (design D7) with no personal data, that a refused request records none, that an audit failure returns `503` and no file, and that the `Exports` rate limit answers `429`. Then implement them. Verify: the tests pass.
- [x] 5.5 Regenerate `contracts/openapi.json` and check `OpenApiDocumentTests`: the new routes, the file responses and the problem codes. Verify: the tests pass and the diff is additive.
- [x] 5.6 Update the docs: `docs/data-model.md` (export definitions, provisional versions), `docs/use-cases.md` (UC-17 notes with the maintainer decisions), `docs/compliance.md` (SEC-06 columns per recipient, audit of exports), `docs/development.md` (`RATE_LIMIT_EXPORTS_PER_MINUTE` in the E2E command). Verify: the docs match the spec, and no real data from `docs/sources/` appears.
- [x] 5.7 Review group 5 in parallel with `csharp-reviewer` and `security-reviewer` (permissions, scope, `no-store`, audit, rate limit). Fix CRITICAL/HIGH findings.

## 6. Frontend foundations

- [x] 6.1 Regenerate the API client and add the `exports` namespace in `es-ES`, `ca-ES-valencia` and `en`, registered in `i18n/index.ts` (design D9). Verify: `npm run typecheck` and `npm run check-i18n` pass.
- [x] 6.2 Write tests (Vitest + Testing Library + axe) for `DownloadButtons`: "Excel" and "PDF" buttons named after what they download (and "draft" when the list is one), `pending` while downloading, the file saved with the server's name, and the translated error for `exports.notPrepared`, `429` and a network failure. Then implement it. Verify: the tests pass, and axe passes.
- [x] 6.3 Review group 6 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 7. Exports screens

- [x] 7.1 Write tests for `ExportsPage`: the provisional notice; the warning listing the comparsas not validated by status; each recipient export with its contents and its buttons; the comparsas with an order and their buttons, marked as a draft while not validated; the not-found page for a FiringChief; a 360 px layout without sideways scrolling of the page. Then implement it and its route. Verify: the tests pass, and axe passes.
- [x] 7.2 Write tests for the "Exports" link in the Admin's orders overview (absent for a FiringChief) and for "Download list" on the order page (any status, saying "draft" while not `VALIDATED`, for Admins and the comparsa's FiringChiefs). Then implement them. Verify: the tests pass, and axe passes.
- [x] 7.3 Review group 7 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 8. End-to-end and verification

- [x] 8.1 Write `e2e/exports.spec.ts` on the seeded stack: the Admin opens the exports of the current edition, sees the provisional notice and the comparsas not validated, and downloads the Arms Authority export as Excel and as PDF (Playwright `download`: file name, non-empty, PDF header); axe on the page. The seeded FiringChief downloads their list as a draft from the submitted order. Add to `e2e/serial-state/order-review.spec.ts` the FiringChief downloading their list, no longer a draft, once the order is validated. Raise `RATE_LIMIT_EXPORTS_PER_MINUTE` for E2E in CI and in `docs/development.md`. Verify: the specs pass in the compose stack, and the full suite passes twice in a row.
- [x] 8.2 Run `verification-loop`:
  - build, types and lint;
  - backend and frontend tests with coverage of at least 80 % on the `Exports` module and the changed contracts;
  - a security grep: no unauthenticated route, no personal data in logs, audit data or file names, `no-store` on every export;
  - a diff review.

  Run `e2e-runner` and `pr-test-analyzer` on the change. Verify: the PASS report, with the findings and follow-ups recorded in design.md. `docs/mvp.md` marks #12 done when the change is archived.
