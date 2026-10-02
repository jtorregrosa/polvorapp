# Proposal

## Why

FiringChiefs keep their own "Avisos" and "Estadísticas" sheets to find expired or expiring
licenses, missing courses and under-age arquebusiers, and to count their people by age and gender
(`docs/current-state.md` §2). Today PolvorApp only shows whether a license is expired, pending or
missing, and the detail page computes a partial warning in the browser. Nobody can see at a glance
what needs attention before the license and course deadlines of October–December. The registry is
now complete (#5, #6, #6a, #6b), so the insights can be derived from it before editions and orders
(#9, #10) build on the same rules.

Capability (from `docs/mvp.md`): **`compliance-insights`**, change #7 of the sequence. It implements
**UC-06** (alerts dashboard) and **UC-07** (statistics and equality report). It turns **BR-04** into
explicit **warnings** that never block anything. It keeps **BR-12** (FiringChiefs see only their
comparsas) on the server. It follows ADR-0001 (one module per capability), ADR-0002, ADR-0007
(three locales), ADR-0009 and ADR-0011 (composites and tokens only), and ADR-0013 (warning tone).

## What Changes

- **Compliance warnings (BR-04)**, derived on the server and never stored. They use today in
  Europe/Madrid as the reference date until editions exist. They apply to every arquebusier,
  `ACTIVE` and `RESERVE` alike (maintainer decision). The warnings are:
  - `LICENSE_MISSING`: no license;
  - `LICENSE_PENDING`: the license is still pending;
  - `LICENSE_EXPIRED`: the license has expired;
  - `LICENSE_EXPIRING`: the license is valid but expires in less than 12 months, the same
    threshold as today's spreadsheet (maintainer decision);
  - `COURSE_MISSING`: the training course is not done;
  - `UNDER_AGE`: the arquebusier is younger than 18;
  - `ID_PHOTO_MISSING`: there is no ID photo, which the badge needs (maintainer decision);
  - `LICENSE_PHOTOS_MISSING`: the license is issued but has no front or no back photo
    (maintainer decision).

  Warnings never block registering, editing, transferring or deleting. One module owns the rules,
  and every screen shows the same result.
- **Registry shows the warnings.**
  - Arquebusier list:
    - rows and the detail carry their warnings;
    - a valid license that is expiring shows as "Expiring soon";
    - a new "Expiring soon" counter is added. It was deferred from #6a because no threshold
      existed;
    - a new warning filter is added.
  - Arquebusier detail: the warning banner lists every warning, with the expiry date or the age.
    It replaces the partial check the browser makes today.
- **Alerts dashboard (UC-06)** on the start page, replacing the interim shortcut cards. It shows:
  - figures for active and reserve arquebusiers and for those with warnings;
  - one figure per warning, each linking to the arquebusier list filtered by that warning;
  - a short table of the next license expiries.

  Everything stays within the user's scope. The Arquebusiers navigation item shows how many
  arquebusiers have warnings.
- **Statistics and equality report (UC-07)** on a new Statistics page, shown on screen only
  (maintainer decision). It can be filtered by comparsa and status. It shows:
  - totals and the status split;
  - gender, overall and across age brackets (under 25, 25–34, 35–44, 45 and over);
  - training course done or not, by gender;
  - license state, including expiring;
  - owned weapons: arquebusiers with or without one, by gender, and weapons by kind;
  - for users who see several comparsas, a row per comparsa.

  Only aggregates are returned. Gender is used for nothing else (`docs/compliance.md`).
- **Synthetic seed**: an under-age arquebusier, a license expiring in a few months, and an issued
  license with a missing photo, so every warning can be seen locally.

## Non-goals

- **Checking licenses against the festival dates**, and the warnings shown when an order is
  submitted (BR-04 for edition entries, UC-14). These need editions and orders (#9, #10). The order
  change will evaluate the same rules on the festival dates.
- **"First year" statistic and flag.** It needs entries of previous editions (#10), and is added
  to the statistics there.
- **Downloads** (Excel, PDF or CSV) of the statistics or of the alerts. Exports belong to #12
  `add-exports`, with their audit entries.
- **Email notifications** about expiring licenses (UC-23, #14).
- **Configurable thresholds.** 12 months and 18 years are fixed rules, changed only through an
  OpenSpec change.
- **Next birthdays list** from the old sheet ("nice-to-have", not chosen).
- **Blocking anything.** Warnings stay warnings (BR-04).
- **Warning for RESERVE arquebusiers who stay in reserve for years** (SEC-08 optional item).
- **A chart library.** Bars use the native `meter` element, already styled for `KeyFacts`, so no
  new dependency or ADR is needed.

## Capabilities

### New Capabilities
- `compliance-insights`: the compliance warnings and their rules, the alerts dashboard on the
  start page, the warning counter in the navigation, and the statistics and equality report.

### Modified Capabilities
- `arquebusier-registry`: the following requirements change:
  - "Arquebusier visibility (BR-12)": rows carry warnings, and the list gains the expiring state,
    the "Expiring soon" counter and a warning filter;
  - "Registry screens": the detail lists the server's warnings;
  - "Synthetic registry data": the seed covers every warning.
- `design-system`: the following requirements change:
  - "Status semantics": the warning codes are listed, and "expiring soon" is tied to the
    `LICENSE_EXPIRING` warning;
  - a new "Breakdown figures" requirement adds a composite for counts with shares and an
    accessible bar.

## Impact

- **Backend**:
  - New module `Modules/ComplianceInsights`, with `PolvorApp.ComplianceInsights` and
    `PolvorApp.ComplianceInsights.Contracts`. It has no database schema. It holds the warning
    rules behind `IComplianceRules` and the summary and statistics endpoints.
  - `ArquebusierRegistry.Contracts` gains the read contract `IArquebusierFacts` (no names and no
    contact data).
  - The registry list and detail call `IComplianceRules` to add warnings.
  - The module is registered in `Program.cs`, `PolvorApp.slnx` and the `backend/Dockerfile`.
- **API**:
  - `GET /api/compliance/summary` and `GET /api/compliance/statistics`;
  - `ArquebusierRowResponse.warnings` and `ArquebusierResponse.warnings`;
  - `contracts/openapi.json` and the orval client are regenerated.

  The change is additive, with no breaking change.
- **Frontend**:
  - a new `features/compliance-insights` folder: dashboard, statistics page and the warnings
    banner;
  - the start page and navigation change: a Statistics entry and the warning count;
  - the arquebusier list and detail change;
  - new composite `Breakdown`;
  - the `warning` status map gains the new codes;
  - new `insights` namespace and changed `registry`, `ui` and `common` keys in the three locales.
- **Docs**:
  - `docs/data-model.md`: BR-04 note, derived data;
  - `docs/glossary.md`: warning codes, "expiring soon";
  - `docs/design/` (`status.md` regenerated, `README.md` and `patterns.md` for `Breakdown` and the
    dashboard);
  - `backend/src/Modules/README.md`: the new module and its contracts;
  - `docs/mvp.md`: the status of #7.
- **Security and GDPR**:
  - no new personal data is stored, and reads are scoped (BR-12);
  - statistics return only aggregates;
  - nothing is exported, so no new audit entry is needed (SEC-05 covers writes and exports).
- **ADRs**: none new.
