# Tasks

## 1. Research

- [x] 1.1 Check the APIs used in design D2, D6 and D7 with Context7, and with `gh` for real-world usage:
  - .NET 10: `DateOnly.AddYears` and `AddMonths` on 29 February;
  - TanStack Query 5: invalidating several query keys by prefix, and `staleTime` with `refetchOnWindowFocus`;
  - React Router 8: `useSearchParams` with `replace`;
  - the lucide-react name of the column-chart icon;
  - native `<meter>` accessible naming and styling in Chromium, Firefox and WebKit.

  Record the versions and any workaround in design.md. Verify: design.md is updated and no open question is left that changes the tasks.

## 2. Backend: compliance module and warning rules

- [x] 2.1 Create `Modules/ComplianceInsights/PolvorApp.ComplianceInsights` and `.Contracts` (design D1):
  - add both to `PolvorApp.slnx` and the `backend/Dockerfile` restore layer;
  - add `ComplianceInsightsModule : IModule` with no DbContext, registered in `Program.cs`.

  Verify: the solution builds, `ModuleDependencyRules` and the solution-boundary architecture tests pass, and the API starts.
- [x] 2.2 Write table-driven unit tests for `ComplianceRules`, covering every boundary of the spec "Compliance warnings (BR-04)":
  - expired yesterday, expiring today, expiring in 12 months minus a day, expiring in exactly 12 months;
  - pending with and without photos, no license;
  - an issued license missing the front, the back or both photos;
  - no course;
  - the 18th birthday today versus tomorrow, and a birth on 29 February;
  - no ID photo;
  - the order of the warnings, and a compliant arquebusier.

  Then add `ComplianceWarning` (coded enum), `ComplianceFacts`, `IComplianceRules` and the singleton `ComplianceRules` (design D2). Verify: the tests and `CodedEnumsTests` pass.
- [x] 2.3 Document the warnings:
  - `docs/data-model.md`: a BR-04 note that the registry is evaluated today for `ACTIVE` and `RESERVE`, with entries on festival dates in #10, and the warnings added to the derived data in §5;
  - `docs/glossary.md`: `ComplianceWarning` and its codes, and "expiring soon" as less than 12 months;
  - `backend/src/Modules/README.md`: the new module, and its two contract directions;
  - `docs/open-questions.md`: a maintainer decision (Q-54) that arquebusiers must be of legal age (18), with no special authorisation for minors.

  Verify: the docs match the spec and name the codes exactly.
- [x] 2.4 Review group 2 in parallel with `csharp-reviewer` and `type-design-analyzer`. Fix CRITICAL/HIGH findings.

## 3. Backend: registry facts and warnings on the list and detail

- [x] 3.1 Write integration tests (PostgreSQL) for `IArquebusierFacts.ListAsync`:
  - `null` returns every comparsa, and a set of ids returns only those;
  - the photo flags and owned weapon model ids are correct;
  - the record type has no name, national ID, federation ID or contact property.

  Then add the contract to `ArquebusierRegistry.Contracts` and implement `RegistryArquebusierFacts` (design D3). Verify: the tests pass.
- [x] 3.2 Write endpoint tests for `GET /api/arquebusiers`:
  - rows carry `warnings` in rule order, with time frozen by `FakeTimeProvider`;
  - an expiring license still has `licenseStatus` `VALID`;
  - the JSON has no `birthDate`;
  - an arquebusier with warnings can still be edited (spec "Warnings never block").

  Then select the extra columns, add `RegistryCompliance.FactsOf` and `ArquebusierRowResponse.Warnings` (design D3, D4). Verify: the tests and the existing registry list tests pass.
- [x] 3.3 Write endpoint tests for `GET /api/arquebusiers/{id}`:
  - `warnings` and `age` are returned;
  - `age` changes on the birthday;
  - a photo upload that adds the ID photo removes `ID_PHOTO_MISSING` on the next read.

  Then extend `ArquebusierViews` and `ArquebusierResponse`. Verify: the tests and the existing detail tests pass.
- [x] 3.4 Regenerate `contracts/openapi.json` and the orval client. Verify: the generated-contract check in CI (`npm run api:check` or its equivalent) passes, and the frontend still type-checks.
- [x] 3.5 Review group 3 in parallel with `csharp-reviewer`, `database-reviewer` (the projection queries) and `security-reviewer` (data minimisation in the contract and the list). Fix CRITICAL/HIGH findings.

## 4. Backend: summary and statistics endpoints

