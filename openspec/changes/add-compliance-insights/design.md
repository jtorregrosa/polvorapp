# Design

## Context

See `proposal.md` for the motivation, and the delta specs (`compliance-insights`,
`arquebusier-registry`, `design-system`) for the behaviour. The current state that shapes this
design:

- **Registry module (#5, #6).**
  - The license is stored inline on `registry.arquebusiers` (`LicenseType`, `LicensePending`,
    `LicenseIssuedOn`, `LicenseExpiresOn`). Its status comes from `License.StatusOn(today)`, and
    "today" is `FederationCalendar.Today(TimeProvider)` (Europe/Madrid).
  - Photos live in `registry.arquebusier_photos`, one row per kind.
  - `ArquebusierQueries.ListAsync` filters in SQL through `IComparsaScope`, sorts about 800 rows in
    memory, and on purpose does not select the birth date.
  - `ArquebusierRowResponse` has no course, no birth date and no warnings.
  - `ArquebusierResponse` has the birth date, course and license, but no age and no warnings.
  - Age is computed nowhere.
- **Module boundaries** (ADR-0001, `backend/src/Modules/README.md`).
  - A module references only the SharedKernel and other modules' `.Contracts` projects.
  - A `.Contracts` project references only the SharedKernel.
  - Two modules may depend on each other's contracts. The precedent is
    `IdentityAccess.Contracts.IFiringChiefAssignmentSource`, which the catalogue implements.
  - `ArquebusierRegistry.Contracts` has only enums today, with no read contract.
- **Scope.** `IComparsaScope.GetAccessAsync` returns a `ComparsaAccess`: `All`, `None`, or `Only(ids)`
  with `Filter(...)` and `CanAccess(id)`. Out-of-scope data is answered `404`.
- **Frontend.**
  - `HomePage` (`features/platform`) is an interim grid of `LinkCard`s, marked for replacement by #7.
  - `useArquebusierFilters` computes five counters from the loaded rows and keeps `comparsaId`,
    `status` and `license` (`EXPIRED|PENDING|NONE`) in the address.
  - `ArquebusierSummary.tsx` derives a partial warning in the browser (license not `VALID`, no
    course).
  - `status.ts` already maps `license.EXPIRING` and `warning.{LICENSE, COURSE, AGE}`, used only by
    stories.
  - `AppLayout`'s `NavigationItem.count` renders a badge with no accessible text, and `NAVIGATION`
    entries carry no count.
  - `StatCard`, `StatFilter`, `FilterBar`, `FilterSelect`, `DataTable`, `SectionCard`,
    `AlertBanner` and `EmptyState` exist. `KeyFacts` already styles a native `<meter>`. There is no
    chart component or library.
  - ESLint rejects `style` attributes and arbitrary values.
- **Tests.**
  - The backend tests use xUnit v3, Testcontainers PostgreSQL, `ApiFactory`, `RegistryTestHost`
    with a `FakeTimeProvider`, `RegistryData` builders, and the scope-guard tests that list every
    scoped route.
  - The frontend uses Vitest, Testing Library, MSW and axe.
  - Playwright runs against the compose stack.

## Goals / Non-Goals

**Goals:**
- One owner for the warning rules (the `ComplianceInsights` module). It takes the reference date
  as a parameter, so #10 can evaluate edition entries on the festival dates with the same code.
- The registry list and detail, the dashboard, the navigation count and the statistics always
  agree for the same data on the same day.
- No new table, migration or dependency. Insights are derived on every request from the registry
  (the derived data of `docs/data-model.md` §5).

**Non-Goals:**
- No server-side paging or filtering for the arquebusier list. The new filters work on the loaded
  rows, as today (#6a design D10).
- No caching layer on the server and no materialised statistics. At about 800 rows, computing in
  memory is far within NFR-05.
- No change to permissions: reads only, scoped as today.

## Decisions

### D1. A `ComplianceInsights` module, with two contracts that point in opposite directions

New projects:

```
Modules/ComplianceInsights/PolvorApp.ComplianceInsights/            rules, summary, statistics, endpoints
Modules/ComplianceInsights/PolvorApp.ComplianceInsights.Contracts/  ComplianceWarning, ComplianceFacts, IComplianceRules
```

The dependencies are:
- `ArquebusierRegistry` (implementation) → `ComplianceInsights.Contracts`. The registry list and
  detail call `IComplianceRules` to attach warnings.
- `ComplianceInsights` (implementation) → `ArquebusierRegistry.Contracts`, for the new read
  contract `IArquebusierFacts` (D3).
- `ComplianceInsights` → `FederationCatalog.Contracts`, for comparsa names and weapon kinds.
- `ComplianceInsights` → `IdentityAccess.Contracts`, for `IComparsaScope`.

No project cycle exists, and the architecture tests stay as they are. The module has **no
DbContext and no schema**: it owns rules, not data. It is registered in `Program.cs`, `PolvorApp.slnx`
and the `backend/Dockerfile` restore layer, without a `MigrationOrder`.

*Alternatives considered*:
- **Rules inside the registry.** This is the simplest option, but it breaks "one capability, one
  module" (ADR-0001). The orders module (#10) would then depend on the registry for a
  compliance rule. The registry spec already says "compliance warnings belong to the compliance
  insights capability".
- **The frontend fetches warnings from compliance endpoints and merges them into registry
  pages.** This means two requests per page, a client-side join by id, and filters spread over two
  sources.
- **Rules in the SharedKernel.** That would put domain logic in the kernel, which only holds
  technical building blocks.

### D2. The rules (`ComplianceRules`, pure)

`ComplianceInsights.Contracts` holds:
- `ComplianceWarning`, a coded enum in rule order: `LICENSE_MISSING`, `LICENSE_PENDING`,
  `LICENSE_EXPIRED`, `LICENSE_EXPIRING`, `COURSE_MISSING`, `UNDER_AGE`, `ID_PHOTO_MISSING`,
  `LICENSE_PHOTOS_MISSING`. It uses `[JsonStringEnumMemberName]` and `CodeEnumConverter`, and
  `CodedEnumsTests` picks it up.
- `ComplianceFacts`: `BirthDate`, `License` (a `ComplianceLicense?`, null when there is no
  license), `TrainingCompletedOn?` and `HasIdPhoto`.
  - `ComplianceLicense` is a closed hierarchy with a private constructor:
    `ComplianceLicense.Pending`, which has no dates, and
    `ComplianceLicense.Issued(ExpiresOn, HasFrontPhoto, HasBackPhoto)`.
  - So a pending license can carry neither dates nor photo flags, which no rule reads, and every
    `with` expression yields a valid value (type-design review).
  - `ComplianceFacts.ToString()` prints no value, because the birth date is personal data.
  - Parity tests check the warnings against the registry's `License.StatusOn` around the expiry
    day.
- `IComplianceRules`, with `IReadOnlyList<ComplianceWarning> Evaluate(ComplianceFacts facts,
  DateOnly referenceDate)` and `int AgeOn(DateOnly birthDate, DateOnly referenceDate)`.

The implementation is an `internal sealed` singleton with no state. Its named constants are
`LegalAge = 18` and `ExpiringWindowMonths = 12`. Its rules:
- License:
  - no license gives `LICENSE_MISSING`;
  - a pending license gives `LICENSE_PENDING`;
  - `referenceDate > expiresOn` gives `LICENSE_EXPIRED`;
  - `expiresOn < referenceDate.AddMonths(12)` gives `LICENSE_EXPIRING`.

  These are exclusive, and they reuse the same comparison as `License.StatusOn`: valid through the
  expiry day.
- Age: `AgeOn` counts the whole years. Someone born on 29 February has their birthday on 28
  February in non-leap years (`DateOnly.AddYears` semantics). The same applies to the 12-month
  window (`AddMonths`).
- `LICENSE_PHOTOS_MISSING` requires an issued license.

Table-driven unit tests cover every boundary in the spec, plus 29 February.

### D3. Registry read contract `IArquebusierFacts`

The contract has two methods, so "every comparsa" is always an explicit choice and never a
forgotten `null`:
- `ListAllAsync(ct)`, called only when the caller's `ComparsaAccess` covers every comparsa;
- `ListAsync(IReadOnlyCollection<Guid> comparsaIds, ct)`, which returns nothing for an empty set.

Like `ICatalogDirectory`, it **applies no scope** itself, because a `.Contracts` project cannot
reference `IdentityAccess.Contracts` to take a `ComparsaAccess`. Its XML docs state the
obligations (security review):
- take the ids from `IComparsaScope`;
- check a user-chosen comparsa with `CanAccess` first;
- never return the facts to a client.

The ids are copied to an array, so Npgsql sends one `uuid[]` parameter whatever set the caller
holds (database review).

`ArquebusierFacts` has these fields:
- `ComparsaId`, `Status`, `Gender` and `BirthDate`;
- `License`: a closed `ArquebusierLicenseFacts` hierarchy, either `Pending` or
  `Issued(ExpiresOn, HasFrontPhoto, HasBackPhoto)`;
- `TrainingCompletedOn?` and `HasIdPhoto`;
- `OwnedWeaponModelIds`.

It carries **no identifier, name, national ID, federation ID, license type or contact data**: data
minimisation, and no consumer needs them. The statistics need gender and birth date only as
inputs to aggregates. The records print only their type name.

The contract runs one projection query, with an `Any` per photo kind as the list query does, and a
second query for owned weapons in the same comparsas. The database review found that the existing
indexes cover both: `ix_arquebusier_photos_arquebusier_id_kind`, `ix_owned_weapons_arquebusier_id`
and `ix_arquebusiers_comparsa_id`, so no migration is needed. It is implemented by the
`internal` `RegistryArquebusierFacts`.

`RegistryCompliance.LicenseOf` decides the license shape in **one** switch, used by the list, the
detail and the contract. It throws, with the arquebusier's id, on an issued license without expiry,
which `ck_arquebusiers_license` makes impossible. `FactsOf(id, birthDate, course, LicenseColumns,
PhotoFlags)` builds `ComplianceFacts` from it, taking small structs instead of eight positional
values. The compliance module maps the closed contract hierarchy with a total switch. The
contracts cannot share a type, because Registry.Contracts cannot reference
ComplianceInsights.Contracts. Two kinds of test guard the mapping:
- a registry test checks that list and detail agree for every license shape;
- the consistency test of task 4.3 checks list rows against the summary counts.

*Alternative*: have the registry return warnings inside `ArquebusierFacts`. This is impossible
without a contract-to-contract reference, which the architecture tests forbid.

### D4. API

All routes sit in the authenticated `/api` group. They have no role policy: the scope comes from
`IComparsaScope`. `no-store` comes from the platform.

Reads get no rate limit, as elsewhere. This is a deliberate waiver of the generic "rate limit every
endpoint" rule (security review), for three reasons:
- only signed-in Admins and FiringChiefs can call them;
- a request reads at most about 800 rows;
- the navigation's summary query is throttled by its `staleTime` of 30 s.

A per-user read policy can be added later without changing the API.

| Route | Name | Response |
|---|---|---|
| `GET /api/compliance/summary` | `GetComplianceSummary` | `ComplianceSummaryResponse(int Active, int Reserve, int WithWarnings, IReadOnlyList<WarningCountResponse> Warnings)`, with every code in rule order |
| `GET /api/compliance/statistics?comparsaId=&status=` | `GetComplianceStatistics` | `ComplianceStatisticsResponse` (below) |

`ComplianceStatisticsResponse` has these fields:
- `Total`, `Active` and `Reserve`;
- `Gender`, a `GenderCounts(Male, Female, Unspecified)`;
- `AgeBrackets`: a list of `AgeBracketCounts(AgeBracket Bracket, GenderCounts Counts)`.
  `AgeBracket` is a coded enum: `UNDER_25`, `FROM_25_TO_34`, `FROM_35_TO_44`, `FROM_45`;
- `Course`, a `CourseCounts(GenderCounts Done, GenderCounts NotDone)`;
- `Licenses`, a `LicenseStateCounts(Valid, Expiring, Expired, Pending, None)`. `Valid` excludes the
  expiring ones;
- `OwnedWeapons`, an `OwnedWeaponCounts(GenderCounts WithWeapon, GenderCounts WithoutWeapon,
  IReadOnlyList<WeaponKindCount> ByKind)`, with every `WeaponKind` included;
- `Comparsas`: a list of `ComparsaStatisticsResponse(Guid ComparsaId, string Name, int Total, int
  Active, int Reserve, GenderCounts Gender, int WithWarnings)`, sorted with `SpanishOrder`. It is
  empty when `comparsaId` is set or the scope holds a single comparsa.

Validation:
- `status` is parsed with `InputFields.OptionalCode`, and an invalid value answers `400` naming
  `status`;
- a `comparsaId` that is outside the scope, or that `ICatalogDirectory` does not know, answers
  `404 compliance.comparsaNotFound`.

The problem codes live in `ComplianceProblems`.

Registry changes, all additive:
- `ArquebusierRowResponse.Warnings`, an `IReadOnlyList<ComplianceWarning>`. The list query also
  selects the birth date, course date and license photo flags, to evaluate them, but does not
  return them.
- `ArquebusierResponse.Warnings` and `ArquebusierResponse.Age`. The age is derived by
  `IComplianceRules.AgeOn` and never stored, so the UI shows the age without computing it a second
  time.

After this, `contracts/openapi.json` and the orval client are regenerated.

### D5. Aggregation

`ComplianceInsightsQueries` handles a request in this order:
1. Read the access.
2. If `comparsaId` is set, check it with `CanAccess` and `FindComparsaAsync`.
3. Call `IArquebusierFacts.ListAsync`. It passes `null` for `All`, the ids for `Only`, and makes no
   call for `None`, which gives all zeros.
4. Filter by status in memory.
5. Evaluate the warnings once per arquebusier, with `today`.
6. Aggregate with LINQ.

Weapon kinds come from one `FindWeaponModelsAsync` over the distinct model ids, and comparsa names
from one `FindComparsasAsync`. So a request costs three queries at most, in memory over about 800
rows.

### D6. Frontend structure (`features/compliance-insights`)

- `pages/DashboardPage.tsx` replaces `features/platform/pages/HomePage.tsx` on the index route
  (the dashboard template).
  1. `PageHeader`.
  2. The figures section: `StatCard`s for active (`/arquebusiers?status=ACTIVE`), reserve and "with
     warnings" (`?warning=ANY`).
  3. An `AlertBanner`: warning tone with the count, or success when nothing is pending.
  4. The "Warnings" section: a grid of eight `StatCard`s, each linked to `?warning=<CODE>`.
  5. "Next license expiries": a `DataTable` of up to 10 rows, taken from the cached
     `useListArquebusiers()` rows that carry `LICENSE_EXPIRING`, sorted by `licenseExpiresOn` and
     then by Spanish name order. A "See all" link goes to `/arquebusiers?license=EXPIRING`.

  A FiringChief without assignments gets `EmptyState`, with the same text and test as the list.
  A failed summary shows an `AlertBanner` error with a retry `Button`.
- `pages/StatisticsPage.tsx` (`/statistics`) follows the dashboard template:
  - a `FilterBar` with a comparsa `FilterSelect`, shown when the scope has more than one comparsa
    and reusing the list's source for comparsa options, and a status `FilterSelect`. The address
    keeps `comparsaId` and `status`, through `lib/search-filters`;
  - `StatCard`s for total, active and reserve;
  - a `SectionGrid` of `SectionCard`s (with no edit action), each holding a `Breakdown`:
    - gender;
    - age brackets, with columns women, men, unspecified and total;
    - course, done and not done by gender;
    - license state;
    - owned weapons: with and without one by gender, and by kind, with the kind labels from the
      `catalog` namespace;
  - a `DataTable` per comparsa, whose names link to `/comparsas/:id`.

  When the filters match nothing, it shows `NoMatches` (`total === 0` with a filter set).
- `components/ComplianceWarnings.tsx` is used by the registry detail. It takes `warnings`,
  `license?.expiresOn` and `age`, and renders one `AlertBanner severity="warning"`: a list of
  translated sentences, plus the BR-04 hint. It replaces the browser-side check in
  `ArquebusierSummary.tsx`. Cross-feature imports are already used (e.g. `useSession`).
- `useWarningCount()` returns the summary's `withWarnings` (`staleTime` 30 s). `invalidateInsights(queryClient)`
  invalidates the summary and every statistics query.
  - Callers: the registry's `useRefreshArquebusier` and its other list invalidations, which cover
    create, edit, status, transfer, delete, photos and owned weapons. So the navigation count and
    the dashboard follow an edit (spec "Count follows a fix").
- Navigation:
  - `NavigationEntry` gains `count?: 'warnings'`;
  - `AppShell` resolves it with `useWarningCount()` into `NavigationItem.count` and a new
    `NavigationItem.countLabel`, a translated "{{count}} with warnings";
  - `AppLayout` renders the badge `aria-hidden` and the label as visually hidden text inside the
    link, so the accessible name is "Arquebusiers, 5 with warnings";
  - a new entry `{ to: '/statistics', labelKey: 'nav.statistics', icon: ChartColumn }` is added
    for every role.
- Registry list (`useArquebusierFilters`, `useArquebusierColumns`):
  - `LICENSE_FILTERS` gains `EXPIRING`, which matches rows whose `warnings` include
    `LICENSE_EXPIRING`;
  - a `warning` URL filter (`ANY` or a code) is set through a `FilterSelect` in the `FilterBar`;
  - the counters become active, reserve, expiring (tone `warning`), expired (`warning`), pending
    and none;
  - the license cell shows `StatusBadge license=EXPIRING` when that warning is present;
  - a new "Warnings" column shows "2 warnings" with the names as the secondary line, also in the
    mobile row, and an empty cell when there are none.
- Registry detail: the header license badge uses `EXPIRING` in the same way, and
  `ComplianceWarnings` replaces the local component.

### D7. The `Breakdown` composite

`components/app/Breakdown.tsx` takes these props:
- `title`;
- `total`;
- `columns?: { id; label }[]`;
- `rows: { id; label; counts: number[] }[]`;
- `showRowTotal?`.

It renders a `<table>` with a caption from `title`, `scope="col"` and `scope="row"` headers, and
right-aligned `tabular-nums` cells formatted with `lib/format`. The last column holds the share:
the percentage as text (`Intl.NumberFormat` with `style: 'percent'` and no decimals), then a
`<meter min=0 max=100 value=…>` capped at 100, styled like the `KeyFacts` meter in `globals.css`.
The meter is `aria-hidden` (accessibility review): the text in the same cell, with its row and
column headers, carries the share. Exposing the meter too would announce the share twice, and the
meter role sounds different in every screen reader. With `total === 0`, it drops the share column. The table sits in an `overflow-x-auto`
area, so a 360 px screen scrolls the breakdown, not the page.

It ships with a story (single column, gender columns, zero total), tests (headers association,
formatting in es-ES and en, zero total) and axe in both themes. `docs/design/README.md` lists it
under "Lists".

*Alternative*: a chart library (Recharts, visx). That is a new dependency that needs an ADR, it is
harder to make accessible, and tables with meters say the same thing.

### D8. Status map and i18n keys

- `status.ts`: `warning` becomes the eight codes, all with `tone: 'warning'` and the
  `TriangleAlert` icon. `LICENSE`, `COURSE` and `AGE` are replaced; only stories use them.
  `docs/design/status.md` is regenerated (`npm run docs:design`).
- `ui.json`:
  - `status.warning.<CODE>` (short names, e.g. "Expiring soon", "No course", "Under 18", "No ID
    photo", "License photos missing");
  - `breakdown.share` and `breakdown.total`.
- `common.json`:
  - `nav.statistics`;
  - `nav.warningCount_one` and `nav.warningCount_other`;
  - remove `home.sectionsTitle` and `home.sections.*` together with `HomePage`.
- New namespace `insights.json`, registered in `src/i18n/index.ts` and `i18next.d.ts` in the three
  locales:
  - `dashboard.{title, description, figuresTitle, active, reserve, withWarnings, attention_one/_other, upToDate, warningsTitle, expiries.{title, seeAll, empty, columns.{name, comparsa, expiresOn}}, noComparsa.{title, description}, loadError, retry}`;
  - `statistics.{title, description, filters.{comparsa, status, allComparsas, allStatuses}, figures.{total, active, reserve}, gender.{title, MALE, FEMALE, UNSPECIFIED}, ageBrackets.{title, UNDER_25, FROM_25_TO_34, FROM_35_TO_44, FROM_45}, course.{title, done, notDone}, licenses.{title, valid, expiring, expired, pending, none}, ownedWeapons.{title, withWeapon, withoutWeapon, byKindTitle}, comparsas.{title, columns.*}, noMatches, loadError, retry}`;
  - `warnings.{title, hint, LICENSE_MISSING, LICENSE_PENDING, LICENSE_EXPIRED ({{date}}), LICENSE_EXPIRING ({{date}}), COURSE_MISSING, UNDER_AGE ({{age}}), ID_PHOTO_MISSING, LICENSE_PHOTOS_MISSING}`.
- `registry.json`:
  - `arquebusiers.counters.expiring`;
  - `arquebusiers.filters.warning.{label, any}`;
  - `arquebusiers.columns.warnings`;
  - `arquebusiers.warningCount_one` and `arquebusiers.warningCount_other`;
  - remove `detail.warnings.*`, which moves to `insights:warnings`.

`check-i18n` and the translation completeness test cover all of these.

### D9. Synthetic seed

`RegistrySeeder` changes in two ways:
- It adds arquebusier **14** in Norte: "Arcabucera Sintética Catorce", female, `ACTIVE`, 16 years
  old (a new optional `AgeYears` on `ArquebusierSeed`). She has an AE license issued 4 years and
  9 months ago that expires in 3 months, both license photos, no course and no ID photo. Her warnings are
  `LICENSE_EXPIRING`, `COURSE_MISSING`, `UNDER_AGE` and `ID_PHOTO_MISSING`, which match the
  registry scenario "Every warning is listed".
- Arquebusier **7** gets a `LICENSE_FRONT` photo only, for `LICENSE_PHOTOS_MISSING`.

Both are additions with fixed ids, so existing development databases pick them up on the next
seed run. All dates stay relative to the run day. `RegistrySeederTests` assert that every warning
code has at least one seeded arquebusier, through `IComplianceRules`.

### D10. Security, GDPR and audit

- **BR-12.** Both endpoints read the scope first. `None` returns zeros without touching the
  registry. A foreign `comparsaId` answers `404`, the same as an unknown one, so the response never
  reveals whether a comparsa exists outside the caller's scope. The scope-guard tests list both
  routes.
- **Data minimisation.**
  - `IArquebusierFacts` carries no identifying fields.
  - The statistics return counts and comparsa names only, which also keeps gender to its
    equality-report purpose (`docs/compliance.md`).
  - The list response still omits the birth date.
  - The detail adds the derived age; it already returns the birth date.
- **SEC-05.** No audit entries: the endpoints neither write nor export. A test asserts that the
  audit table is unchanged after the requests (spec "Viewing records nothing").
- **Logging.** The new code logs no request data. Query parameters are a comparsa id and a status
  only.
- **SEC-06** (exports) does not apply, because there are no downloads.

### D11. Testing

- Backend:
  - unit tests for `ComplianceRules` (D2), table-driven;
  - integration tests (Testcontainers) for the summary and statistics: Admin, FiringChief with
    one and two comparsas, no assignments, unknown or foreign comparsa `404`, invalid status
    `400`, `401` signed out, brackets and kinds, the per-comparsa sums equal the total, and no
    audit entry;
  - registry list and detail warnings and `age`;
  - the consistency test of D3;
  - `ModuleDependencyRules` with the new projects.
- Frontend: Vitest, Testing Library, MSW and axe for:
  - `Breakdown`;
  - `ComplianceWarnings`;
  - `DashboardPage`: figures, links, top 10, up to date, no comparsa, error and retry;
  - `StatisticsPage`: filters in the address, single comparsa, no matches, comparsa links;
  - the registry list: the expiring counter, the warning filter, the warnings column;
  - the navigation count and its accessible name.
- E2E: `e2e/insights.spec.ts`, against the seeded stack:
  - the FiringChief's dashboard is scoped;
  - a figure opens the filtered list;
  - arquebusier 14's detail lists four warnings;
  - recording her course removes `COURSE_MISSING`, and the navigation count follows;
  - the statistics are filtered by comparsa and survive a reload;
  - axe runs in both themes and at 360 px.

### Research (task 1.1)

These versions were checked on 2026-10-02:
- **.NET 10.** `DateOnly.AddYears` and `AddMonths` clamp to the last day of the month, so 29
  February + 1 year gives 28 February. D2 relies on this, and the unit tests pin it.
- **TanStack Query 5.104.** `invalidateQueries({ queryKey })` matches by prefix
  (`partialMatchKey`, element by element) and refetches the active queries. Orval keys are the
  full path (`['/api/compliance/summary']` and `['/api/compliance/statistics', params?]`), so a
  shared string prefix would not match. `invalidateInsights` invalidates the summary key and the
  statistics key without params, which matches every filter combination. The unit test asserts
  it.
- **React Router 8.4.** `useSearchParams` with `{ replace: true }` is already used through
  `lib/search-filters`. The statistics filters reuse it.
- **lucide-react 1.49.** It exports `ChartColumn`, used for the Statistics navigation entry.
- **Native `<meter>`.**
  - `globals.css` already styles `meter[data-slot='meter']` for Chromium/WebKit
    (`::-webkit-meter-*`) and Firefox (`::-moz-meter-bar`), with tokens. `Breakdown` reuses the
    same slot.
  - The meter is named with `aria-label`: ARIA 1.2 role `meter`, the same pattern as `KeyFacts`.

There is no workaround and no open question.

## Review follow-ups

- **Group 4.** The statistics read only the filtered comparsa, take one reference date per request
  (the age is carried in `EvaluatedFacts`), and count genders, statuses and license states through
  exhaustive switches, so every breakdown adds up to the total. Tests pin this, and an allow-list
  test checks the response shape.
  - **Not done:** taking a `ComparsaAccess` in `IArquebusierFacts`. A `.Contracts` project cannot
    reference IdentityAccess.Contracts, so the obligation is documented in the contract and applied
    in one place, `ScopedFacts`.
- **Group 6.** The `Breakdown` meter is `aria-hidden`, because the share is text in the same
  cell, and it is capped at 100. `count` and `countLabel` are one typed pair, so a navigation count
  is never hidden from assistive technology without its label. `STATUS_MAP.warning` must be
  exactly `Record<ComplianceWarning, …>`.
- **Group 7.**
  - Warning names are joined with `Intl.ListFormat` ("A, B y C"), because screen readers read or
    skip " · " unpredictably.
  - Badges outside a table carry a visually hidden "Licencia: " prefix, and phone rows show the
    expiry date.
  - The detail warning message is a lasting state (`live={false}`) and writes dates out.
  - Rows without warnings say "Sin avisos".
  - A version conflict refreshes the list and the insights.
  - `useRefreshArquebusier` lets the insights refetch in the background, so a save never waits for
    them.
  - *Done after review, at the maintainer's request:* `StatFilter`'s pressed state shows a check
    and a ring besides its tint.
  - *Follow-up outside this change:* the counters' group name ("Resumen de arcabuceros") does not
    say that they filter.
- **Group 8.**
  - Both pages wait for a FiringChief's comparsas before showing figures, so an unassigned
    FiringChief never sees a flash; an Admin's dashboard does not ask for the comparsas.
  - A failed comparsas request on the statistics page shows its own retry, because a `comparsaId`
    in the address would otherwise leave the page blank.
  - The statistics keep the previous figures while new filters load (`keepPreviousData`, with
    `aria-busy`) and always announce "Mostrando N arcabuceros".
  - The dashboard's message on what needs attention comes first.
  - Share columns say what they are a share of ("% del total", "% de las armas"), and the bar is
    hidden below `sm`.
  - Phone rows of the per-comparsa table keep the gender figures.
  - The start page keeps `common:home.title`, and the unassigned state reuses the
    registry's texts, so D8's `dashboard.title`, `noComparsa` and `loadError` keys were not needed.
  - *Done after review, at the maintainer's request:*
    - `StatCard` links show the focus ring on the whole card, with a chevron and an underlined label
      on hover.
    - The start page's h1 is "Inicio" / "Inici" / "Home", gender-neutral and matching its navigation
      item.
  - *Follow-ups outside this change:*
    - The dashboard figures have no loading placeholder.
    - `LoadFailure` could move to `components/app`.
    - Response casts (`data.data as T`) could go through `responseData` as the query's `select`,
      in one sweep across the app.
- **Verification (task 9.3, 2026-10-02).** Overall result: ready for review.

  | Check | Result |
  |---|---|
  | Build | Backend (Release) and frontend (Vite + PWA) build with 0 warnings |
  | Types, lint, format | `tsc`, ESLint (`--max-warnings 0`), Prettier, `dotnet format` and `check-i18n` are clean |
  | Backend tests | 1064 passed, plus 4 added after the test analysis; 0 failed |
  | Backend coverage | 95.5 % lines overall; `ComplianceInsights` 98.5 % and `ArquebusierRegistry` 98.3 %. Only the impossible-invariant throws are uncovered |
  | Frontend tests | 1673 passed; new code at 96.4 % lines and 93 % branches |
  | E2E (Playwright) | `insights.spec.ts`: 31/31 on Chromium, mobile-360, Firefox and WebKit, and 87/87 with `--repeat-each=3`. Full suite: 233 passed, 9 skipped (existing skips) |
  | Security grep | No secrets, `console.log`, new logging or real-looking personal data in the changed files |

  - *Pre-existing flake to follow up.* `photos.spec.ts` "rotates a license photo…" failed once on Firefox under full-suite load. It passed 5/5 alone, and this change does not touch it.
  - *Test analysis (`pr-test-analyzer`).* Its HIGH finding is fixed: invalidation is now tested at the real call sites (save, delete, registration), mutation-checked so that each test fails without its `invalidateInsights` call. The following were also added:
    - the Madrid-midnight date flip across the list, the summary and the statistics;
    - the 34/35 age bracket;
    - owned weapons under scope and filters.
  - *E2E review (`e2e-runner`).* Its findings are fixed:
    - a real out-of-scope row is asserted absent;
    - the summary refetch after a save is awaited;
    - the spec has its own identity range;
    - the dark theme is asserted;
    - content markers are awaited before axe.
- **For `add-exports` (#12).** Any downloadable statistics leave the user's scope, so cells below a
  small threshold (e.g. under 5) must be suppressed there (security review, small-cell
  re-identification). On screen nothing new is disclosed.

## Risks / Trade-offs

- [Two mappings to `ComplianceFacts`, in the registry list/detail and in the compliance module] →
  One small mapper on each side, with the D3 consistency test over the seeded data, which fails if
  they diverge.
- [The registry now depends on a later module's contract, and DI needs `IComplianceRules` wherever
  the registry runs] → Every host (API, test `ApiFactory`, seed command) registers all modules
  through `AddModules`. A startup test resolves the registry endpoints.
- [Every A_PROF license shows as expiring, because it lasts one year] → Accepted by the maintainer
  (12-month threshold). The threshold is one named constant, changed only through an OpenSpec
  change.
- [The navigation count adds a request to every signed-in page] → It is a small payload, with a
  `staleTime` of 30 s, refetched on focus and after registry writes only.
- [The list and the summary are computed at different instants around midnight in Europe/Madrid,
  so they may disagree for a moment] → They converge on the next refetch. The tests freeze time.
- [Small counts on the statistics page could single out a person] → The users who see them can
  already open every arquebusier in their scope, so nothing new is disclosed. Downloads, which
  could leave the scope, are out of scope (#12).
- [Removing `HomePage` drops the shortcut cards] → The navigation keeps every destination, and the
  dashboard links to the filtered lists.

## Migration Plan

There are no database migrations. The API change is additive: new fields and routes. Deploy as
usual, then run the seed in development to get arquebusier 14 and the extra photo. To roll back,
revert the change. No data is affected.

## Resolved Questions

- Under-18 arquebusiers: the maintainer confirmed (2026-10-02) that arquebusiers must be of legal
  age (18). There is no special authorisation for minors. `UNDER_AGE` stays as specified, and it is
  a warning, as BR-04 requires. It is recorded as a maintainer decision in
  `docs/open-questions.md` (task 2.3).
