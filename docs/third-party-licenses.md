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
| QuestPDF (planned, #12/#13) | [QuestPDF Community License v3.0](https://www.questpdf.com/license/community.html) (effective 2026-07-06) | Server-side PDF generation (ADR-0008) | **Acceptable.** Free for "charitable organisations, academic institutions, and open-source projects" and for businesses under USD 1,000,000 annual gross revenue. The Federation is a private non-profit association well under the threshold, and PolvorApp is open source. Not eligible: public-sector entities and publicly traded companies — a deployment operated by a public body (e.g. a town council) would need a paid license. Re-check on every major QuestPDF upgrade. Checked 2026-09-30. |
| MinIO via `cgr.dev/chainguard/minio` | [AGPL-3.0](https://github.com/minio/minio/blob/master/LICENSE) | Local development S3 emulator only (ADR-0005/0006) | **Acceptable.** Runs unmodified as a separate container in local/CI environments; not distributed with PolvorApp nor linked into it. Production uses any S3-compatible provider. Checked 2026-09-30. |

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
| `Microsoft.AspNetCore.OpenApi` | MIT | runtime |
| `Microsoft.Extensions.ApiDescription.Server` | MIT | build |
| `Npgsql` | PostgreSQL | runtime |
| `coverlet.MTP` | MIT | test |
| `Microsoft.AspNetCore.Mvc.Testing` | MIT | test |
| `Testcontainers.PostgreSql` | MIT | test |
| `xunit.v3` | Apache-2.0 | test |

## Frontend (npm, direct)

MPL-2.0 packages (`axe-core`, `@axe-core/playwright`) are used unmodified in tests only.

| Package | License | Scope |
|---|---|---|
| `@axe-core/playwright` | MPL-2.0 | dev |
| `@eslint/js` | MIT | dev |
| `@playwright/test` | Apache-2.0 | dev |
| `@tanstack/react-query` | MIT | runtime |
| `@testing-library/dom` | MIT | dev |
| `@testing-library/jest-dom` | MIT | dev |
| `@testing-library/react` | MIT | dev |
| `@testing-library/user-event` | MIT | dev |
| `@types/node` | MIT | dev |
| `@types/react` | MIT | dev |
| `@types/react-dom` | MIT | dev |
| `@vite-pwa/assets-generator` | MIT | dev |
| `@vitejs/plugin-react` | MIT | dev |
| `@vitest/coverage-v8` | MIT | dev |
| `axe-core` | MPL-2.0 | dev |
| `eslint` | MIT | dev |
| `eslint-config-prettier` | MIT | dev |
| `eslint-plugin-i18next` | ISC | dev |
| `eslint-plugin-jsx-a11y` | MIT | dev |
| `eslint-plugin-react-hooks` | MIT | dev |
| `eslint-plugin-react-refresh` | MIT | dev |
| `globals` | MIT | dev |
| `i18next` | MIT | runtime |
| `i18next-browser-languagedetector` | MIT | runtime |
| `jsdom` | MIT | dev |
| `msw` | MIT | dev |
| `orval` | MIT | dev |
| `prettier` | MIT | dev |
| `react` | MIT | runtime |
| `react-dom` | MIT | runtime |
| `react-i18next` | MIT | runtime |
| `react-router` | MIT | runtime |
| `typescript` | Apache-2.0 | dev |
| `typescript-eslint` | MIT | dev |
| `vite` | MIT | dev |
| `vite-plugin-pwa` | MIT | dev |
| `vitest` | MIT | dev |