- [x] 4.1 Write integration tests for `GET /api/compliance/summary`:
  - an Admin counts every comparsa, inactive ones included;
  - "Jefa Sintética Dos" counts only her comparsas;
  - an arquebusier with several warnings counts once in `withWarnings` and once per code;
  - every code is present with zeros;
  - a FiringChief without assignments gets all zeros;
  - a request signed out gets `401`.

  Then implement `ComplianceInsightsQueries.SummaryAsync` and the endpoint (design D4, D5). Verify: the tests pass.
- [x] 4.2 Write integration tests for `GET /api/compliance/statistics`:
  - totals and status;
  - gender;
  - the age-bracket boundaries on the 25th birthday;
  - course by gender;
  - the license states, with valid excluding expiring;
  - owned weapons by gender and by kind, with every kind present;
  - the per-comparsa rows, sorted, summing to the total, and absent for a single-comparsa FiringChief and with a `comparsaId` filter;
  - the `comparsaId` and `status` filters;
  - a foreign or unknown comparsa gives `404 compliance.comparsaNotFound`;
  - `status=INACTIVE` gives `400` naming `status`;
  - a request signed out gets `401`;
  - the JSON has no arquebusier names, ids, national IDs or birth dates.

  Then implement `StatisticsAsync`, the endpoint and `ComplianceProblems`. Verify: the tests pass.
- [x] 4.3 Write the cross-cutting tests:
  - the D3 consistency test: for the seeded data, the warnings of the registry list rows agree with the summary counts;
  - the audit table is unchanged after the summary and statistics requests;
  - the scope-guard tests list both new routes.

  Verify: the tests pass.
- [x] 4.4 Review group 4 in parallel with `csharp-reviewer`, `security-reviewer` (BR-12, 404 parity, aggregates only) and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 5. Synthetic seed

- [x] 5.1 Extend `RegistrySeederTests`:
  - every `ComplianceWarning` has at least one seeded arquebusier, evaluated with `IComplianceRules`;
  - arquebusier 14 has exactly `LICENSE_EXPIRING`, `COURSE_MISSING`, `UNDER_AGE` and `ID_PHOTO_MISSING`;
  - arquebusier 7 has `LICENSE_PHOTOS_MISSING`;
  - seeding twice still creates each record once.

  Then add arquebusier 14 (with an optional `AgeYears`) and the front-only license photo of arquebusier 7 (design D9). Verify: the tests pass, and `seed` on an existing development database adds the new records without duplicates.
- [x] 5.2 Review group 5 with `csharp-reviewer`. Check that no real data is used (SEC-11). Fix CRITICAL/HIGH findings.

## 6. Frontend: composites and status map

- [x] 6.1 Write tests for `Breakdown`:
  - row and column headers are associated;
  - counts and percentages are formatted in es-ES and en;
  - the meter is named by the percentage text;
  - a zero total renders no share column;
  - the table scrolls inside its own area at 360 px;
  - axe passes in both themes.

  Then implement `components/app/Breakdown.tsx` with the `globals.css` meter styling and its stories (design D7). Verify: the tests, lint (no `style`, no arbitrary values) and the Storybook build pass.
- [x] 6.2 Write tests for the `warning` status map: every code renders the warning tone and the `TriangleAlert` icon, and `LICENSE_EXPIRED` is never destructive. Then replace `LICENSE`, `COURSE` and `AGE` with the eight codes in `status.ts`, `ui.json` (three locales) and the stories. Run `npm run docs:design`. Verify: the tests pass and `docs/design/status.md` is regenerated.
- [x] 6.3 Write tests for `AppLayout`:
  - a navigation item with `count` and `countLabel` has the accessible name "Arquebusiers, 5 with warnings";
  - the badge is hidden from assistive technology;
  - no badge renders for 0.

  Then add `countLabel` (design D6). Verify: the tests and the existing `AppLayout` tests pass.
- [x] 6.4 Document `Breakdown` and the navigation count label in `docs/design/README.md`. Add the statistics layout to the dashboard template in `docs/design/patterns.md`. Verify: the docs name the props and the stories.
- [x] 6.5 Review group 6 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 7. Frontend: registry list and detail

- [x] 7.1 Write tests for `useArquebusierFilters` and the list page with MSW:
  - the expiring counter counts and filters the `LICENSE_EXPIRING` rows;
  - `license=EXPIRING` in the address is honoured;
  - the warning `FilterSelect` filters by a code and by `ANY`, and keeps it in the address;
  - the counters are in the order active, reserve, expiring, expired, pending, none;
  - an unknown `warning` value in the address is ignored;
  - the result count is announced.

  Then implement them (design D6). Verify: the tests pass.
