# Proposal

## Why

Comparsas, FiringChief assignments and the weapon catalogue now exist (#4), but nobody can register
an arquebusier yet. Every comparsa still keeps its arquebusiers in its own spreadsheet
(`docs/current-state.md` §2). There, DNI letters are not checked, the same person can appear in
two comparsas, and license expiry is a hand-written formula. The registry is the source of truth
that every later change reads: photos (#6), compliance warnings (#7), import (#8), orders (#10),
exports (#12) and badges (#16). PolvorApp is the authoritative source for arquebusier data (Q-05).

Capability (from `docs/mvp.md`): **`arquebusier-registry`**, change #5 of the sequence. It
implements **UC-01** (register), **UC-02** (update and renew the license, without photos),
**UC-03** (training course), **UC-04** (owned weapons), **UC-05** (Active/Reserve and deletion) and
**UC-29** (transfer between comparsas). It enforces **BR-01**, **BR-02**, **BR-03**, **BR-12**,
**BR-13** and **BR-14**.

## What Changes

- **Arquebusiers** (UC-01, UC-02): FiringChiefs register and edit the arquebusiers of their own
  comparsas, and Admins those of every comparsa. An arquebusier has:
  - `federationId`, `nationalId` (DNI/NIE), `firstName`, `lastName` and `birthDate`;
  - optional `email` and `phone`;
  - `gender` (`MALE` | `FEMALE` | `UNSPECIFIED`);
  - `comparsa` and `status` (`ACTIVE` | `RESERVE`).

  These rules are blocking:
  - the `nationalId` must be a valid DNI or NIE with its check letter, and look-alike non-Latin
    characters are rejected (BR-01);
  - the `nationalId` and the `federationId` are unique across the whole Federation (BR-02). A
    FiringChief who hits a duplicate in another comparsa is not told which comparsa it is (BR-12).
- **License and course** (UC-02, UC-03): the current license only, with no number and no history:
  - its `type` (`AE` | `A_PROF`), and either the `PENDING` state or its `issuedOn` and `expiresOn`
    dates;
  - `expiresOn` defaults to `issuedOn` + 5 years for AE and + 1 year for A-PROF, and can be edited
    (BR-03);
  - the license status (`PENDING`, `VALID`, `EXPIRED`) is derived, never stored;
  - `trainingCompletedOn` records the course date, and empty means not done.

  The license photos come in #6.
- **Status** (UC-05): `ACTIVE` or `RESERVE`, the only status. New arquebusiers are `ACTIVE`.
- **Owned weapons** (UC-04): each arquebusier has a list of owned weapons. Each one has an active
  `WeaponModel` from the catalogue (any kind, including pistols), a `weaponNumber` and an
  `ownershipGuideNumber`. Both numbers are required. The `ownershipGuideNumber` is unique across
  the Federation (blocking, maintainer decision). The `weaponNumber` is not unique, because numbers
  engraved by different makers can coincide.
- **Transfer** (UC-29, BR-13): an Admin moves an arquebusier, with their owned weapons, to another
  active comparsa. There are no editions yet, so nothing past exists to keep. #10 keeps past
  entries in their original comparsa.
- **Deletion** (UC-05, BR-14): when an arquebusier leaves the Federation, a FiringChief (in their
  own comparsa) or an Admin deletes them after a confirmation (maintainer decision). This erases
  the arquebusier's personal data and owned weapons. The audit entry keeps no personal data.
- **Visibility** (BR-12, SEC-03): FiringChiefs see only the arquebusiers of their assigned
  comparsas. Any other arquebusier answers `404`. Admins see every arquebusier and can filter by
  comparsa. Every write is audited (SEC-05), with the arquebusier's comparsa.
- **Catalogue usage** (federation-catalog contract): the registry implements `ICatalogUsage`. A
  comparsa with arquebusiers, or a weapon model with owned weapons, can no longer be deleted
  (`409`). In that case the Admin deactivates it instead.
- **UI**: an Arquebusiers list (search by name, DNI or federationId, and filters by comparsa and
  status), a create and edit form with license, course and owned weapons sections, a transfer
  action for Admins and a delete action. A new navigation entry. Every text is translated into
  es-ES, ca-ES-valencia and en.
- **Synthetic seed** (SEC-11): fictional arquebusiers with valid synthetic DNI/NIE in the seeded
  comparsas, covering every status, license state and owned weapons.

## Non-goals

- The ID photo and the license photos, cropping and EXIF stripping: that is
  `add-arquebusier-photos` (#6). The ID photo is mandatory in `docs/data-model.md`. Until #6, an
  arquebusier is registered without one, and #6 decides how existing records without a photo are
  handled.
- Compliance warnings (license expiring or expired at festival dates, no course, under age), the
  alerts dashboard and the statistics: that is `add-compliance-insights` (#7). This change only
  shows the license status and the course date.
- Spreadsheet import (UC-09): that is `add-registry-import` (#8).
- Locking the registry (BR-10, UC-11): that is `add-festival-editions` (#9).
- Edition entries, weapon loans and the loan exception of BR-12 (borrower visible across
  comparsas), and anonymising past entries on deletion (second half of BR-14): those are
  `add-comparsa-orders` (#10), which extends the deletion when entries exist.
- The "first year" flag, comparsa notes (UC-08) and the badge (UC-30).
- GDPR export and erasure tooling, and the audit log viewer (UC-25, UC-26): those are #15.
- Linking a FiringChief's `User` to their own `Arquebusier` record (Q-52). They stay separate
  records with no link (maintainer decision). A FiringChief who fires registers themselves like
  any other arquebusier of their comparsa.
- Edit restrictions on weapon models that owned weapons reference. Catalogue corrections keep
  applying to every owned weapon of that model (see design).

## Capabilities

### New Capabilities
- `arquebusier-registry`: arquebusiers with DNI/NIE validation and Federation-wide uniqueness, the
  current license and training course, Active/Reserve status, owned weapons, transfer between
  comparsas and deletion, with comparsa scoping, auditing, UI and synthetic seed.

### Modified Capabilities
<!-- None. federation-catalog "Deleting comparsas and weapon models" already requires a 409 when
     other records reference a comparsa or model, and names arquebusiers and owned weapons as such
     records. This change supplies that reference check without changing the requirement. -->

## Impact

- **Backend**: a new module `Modules/ArquebusierRegistry` (implementation and `.Contracts`) with
  schema `registry`, its DbContext, migration and seeder. It references
  `FederationCatalog.Contracts` to implement `ICatalogUsage` and to read comparsas and weapon
  models through new small read contracts. It references `IdentityAccess.Contracts` for
  `IComparsaScope` and `ICurrentUser`. Registry tables reference the catalog's comparsas and
  weapon models by foreign key, which the catalog's delete already maps to its `inUse` problem.
  `PolvorApp.Api` registers the module, and the Dockerfile and solution list the new projects.
- **API**: `/api/arquebusiers` (list, detail, create, update, delete), its owned-weapon
  sub-resource and transfer. `contracts/openapi.json` and the orval client are regenerated. The
  scope guard test is extended to `/arquebusiers/{id}` routes.
- **Frontend**: a new feature folder `src/features/arquebusier-registry/`, routes, a navigation
  entry, the i18n namespace `registry` in 3 locales, and possibly a date input composite in
  `components/app/`.
- **Docs**: `docs/data-model.md` (Arquebusier and OwnedWeapon field rules, ID photo deferred to
  #6), `docs/glossary.md` (`Gender`, `LicenseStatus` codes), `docs/open-questions.md` (Q-52
  answered), `docs/design/status.md` if the license mapping changes, `docs/mvp.md` status, and
  `backend/src/Modules/README.md` (cross-module foreign key convention).
- **ADRs**: none new. The change follows ADR-0001, ADR-0002, ADR-0004, ADR-0007, ADR-0009 and
  ADR-0011.
