# Spec Delta

## MODIFIED Requirements

### Requirement: Entry history (BR-14)
Entries SHALL be kept as the history of the edition. Each entry SHALL keep its own copy of:
- the arquebusier's `firstName`, `lastName`, `nationalId` and `federationId`;
- for `OWNED`, the owned weapon's model, `weaponNumber` and `ownershipGuideNumber`.

Each loan SHALL keep its own copy of the lender's `firstName`, `lastName`, `nationalId` and
comparsa, and of the weapon's model, `weaponNumber` and `ownershipGuideNumber`.

The copy SHALL be taken from the registry when the entry or loan is created, and again whenever
it is saved, while the arquebusier and the weapon are still in the registry. Submitting or
validating the order SHALL take it again for all its entries and loans. While the arquebusier is
in the registry, screens SHALL show their current registry data. Once they are deleted, screens,
totals and later exports SHALL use the copy.

Entries of every edition other than the one in progress, and those of the edition in progress
once its orders are closed, are the history: no operation SHALL remove them. Deleting an arquebusier SHALL NOT anonymise them. They SHALL be anonymised only on a
GDPR erasure request (UC-26, audit-privacy capability, "Erasing a person's data"), which blanks the
copied identity and weapon number and guide of the person's entries and of the loans in which they
are the lender, marks them as erased, and keeps every other field.

#### Scenario: Name kept after deletion
- **WHEN** an arquebusier with a validated 2030 entry is deleted from the registry
- **THEN** the 2030 order still shows that entry with their name, national ID and federationId, marked as no longer in the registry

#### Scenario: Copy follows registry corrections
- **WHEN** a FiringChief corrects an arquebusier's last name in the registry, and the order is submitted afterwards
- **THEN** the entry's copy holds the corrected last name

#### Scenario: Owned weapon kept in the history
- **WHEN** the owned weapon of a validated `OWNED` entry is later removed from the registry
- **THEN** the entry still shows the weapon's model, number and ownership guide

#### Scenario: Anonymised only by erasure
- **WHEN** an Admin erases, on a GDPR request, a person whose 2030 entry keeps its copy
- **THEN** the 2030 entry keeps its status, powder, caps, weapon source, flask and rental model, and its name, national ID, federationId and weapon number and guide are blank

## ADDED Requirements

### Requirement: Erased entries
An entry or loan erased by a GDPR request SHALL:
- show "Erased person" in place of the name, and no national ID, `federationId`, weapon number or
  ownership guide, on every screen;
- be read-only: editing an erased entry, or a loan with an erased lender, SHALL be blocking
  (`409 Conflict`, `entryErased`). Changing the weapon source of a borrower whose lender is
  erased SHALL still be allowed and SHALL delete the erased loan;
- keep counting in the order's totals, the orders dashboard and billing;
- never be offered or found by the lender lookup, and never be pre-filled into a later edition.

#### Scenario: Erased entry in the order
- **WHEN** a user opens an order with an erased entry
- **THEN** the entry shows "Erased person" without national ID or federationId, and has no edit action

#### Scenario: Editing an erased entry is blocking
- **WHEN** an Admin saves a change to an erased entry
- **THEN** the request is rejected with `409 Conflict` with `entryErased`, and nothing changes

#### Scenario: Totals keep the erased entry
- **WHEN** a validated order has an erased `ACTIVE` 2 kg entry
- **THEN** the order's total powder includes those 2 kg

#### Scenario: Borrower leaves an erased loan
- **WHEN** a FiringChief changes to `RENTAL` the weapon source of an editable entry whose external lender was erased
- **THEN** the entry is saved and the erased loan is deleted
