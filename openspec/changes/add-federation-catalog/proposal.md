# Proposal

## Why

Change #3 left comparsa scoping in place (BR-12, SEC-03), but a FiringChief can still see no data at
all. That is because comparsas and FiringChief assignments do not exist yet. Every later change
depends on this reference data: arquebusiers belong to a comparsa (#5), owned weapons and rentals
point to a weapon model (#5, #9, #10) and orders are submitted per comparsa (#10). Today every
comparsa names the same weapons differently ("Arcabuz Normal" vs "ARCABUZ CRISTIANO DIESTRO",
`docs/current-state.md` §7). A shared catalogue fixes this at the source.

Capability (from `docs/mvp.md`): **`federation-catalog`**, change #4 of the sequence. It implements
the catalogue part of **UC-24**: manage comparsas, FiringChief assignments and the weapon catalogue.
It also enforces the catalogue half of **BR-07** (pistols are never rentable) and it feeds
**BR-12** with real assignments.

## What Changes

- **Comparsas** (UC-24): Admins create, edit, deactivate, reactivate and delete comparsas. A
  comparsa has a `name` that is unique case-insensitively and a `side` (`MOORISH` | `CHRISTIAN`). A
  deactivated comparsa keeps its history and gets no new assignments. Admins see every
  comparsa. FiringChiefs see only the comparsas they are assigned to, in read-only form. Any other
  comparsa answers `404`, as BR-12 requires.
- **FiringChief assignments** (UC-24, BR-12): Admins assign and unassign users with the
  `FIRING_CHIEF` role to comparsas. Both the comparsa detail page and the user detail page offer
  this (maintainer decision). A comparsa can have several FiringChiefs, and a FiringChief can hold
  several comparsas. These rules are blocking: only a `FIRING_CHIEF` user can be assigned, a
  deactivated user or an inactive comparsa cannot receive new assignments, and the same assignment
  cannot exist twice. The federation-catalog module implements the `IFiringChiefAssignmentSource`
  contract from `identity-access`, so FiringChiefs finally get their comparsa scope.
- **Weapon catalogue** (UC-24, BR-07): Admins create, edit, deactivate, reactivate and delete
  `WeaponModel`s. A model has a `kind` (`TRABUCO` | `ARCABUZ` | `PISTOL`), `side`, `handedness`
  (`RIGHT` | `LEFT`), `size` (`NORMAL` | `SMALL`), `rentable` and a Federation `label`. Its rules
  are blocking:
  - the label is unique;
  - a pistol can never be rentable (BR-07);
  - a trabuco or arcabuz needs a side, a handedness and a size, and that combination is unique;
  - for pistols only the kind and the label are required (maintainer decision).
  The kind and the side can be combined freely (maintainer decision). The Federation's own labels
  do not follow Q-07 strictly ("ARCABUZ CRISTIANO"). Every signed-in user can read the catalogue,
  because FiringChiefs need it for owned weapons in #5.
- **Deletion** (maintainer decision): Admins can delete a comparsa or a weapon model, after a
  confirmation. Deleting a comparsa also removes its FiringChief assignments. Deletion is blocking
  (`409`) while other records reference the comparsa or model. No module references them in this
  change. Later changes (#5 arquebusiers and owned weapons, #9 edition availability, #10 orders)
  plug their checks into a usage contract that this change defines. Deactivation stays available
  for comparsas and models that have history.
- **Audit trail** (SEC-05): every create, update, deactivation, reactivation, deletion, assignment
  and unassignment is audited in the same transaction. Comparsa-related entries carry the comparsa id.
- **UI**: a Comparsas list and a comparsa detail page with a FiringChiefs section. A Weapon models
  list and a model form (Admin). A "Comparsas" section on the user detail page for FiringChiefs. New
  navigation entries. Every text is translated into es-ES, ca-ES-valencia and en.
- **Synthetic seed** (SEC-11): fictional comparsas of both sides, assignments for the seeded
  FiringChiefs and a standard weapon catalogue, for development and E2E only. In production an Admin
  enters the ~20 real comparsas and the models through the UI (maintainer decision), so no real
  names go into the repository.

## Non-goals

- Edition availability of rental models (`EditionWeaponModel`) and the edition half of BR-07: that
  is `add-festival-editions` (#9).
- Owned weapons, rental weapon units and weapon numbers: that is `add-arquebusier-registry` (#5) and
  later changes.
- Arquebusiers and comparsa transfers (BR-13): that is #5.
- The usage checks themselves (arquebusiers, owned weapons, edition availability, orders) and the
  edit restrictions on referenced models and comparsas. The later changes that create those
  references add them.
- Linking a `User` to an `Arquebusier`. Arquebusiers are not users: they are never invited and never
  sign in. Only FiringChiefs and Federation staff are users. A FiringChief can also be an arquebusier
  with powder and a weapon. #5 registers them in the registry like any other arquebusier and decides
  whether the two records need a link.
- Loading real comparsas or catalogue data through migrations or commands.
- Enforcing kind ↔ side consistency (Q-07). This change records the free combination as a
  maintainer decision.
- The comparsa logo. It needs the private storage and the image pipeline that #6 builds, so it
  comes in its own change `add-comparsa-logos` (6b in `docs/mvp.md`, maintainer decision). An
  Admin uploads it, and it is shown in the list, the detail and the FiringChief's header, and in
  later PDFs.
- Comparsa notes (UC-08), which come after the MVP.
- The audit log viewer (#15).

## Capabilities

### New Capabilities
- `federation-catalog`: comparsas (name, side, active), FiringChief assignments that feed comparsa
  scoping, and the weapon model catalogue, with Admin management, read access for FiringChiefs,
  auditing and UI.

### Modified Capabilities
<!-- None. The identity-access "Comparsa scoping (BR-12)" requirement already defines FiringChief
     scope as "the comparsas assigned to them". This change supplies those assignments without
     changing that requirement. -->

## Impact

- **Backend**: a new module `Modules/FederationCatalog` (implementation and `.Contracts`) with its
  own schema, DbContext, migration and seeder. It references `IdentityAccess.Contracts` to implement
  `IFiringChiefAssignmentSource`. It also uses a new, small read contract `IUserDirectory` to check
  a user's role and status and to show their name. `PolvorApp.Api` registers the module.
- **API**: new endpoints `/api/comparsas`, `/api/comparsas/{id}/firing-chiefs`,
  `/api/firing-chiefs/{userId}/comparsas` and `/api/weapon-models`. `contracts/openapi.json` and
  the orval client are regenerated.
- **Frontend**: a new feature folder `src/features/federation-catalog/`, routes, navigation entries,
  an i18n namespace `catalog` in 3 locales and a section on the identity `UserDetailPage`.
- **Docs**: `docs/data-model.md` (WeaponModel `active`, optional attributes for pistols),
  `docs/glossary.md` if needed, `docs/design/status.md` (active/inactive), `docs/mvp.md` status,
  `backend/src/Modules/README.md` if conventions change.
- **ADRs**: none new. The change follows ADR-0001 (module), ADR-0002 (EF Core/PostgreSQL),
  ADR-0004 (authorisation), ADR-0007 (i18n), ADR-0009 (design system) and ADR-0011 (React,
  Playwright).