- [x] 7.2 Write tests for the list rows:
  - an expiring license shows "Expiring soon" with its date;
  - the warnings column shows "2 warnings" with the names on desktop and mobile rows, and nothing without warnings.

  Then implement them in `useArquebusierColumns`. Verify: the tests pass and axe passes on the list.
- [x] 7.3 Write tests for `ComplianceWarnings` and the detail page:
  - each code's sentence, with the expiry date or the age, in the three languages;
  - nothing renders without warnings;
  - the header shows "Expiring soon";
  - after the course is saved, the refetched detail without `COURSE_MISSING` hides it.

  Then add `features/compliance-insights/components/ComplianceWarnings.tsx`, use it in `ArquebusierDetailPage`, and delete the local check in `ArquebusierSummary.tsx`. Verify: the tests pass.
- [x] 7.4 Add `invalidateInsights` and call it from `useRefreshArquebusier` and from the registry's other list invalidations: create, delete, transfer, photos and owned weapons. Write a test that a registry edit invalidates the summary and statistics queries. Verify: the test passes.
- [x] 7.5 Add the `registry` keys of design D8 in es-ES, ca-ES-valencia and en, and remove `detail.warnings.*`. Verify: `npm run check-i18n` and the translation completeness test pass.
- [x] 7.6 Review group 7 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 8. Frontend: dashboard, navigation and statistics

- [x] 8.1 Write tests for `DashboardPage` with MSW:
  - the figures come from the summary and link to the filtered list addresses;
  - eight warning figures, zeros included;
  - the attention banner, or "up to date" when nothing is pending;
  - the next-expiries table shows 10 of 12, sorted by expiry and then name, with "See all";
  - a FiringChief without assignments gets the empty state;
  - a failed summary shows the error with a working retry;
  - axe passes in both themes.

  Then implement the page, route it on the index, and delete `HomePage` with its keys (design D6). Verify: the tests pass and the platform "Home route" test still passes.
- [x] 8.2 Write tests for the navigation:
  - the Arquebusiers item shows the warning count with its accessible name;
  - there is no count at 0 or when the summary fails;
  - the count disappears after an invalidation returns 0;
  - the Statistics entry shows for both roles.

  Then add `useWarningCount`, `NavigationEntry.count` and the Statistics entry in `AppShell` and `navigation.ts`. Verify: the tests pass.
- [x] 8.3 Write tests for `StatisticsPage` with MSW:
  - `comparsaId` and `status` are kept in the address and survive a re-render;
  - no comparsa filter and no per-comparsa table for a single comparsa;
  - the per-comparsa names link to `/comparsas/:id`;
  - `NoMatches` with "Clear filters" when the filters match no arquebusier;
  - the empty state without assignments;
  - an error with retry;
  - axe passes, and nothing scrolls horizontally at 360 px.

  Then implement the page and route `/statistics` with its breadcrumb (design D6). Verify: the tests pass.
- [x] 8.4 Add the `insights` namespace and the `common` keys of design D8 in es-ES, ca-ES-valencia and en, and register the namespace in `src/i18n/index.ts` and `i18next.d.ts`. Verify: `npm run check-i18n` and the translation completeness test pass, and the screens show no raw keys in the three languages.
- [x] 8.5 Review group 8 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 9. End-to-end and verification

- [x] 9.1 Write `e2e/insights.spec.ts` against the seeded stack (design D11):
  - "Jefa Sintética Dos" sees dashboard figures scoped to her comparsas;
  - the `COURSE_MISSING` figure opens the filtered list;
  - "Arcabucera Sintética Catorce"'s detail lists four warnings;
  - recording her course removes `COURSE_MISSING` and updates the navigation count;
  - an Admin filters the statistics by comparsa and keeps the filter after a reload;
  - axe runs on the dashboard and the statistics in both themes, and at 360 px.

  Verify: the spec passes in the compose stack on Chromium, Firefox and WebKit.
- [x] 9.2 Update `docs/mvp.md` (the status of #7, and the "first year" statistic noted under #10) and `docs/compliance.md` (statistics are aggregates only, and viewing them is not audited). Verify: the documents match the specs.
- [x] 9.3 Run `verification-loop`:
  - build, types and lint;
  - backend and frontend tests, with coverage of at least 80 % on the new compliance code and the changed registry code;
  - a security grep: no personal data in logs or in the statistics payload, no real data in the seed;
  - a diff review.

  Run `e2e-runner` and `pr-test-analyzer` on the change. Verify: the PASS report, with the findings and follow-ups recorded in design.md.
