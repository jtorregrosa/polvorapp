# Third-Party Licenses

> Register required by ADR-0010. Every **direct** dependency is listed with its license (rows are
> added in the same change that adds the dependency). A
> dependency whose license is not MIT-compatible (permissive) is added only after its terms are
> recorded below as acceptable for the Federation's use.

Permissive = MIT, Apache-2.0, BSD-2/3-Clause, ISC, 0BSD, MPL-2.0 (file-level, unmodified use),
PostgreSQL License. Transitive dependencies are covered by CI dependency review.

## Non-permissive or conditional licenses

| Component | License | Used for | Assessment |
|---|---|---|---|
| QuestPDF 2026.9.1 (in use since #12, `add-exports`) | [QuestPDF Community License v3.0](https://www.questpdf.com/license/community.html) (effective 2026-07-06) | Server-side PDF generation (ADR-0008) | **Acceptable.** Free for "charitable organisations, academic institutions, and open-source projects" and for businesses under USD 1,000,000 annual gross revenue. The Federation is a private non-profit association well under the threshold, and PolvorApp is open source. Not eligible: public-sector entities and publicly traded companies — a deployment operated by a public body (e.g. a town council) would need a paid license. Re-check on every major QuestPDF upgrade. Checked 2026-09-30; still the Community License v3.0 on 2026-10-03. The code sets `Settings.License = LicenseType.Community` (`PolvorApp.Exports/Writers/PdfSetup.cs`). |
| Bricolage Grotesque, Geist and Geist Mono fonts via `@fontsource-variable/bricolage-grotesque`, `@fontsource-variable/geist` and `@fontsource-variable/geist-mono` | [SIL Open Font License 1.1](https://openfontlicense.org/open-font-license-official-text/) | Display, UI and identifier typefaces, self-hosted (redesign-design-system, ADR-0013; they replace Inter) | **Acceptable.** OFL allows use, embedding and redistribution in software, including commercial; the fonts may not be sold on their own. Redistributed copies must carry the copyright notice and licence text, so the build ships them at `/licenses/bricolage-grotesque-OFL.txt`, `/licenses/geist-OFL.txt` and `/licenses/geist-mono-OFL.txt` (`frontend/public/licenses/`). Shipped unmodified. Checked 2026-10-01. |
| Geist (`Geist-Regular.ttf`, `Geist-Bold.ttf`) from the `geist` npm package 1.7.2, embedded in the backend | [SIL Open Font License 1.1](https://openfontlicense.org/open-font-license-official-text/) | The PDF exports' font (`add-exports`, design D4): the runtime image has no system fonts | **Acceptable.** Same license as the frontend's Geist. Embedded unmodified as resources of `PolvorApp.Exports`, with the copyright notice and license text next to them (`Fonts/Geist-OFL.txt`, copied to the build output). Checked 2026-10-03. |
| MinIO via `cgr.dev/chainguard/minio` | [AGPL-3.0](https://github.com/minio/minio/blob/master/LICENSE) | Local development S3 emulator only (ADR-0005/0006) | **Acceptable.** Runs unmodified as a separate container in local/CI environments; not distributed with PolvorApp nor linked into it. Production uses any S3-compatible provider. Checked 2026-09-30. |

## Data files

| Data | Source and license | Use |
|---|---|---|
| Common-password list (`backend/src/Modules/IdentityAccess/PolvorApp.IdentityAccess/Resources/CommonPasswords.txt`, 46,146 entries of 12–128 characters, lower-cased) | [SecLists](https://github.com/danielmiessler/SecLists) `Passwords/Common-Credentials/xato-net-10-million-passwords-1000000.txt` at commit `e749176`, MIT; notice shipped in `CommonPasswords.NOTICE.txt` next to it | Password policy rejects common passwords (add-identity-access) |

## Container images

| Image | License | Use |
|---|---|---|
| `mcr.microsoft.com/dotnet/sdk`, `aspnet` | MIT (.NET); Ubuntu packages under their own licenses | API build and runtime |
| `postgres` | PostgreSQL License | Database (local/CI) |
| `nginxinc/nginx-unprivileged` | BSD-2-Clause (nginx) | Web entry point |
| `axllent/mailpit` | MIT | Local mail catcher |
| `node` | MIT | Frontend build stage |

## Backend (NuGet, direct)

| Package | License | Scope |
|---|---|---|
| `ClosedXML` | MIT (transitive: `DocumentFormat.OpenXml` MIT, `ClosedXML.Parser` MIT, `ExcelNumberFormat` MIT, `RBush.Signed` MIT, `SixLabors.Fonts` 1.0 Apache-2.0) | runtime (spreadsheet import and template, ADR-0008). Upgrade only to a release that keeps `SixLabors.Fonts` 1.x: 2.x is under the Six Labors Split License and needs an assessment first |
| `EFCore.NamingConventions` | Apache-2.0 | runtime |
| `MailKit` | MIT | runtime |
| `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` | MIT | runtime |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | MIT | runtime |
| `Microsoft.AspNetCore.OpenApi` | MIT | runtime |
| `Microsoft.EntityFrameworkCore` | MIT | runtime |
| `Microsoft.EntityFrameworkCore.Design` | MIT | build |
| `Microsoft.EntityFrameworkCore.Relational` | MIT | runtime |
| `Microsoft.Extensions.ApiDescription.Server` | MIT | build |
| `AWSSDK.S3` | Apache-2.0 | runtime (object storage, ADR-0005) |
| `Npgsql` | PostgreSQL | runtime |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSQL | runtime |
| `QuestPDF` | QuestPDF Community License v3.0 (see above) | runtime (PDF exports, ADR-0008) |
| `SkiaSharp`, `SkiaSharp.NativeAssets.Linux.NoDependencies` | MIT (bundled native Skia: BSD-3-Clause; FreeType: FreeType License) | runtime (photo processing, ADR-0005) |
| `dotnet-ef` (local tool) | MIT | build |
| `coverlet.MTP` | MIT | test |
| `Microsoft.AspNetCore.Mvc.Testing` | MIT | test |
| `Microsoft.Extensions.TimeProvider.Testing` | MIT | test |
| `PdfPig` | Apache-2.0 | test (reads the PDF exports' text for the golden-file tests) |
| `Testcontainers.Minio` | MIT | test |
| `Testcontainers.PostgreSql` | MIT | test |
| `xunit.v3` | Apache-2.0 | test |

## Frontend (npm, direct)

MPL-2.0 packages (`axe-core`, `@axe-core/playwright`) are used unmodified in tests only.

| Package | License | Scope |
|---|---|---|
| `axe-core` | MPL-2.0 | dev |
| `@axe-core/playwright` | MPL-2.0 | dev |
| `class-variance-authority` | Apache-2.0 | runtime |
| `cn` | MIT | runtime |
| `eslint` | MIT | dev |
| `eslint-config-prettier` | MIT | dev |
| `eslint-plugin-better-tailwindcss` | MIT | dev |
| `eslint-plugin-i18next` | ISC | dev |
| `eslint-plugin-jsx-a11y` | MIT | dev |
| `eslint-plugin-react-hooks` | MIT | dev |
| `eslint-plugin-react-refresh` | MIT | dev |
| `@eslint/js` | MIT | dev |
| `@fontsource-variable/bricolage-grotesque` | OFL-1.1 | runtime |
| `@fontsource-variable/geist` | OFL-1.1 | runtime |
| `@fontsource-variable/geist-mono` | OFL-1.1 | runtime |
| `globals` | MIT | dev |
| `@hookform/resolvers` | MIT | runtime |
| `i18next` | MIT | runtime |
| `i18next-browser-languagedetector` | MIT | runtime |
| `jsdom` | MIT | dev |
| `lucide-react` | ISC | runtime |
| `msw` | MIT | dev |
| `orval` | MIT | dev |
| `otpauth` | MIT | dev |
| `@playwright/test` | Apache-2.0 | dev |
| `prettier` | MIT | dev |
| `prettier-plugin-tailwindcss` | MIT | dev |
| `qrcode` | MIT | runtime |
| `radix-ui` | MIT | runtime |
| `react` | MIT | runtime |
| `react-dom` | MIT | runtime |
| `react-hook-form` | MIT | runtime |
| `react-i18next` | MIT | runtime |
| `react-image-crop` | ISC | runtime (photo cropping) |
| `react-router` | MIT | runtime |
| `storybook` | MIT | dev |
| `@storybook/addon-a11y` | MIT | dev |
| `@storybook/react-vite` | MIT | dev |
| `tailwindcss` | MIT | dev |
| `@tailwindcss/vite` | MIT | dev |
| `@tanstack/react-query` | MIT | runtime |
| `@tanstack/react-table` | MIT | runtime |
| `@testing-library/dom` | MIT | dev |
| `@testing-library/jest-dom` | MIT | dev |
| `@testing-library/react` | MIT | dev |
| `@testing-library/user-event` | MIT | dev |
| `tw-animate-css` | MIT | dev |
| `@types/node` | MIT | dev |
| `@types/qrcode` | MIT | dev |
| `@types/react` | MIT | dev |
| `@types/react-dom` | MIT | dev |
| `typescript` | Apache-2.0 | dev |
| `typescript-eslint` | MIT | dev |
| `vite` | MIT | dev |
| `vite-plugin-pwa` | MIT | dev |
| `@vite-pwa/assets-generator` | MIT | dev |
| `@vitejs/plugin-react` | MIT | dev |
| `vitest` | MIT | dev |
| `write-excel-file` | MIT (dependency `fflate` MIT) | dev (E2E: synthetic import workbooks built at run time) |
| `@vitest/coverage-v8` | MIT | dev |
| `zod` | MIT | runtime |
