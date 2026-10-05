# Proposal

## Why

BR-07 says pistols are never rentable. The rule is enforced in the API, in a database check
constraint, in the edition's rental set, in the order entries and in the weapon model form, which
hides the rentable checkbox for pistols. The maintainer has decided to drop the pistol exception.
Whether a model can be rented becomes a plain catalogue choice for every kind, because the
Federation may rent pistols in the future. The spreadsheet PolvorApp replaces already listed
"Pistola" among its rental choices (`docs/current-state.md`). One rule for every kind is also
simpler to explain and to maintain.

Capability (from `docs/mvp.md`): **`federation-catalog`**, with knock-on wording in
**`festival-editions`** and **`comparsa-orders`**. It amends **BR-07** (`docs/data-model.md`), keeps
UC-10, UC-12 and UC-24 as they are, and settles Q-07's "pistols never rented" point by a
maintainer decision.

## What Changes

- **BR-07** becomes "A rental model must be offered in the edition", with no exception by kind.
- **Weapon models**: any kind may be rentable. An Admin sets and changes `rentable` for pistols as
  for trabucos and arcabuces. The `pistolNotRentable` validation reason and the
  `ck_weapon_models_pistol_not_rentable` check constraint are removed.
- **Weapon model form**: the rentable choice is shown for every kind. Choosing `PISTOL` no longer
  clears it. The pistol hint keeps only "side, handedness and size are optional".
- **Edition rental set**: a rentable pistol can be offered. `notRentable` still refuses any model
  that is not rentable.
- **Order entries**: a `RENTAL` entry may name an offered pistol. It counts in totals, billing and
  the rental company export like any other rental model.
- **Synthetic seed**: unchanged. The catalogue keeps a non-rentable "PISTOLA", which still covers
  the non-rentable case.

## Non-goals

- Making existing pistols rentable automatically: the flag is unchanged by the migration.
- Rules tying a rental model to the arquebusier's side, or a pistol to any side.
- Changes to owned weapons or loans, which already allow pistols.
- Separate prices for pistol rentals: the edition's single weapon rental price applies.

## Capabilities

### New Capabilities
- none.

### Modified Capabilities
- `federation-catalog`: "Weapon models" is replaced by "Weapon models (BR-07)", the same rules
  without the pistol restriction. The scenario "Rentable pistol is blocking" goes, and rentable
  pistol scenarios are added.
- `festival-editions`: "Rental models offered in an edition (BR-07)" is replaced by "Rental models
  offered in an edition", where a rentable pistol can be offered.
- `comparsa-orders`: "Edition entries (BR-05, BR-07)". A `RENTAL` entry may name an offered model
  of any kind.

## Impact

- **Backend**:
  - `FederationCatalog`:
    - endpoint validation;
    - the EF model check constraint, with a migration that drops it;
    - XML docs on `WeaponKind`, `WeaponModelSummary` and the contracts.
  - `FestivalEditions` and `ComparsaOrders`: comments only. Their checks already rely on the
    `rentable` flag.
  - Integration tests in `Catalog/WeaponModelManagementTests` and the edition and order tests.
- **Frontend**:
  - `WeaponModelFields`, `WeaponModelDetailPage` and `weaponModelSchema`;
  - `problems.ts` drops `pistolNotRentable`;
  - i18n in three locales (the pistol hint, the removed reason).
- **Docs**:
  - `docs/data-model.md` (BR-07, `WeaponModel`);
  - `docs/open-questions.md` (Q-07 note);
  - `docs/development.md` (seed note).
- **Security and GDPR**: none. The rental flag is catalogue data, and its changes stay audited.
