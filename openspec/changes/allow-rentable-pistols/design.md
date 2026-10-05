# Design

## Context

See `proposal.md`. The pistol rule is enforced in five places today:
- `WeaponModelEndpoints` (`pistolNotRentable`, `rentable` required);
- the check constraint `ck_weapon_models_pistol_not_rentable` in `FederationCatalogDbContext`;
- the form: `WeaponModelFields` hides the checkbox and sets `rentable=false` for `PISTOL`, and
  `WeaponModelDetailPage` forces `rentable:false` when editing a pistol;
- XML docs and comments in `WeaponKind`, `ICatalogDirectory` and the contracts;
- specs and docs.

The edition's rental set (`notRentable`) and the order entries (`notOffered`) already decide from
the `rentable` flag and the set, never from the kind. They need no logic change.

## Goals / Non-Goals

**Goals:** a single rule for every kind; no data rewrite; the build green at every step.

**Non-Goals:** anything listed in the proposal's non-goals.

## Decisions

### D1. Drop the constraint with a migration, not a data change

An EF migration `AllowRentablePistols` drops `ck_weapon_models_pistol_not_rentable`. No row
changes: every pistol stays non-rentable until an Admin edits it. `Down` re-creates the constraint.
It would fail if a rentable pistol exists, which is the correct guard for a rollback.

*Alternative*: keep the constraint and add a feature flag. Rejected, because the maintainer wants
the simpler rule, not a toggle.

### D2. Keep `rentable` required for every kind

The API keeps `rentable` required, as `400` naming `rentable` with `required`. Only the
`pistolNotRentable` reason disappears. Clients sending `rentable: true` for a pistol were refused
before and are accepted now, which is backwards compatible.

### D3. Form

`WeaponModelFields` shows the "Rentable" checkbox for every kind. Changing the kind no longer
touches it. The pistol hint keeps the optional-attributes sentence only. `weaponModelSchema` drops
its pistol refinement.

### D4. Audit and i18n

Edits stay audited by the existing catalogue audit, with previous and new values. The texts
`catalog:weaponModels.problems.pistolNotRentable` and the rentable part of the pistol hint are
removed or rewritten in es-ES, ca-ES-valencia and en.

## Risks / Trade-offs

- [A pistol offered in an edition changes the rental company export] → The export lists rentals by
  model label, so a pistol line appears as any model would. No layout change. Covered by an export
  test.
- [Rollback with rentable pistols present] → `Down` fails loudly. The runbook is to clear the flag
  first.

## Migration Plan

Deploy the migration with the API; it runs at start-up like the others. No data backfill.
