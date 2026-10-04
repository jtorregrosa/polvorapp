# comparsa-orders Specification

## Purpose
Replaces the yearly order spreadsheets and the per-arquebusier Google Form. Each comparsa prepares
its order for a festival edition, one entry per arquebusier with powder, caps, weapon, flask and
weapon loans. FiringChiefs submit it with an attestation, and the Federation reviews it and sees
the totals of every comparsa (UC-12 to UC-16).

## Requirements

### Requirement: Comparsa orders (UC-12)
A `ComparsaOrder` SHALL belong to one `FestivalEdition` and one `Comparsa`. There SHALL be at most
one order per comparsa and edition. This SHALL be blocking and SHALL hold under concurrent
requests (`409 Conflict`, `orders.alreadyPrepared`). An order SHALL have:
- a `status`: `DRAFT`, `SUBMITTED`, `RETURNED` or `VALIDATED`;
- the date it was prepared;
- for its latest submission, the submitting user, the date and time, and whether a FiringChief
  attested it or an Admin submitted it;
- for its latest review, the reviewing Admin, the date and time and, when returned, the
  `returnReason`;
- its `EditionEntry`s.

A comparsa of an edition that has no order SHALL be shown as "not prepared". This is not a
stored status. No user operation SHALL delete an order or remove an entry. The only removal is
the one described in "Entries after registry changes": the entry, in the edition in progress, of
an arquebusier deleted from the registry while its orders are open.

#### Scenario: One order per comparsa and edition
- **WHEN** two FiringChiefs of "Comparsa Sintética Norte" prepare its order for the current edition at the same time
- **THEN** one order is created, and the other request is rejected with `409 Conflict` and `orders.alreadyPrepared`

#### Scenario: Not prepared
- **WHEN** an Admin opens the orders of the current edition before "Comparsa Sintética Sur" prepares its order
- **THEN** "Comparsa Sintética Sur" is listed as not prepared

### Requirement: Preparing an order (UC-12)
Preparing an order SHALL create the order with status `DRAFT` and one `EditionEntry` for every
arquebusier who belongs to the comparsa at that moment, `ACTIVE` and `RESERVE` alike. An
arquebusier who already has an entry in the edition, in any comparsa's order, SHALL be left out.
Each entry SHALL be pre-filled as described in "Pre-fill from the previous edition".

The following rules SHALL be blocking:
- the edition SHALL exist and, for a FiringChief, SHALL NOT be `DRAFT` (`404 Not Found`);
- the comparsa SHALL be in the user's scope (`404 Not Found`, BR-12);
- the edition SHALL have left `DRAFT` (`409 Conflict`, `orders.editionNotStarted`);
- the comparsa SHALL be active (`409 Conflict`, `orders.comparsaInactive`);
- a FiringChief SHALL prepare only while the orders of the edition are open (BR-10, see "Who may
  edit orders").

An Admin SHALL be able to prepare the order of any active comparsa, for an edition that is
`IN_PROGRESS` or `CLOSED`.

#### Scenario: FiringChief prepares the order
- **WHEN** the orders of the current edition are open and a FiringChief prepares the order of "Comparsa Sintética Norte", which has 10 `ACTIVE` and 3 `RESERVE` arquebusiers
- **THEN** a `DRAFT` order with 13 entries is created, 10 `ACTIVE` and 3 `RESERVE`

#### Scenario: Orders closed
- **WHEN** a FiringChief prepares an order while the orders of the current edition are closed
- **THEN** the request is rejected with `409 Conflict` and `orders.closed`, and no order is created

#### Scenario: Admin prepares with closed orders
- **WHEN** an Admin prepares the order of "Comparsa Sintética Sur" while the orders are closed
- **THEN** the order is created

#### Scenario: Comparsa outside the scope
- **WHEN** a FiringChief prepares the order of a comparsa not assigned to them
- **THEN** the API responds `404 Not Found` and nothing is created

#### Scenario: Inactive comparsa
- **WHEN** an Admin prepares the order of an inactive comparsa
- **THEN** the request is rejected with `409 Conflict` and `orders.comparsaInactive`

#### Scenario: Draft edition
- **WHEN** an Admin prepares an order for a `DRAFT` edition
- **THEN** the request is rejected with `409 Conflict` and `orders.editionNotStarted`

### Requirement: Pre-fill from the previous edition (UC-12, BR-11)
Each new entry SHALL take its `status` from the arquebusier's status in the registry. Changing the
entry status later SHALL NOT change the registry, and changing the registry status SHALL NOT
change an existing entry (maintainer decision).

For an `ACTIVE` entry, the **previous entry** is the arquebusier's entry in the latest edition with
an earlier `year` in which they have one. When the previous entry is `ACTIVE`, the new entry SHALL copy
from it `powderKg`, `capsBoxes`, `capsType` and `flask`. It SHALL copy the weapon source as
follows:
- `OWNED`, with the same owned weapon, while the arquebusier still owns it;
- `RENTAL`, with the same rental model, while that model is offered for rental in the new edition
  (BR-07);
- `NONE` in every other case. A loan SHALL never be copied.

When there is no previous entry, or it is `RESERVE`, an `ACTIVE` entry SHALL start with `powderKg` 0, no caps,
`flask` `NONE` and a weapon source of `OWNED` with their only owned weapon if they own exactly
one, otherwise `NONE`. A `RESERVE` entry SHALL start with no powder, caps, weapon or flask. No
quantity SHALL ever be carried over (BR-11). Every pre-filled value SHALL be editable.

#### Scenario: Copy from the previous edition
- **WHEN** an arquebusier had an `ACTIVE` entry in 2030 with 2 kg, 3 boxes of `NORMAL` caps, a rental of an offered model and `flask` `RENTAL_2KG`, and the order for 2031 is prepared
- **THEN** their 2031 entry has 2 kg, 3 `NORMAL` boxes, the same rental model and `RENTAL_2KG`

#### Scenario: Rental model no longer offered
- **WHEN** the model an arquebusier rented in 2030 is not offered in 2031
- **THEN** their pre-filled 2031 entry has weapon source `NONE`

#### Scenario: Loans are not copied
- **WHEN** an arquebusier borrowed a weapon in 2030
- **THEN** their pre-filled 2031 entry has weapon source `NONE`

#### Scenario: First entry of an owner
- **WHEN** an `ACTIVE` arquebusier with exactly one owned weapon and no previous entry gets an entry
- **THEN** the entry has weapon source `OWNED` with that weapon, 0 kg and `flask` `NONE`

#### Scenario: Reserve in the registry
- **WHEN** an arquebusier with status `RESERVE` in the registry gets an entry
- **THEN** the entry has status `RESERVE`, 0 kg, no caps, weapon source `NONE` and `flask` `NONE`

### Requirement: Adding arquebusiers to an order (UC-12)
An order SHALL list the arquebusiers of its comparsa who have no entry in the edition as "not in
the order". Users who may edit the order SHALL be able to add any of them. The new entry SHALL be
pre-filled like the others. Nothing SHALL add an entry by itself. The following rules SHALL be
blocking:
- the arquebusier SHALL belong to the order's comparsa (`404 Not Found` otherwise, BR-12);
- the arquebusier SHALL have no entry in the edition (`409 Conflict`, `orders.alreadyInEdition`).

#### Scenario: Arquebusier registered after the preparation
- **WHEN** a FiringChief registers a new arquebusier after preparing the order, and opens the order
- **THEN** the new arquebusier is listed as not in the order, and is added with a pre-filled entry when the FiringChief chooses "Add"

#### Scenario: Already in another comparsa's order
- **WHEN** an arquebusier with an entry in the 2031 order of "Comparsa Sintética Norte" is transferred to "Comparsa Sintética Sur", and a FiringChief tries to add them to the 2031 order of "Comparsa Sintética Sur"
- **THEN** the arquebusier is not listed as not in the order, and the request is rejected with `409 Conflict` and `orders.alreadyInEdition`

### Requirement: Edition entries (BR-05, BR-07)
An `EditionEntry` SHALL have:
- `status`: `ACTIVE` or `RESERVE`;
- `powderKg`: 0, 1 or 2;
- `capsBoxes`: a whole number from 0 to 99, and `capsType` (`NORMAL` or `SMALL`);
- `weaponSource`: `OWNED`, `RENTAL`, `LOAN` or `NONE`, with the owned weapon for `OWNED`, the
  rental `WeaponModel` for `RENTAL` and the `WeaponLoan` for `LOAN`;
- `flask`: `OWNED`, `RENTAL_1KG`, `RENTAL_2KG` or `NONE`.

The following rules SHALL be blocking (`400 Bad Request` naming the field and the reason):
- every value SHALL be one of the listed values (`invalid`);
- `capsType` SHALL be given when `capsBoxes` is above 0, and SHALL be empty when it is 0;
- a `RESERVE` entry SHALL have `powderKg` 0, `capsBoxes` 0, weapon source `NONE` and `flask` `NONE`
  (BR-05);
- `OWNED` SHALL name an owned weapon of the same arquebusier (`notOwned`);
- `RENTAL` SHALL name a model offered for rental in the edition (`notOffered`, BR-07). A pistol is
  never offered;
- `LOAN` SHALL carry a valid loan (see "Weapon loans").

An `ACTIVE` entry with 0 kg, or with weapon source `NONE`, SHALL be valid and SHALL NOT be flagged.
Some arquebusiers only carry powder and others only fire, such as the comparsa captains
(maintainer decision).

#### Scenario: FiringChief edits an entry
- **WHEN** a FiringChief sets an `ACTIVE` entry to 1 kg, 2 boxes of `SMALL` caps, a rental of an offered model and `flask` `RENTAL_1KG`
- **THEN** the entry is saved with those values

#### Scenario: Reserve with powder is blocking
- **WHEN** a FiringChief saves a `RESERVE` entry with 1 kg
- **THEN** the request is rejected with `400 Bad Request` naming `powderKg`, and the entry is unchanged

#### Scenario: Three kilograms are blocking
- **WHEN** an entry is saved with `powderKg` 3
- **THEN** the request is rejected with `400 Bad Request` naming `powderKg`

#### Scenario: Caps without a type
- **WHEN** an entry is saved with 2 caps boxes and no `capsType`
- **THEN** the request is rejected with `400 Bad Request` naming `capsType`

#### Scenario: Model not offered is blocking
- **WHEN** an entry is saved with a rental of a model that is not offered in the edition
- **THEN** the request is rejected with `400 Bad Request` naming `rentalWeaponModelId` with `notOffered`

#### Scenario: Another arquebusier's weapon
- **WHEN** an entry is saved as `OWNED` with an owned weapon of another arquebusier
- **THEN** the request is rejected with `400 Bad Request` naming `ownedWeaponId` with `notOwned`

#### Scenario: Shooter without powder
- **WHEN** a FiringChief saves an `ACTIVE` entry with 0 kg and an owned weapon
- **THEN** the entry is saved without any warning or flag

#### Scenario: Powder carrier without a weapon
- **WHEN** a FiringChief saves an `ACTIVE` entry with 2 kg and weapon source `NONE`
- **THEN** the entry is saved without any warning or flag

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

### Requirement: Weapon loans (UC-13, BR-09)
An entry with weapon source `LOAN` SHALL have one `WeaponLoan`, registered by the users who may
edit the borrower's entry. The lender SHALL be one of:
- **a registered arquebusier**, of any comparsa: the loan SHALL name one of their owned weapons;
- **an external owner** who is not in PolvorApp (maintainer decision). The loan SHALL hold the
  owner's `firstName`, `lastName` and `nationalId`, and the weapon's `WeaponModel`,
  `weaponNumber` and `ownershipGuideNumber`.

There SHALL be no limit on loans, and one owned weapon MAY be lent to several borrowers in the same
edition (BR-09). The following rules SHALL be blocking (`400 Bad Request` naming the field):
- the named owned weapon SHALL exist and SHALL NOT belong to the borrower (`ownWeapon`: use
  `OWNED` instead);
- an external owner's names SHALL follow the arquebusier name rules, and the `nationalId` SHALL be
  a valid DNI or NIE (BR-01);
- an external owner's `nationalId` SHALL NOT be that of a registered arquebusier
  (`lenderRegistered`): the registered owner's weapon is chosen instead;
- the external weapon's model SHALL exist and be active, of any kind; its `weaponNumber` and
  `ownershipGuideNumber` SHALL be 1 to 30 characters, the guide stored upper-cased.

Changing the weapon source away from `LOAN`, or replacing the loan, SHALL delete the previous loan
with its external owner data. A loan SHALL count in the borrower's order.

#### Scenario: Loan from another comparsa
- **WHEN** a FiringChief of "Comparsa Sintética Norte" sets an entry to `LOAN` with an owned weapon of an arquebusier of "Comparsa Sintética Sur"
- **THEN** the loan is saved, and the FiringChiefs of "Comparsa Sintética Sur" see that weapon lent to the borrower, with the borrower's name and comparsa

#### Scenario: Loan from an external owner
- **WHEN** a FiringChief sets an entry to `LOAN` with an external owner, a valid synthetic DNI, an active model, a weapon number and an ownership guide
- **THEN** the loan is saved with the external owner and the weapon, and the guide upper-cased

#### Scenario: Invalid external DNI
- **WHEN** an external owner's `nationalId` has a wrong check letter
- **THEN** the request is rejected with `400 Bad Request` naming `loan.nationalId`

#### Scenario: Registered owner typed as external
- **WHEN** an external owner is saved with the `nationalId` of a registered arquebusier
- **THEN** the request is rejected with `400 Bad Request` naming `loan.nationalId` with `lenderRegistered`

#### Scenario: Same weapon lent twice
- **WHEN** one owned weapon is lent to two borrowers of the same edition
- **THEN** both loans are saved

#### Scenario: Leaving the loan
- **WHEN** a FiringChief changes an entry with an external loan to weapon source `RENTAL`
- **THEN** the loan and its external owner data are deleted

### Requirement: Lender lookup (UC-13, BR-12)
Users who may edit an order SHALL be able to look up a lender by an exact `nationalId`. This is the
only read of another comparsa's arquebusiers. The lookup SHALL:
- require a valid DNI or NIE (BR-01), normalised as in the registry (`400 Bad Request` otherwise);
- when an arquebusier has that `nationalId`, return only their first name, last name, comparsa
  name and owned weapons, each with its id, model and `weaponNumber`. It SHALL NOT return the
  ownership guide, birth date, contact data, license or photos;
- when no arquebusier has it, answer that the owner is not registered, so the user can enter an
  external owner;
- be limited per user, like other requests that reveal whether a personal identifier exists;
- be recorded in the audit trail with the acting user and whether an arquebusier was found,
  without the `nationalId`.

#### Scenario: Registered lender found
- **WHEN** a FiringChief looks up the synthetic DNI of an arquebusier of another comparsa who owns two weapons
- **THEN** the answer has their name, their comparsa and both weapons with model and number, and no ownership guide

#### Scenario: Not registered
- **WHEN** a FiringChief looks up a valid DNI that no arquebusier has
- **THEN** the answer says the owner is not registered

#### Scenario: Invalid DNI
- **WHEN** a FiringChief looks up "12345678A" with a wrong check letter
- **THEN** the API responds `400 Bad Request` naming `nationalId`, and nothing is returned or recorded

#### Scenario: Lookup is audited
- **WHEN** a FiringChief looks up a lender
- **THEN** one audit entry records the FiringChief and whether an arquebusier was found, and contains no national ID

### Requirement: Who may edit orders (BR-10)
Editing an order means preparing it, adding an arquebusier, editing an entry, and registering a
loan. A FiringChief SHALL edit only the orders of the comparsas in their scope, only
while the orders of the edition are open (the current edition with `ordersOpen`), and only orders
that are not `VALIDATED`. Otherwise their edits SHALL be refused, and nothing SHALL change:
- orders closed: `409 Conflict`, `orders.closed`;
- a validated order: `409 Conflict`, `orders.validated`.

When a FiringChief edits a `SUBMITTED` order, the order SHALL go back to `DRAFT` and SHALL need to
be submitted again (maintainer decision). Edits of `DRAFT` and `RETURNED` orders SHALL keep their
status. An Admin SHALL be able to edit any order of a non-draft edition, in any status, whether the
orders are open or not. An Admin's edit SHALL NOT change the order status.

The orders closing SHALL take effect atomically. A FiringChief's edit SHALL either commit before
the closing is committed, or be refused. Every edit of an entry SHALL carry the entry's version.
An edit based on an outdated version SHALL be rejected (`409 Conflict`, `entries.modified`).

#### Scenario: Editing a submitted order
- **WHEN** a FiringChief changes the powder of an entry of a `SUBMITTED` order while the orders are open
- **THEN** the entry is saved, the order is `DRAFT` again, and the page says that it must be submitted again

#### Scenario: Validated order is read-only for FiringChiefs
- **WHEN** a FiringChief edits an entry of a `VALIDATED` order
- **THEN** the request is rejected with `409 Conflict` and `orders.validated`

#### Scenario: Orders closed while editing
- **WHEN** an Admin closes the orders and a FiringChief then saves an entry
- **THEN** the request is rejected with `409 Conflict` and `orders.closed`, and the UI shows the order read-only

#### Scenario: Admin edits a validated order
- **WHEN** an Admin changes an entry of a `VALIDATED` order while the orders are closed
- **THEN** the entry is saved and the order stays `VALIDATED`

#### Scenario: Outdated entry edit
- **WHEN** two FiringChiefs of one comparsa open the same entry, and both save
- **THEN** the second save is rejected with `409 Conflict` and `entries.modified`, and the UI reloads the entry

### Requirement: Submitting an order (UC-14)
A FiringChief SHALL submit an order of their scope that is `DRAFT` or `RETURNED`, while the orders
are open. The request SHALL carry the order's version and the attestation that the arquebusiers
of the order meet the requirements (license, course and legal age). Before submitting, the UI
SHALL show the pending compliance warnings of the `ACTIVE` entries. These warnings SHALL NOT block
the submission (BR-04).

An Admin SHALL also be able to submit a `DRAFT` or `RETURNED` order on the comparsa's behalf, at
any time, whether the orders are open or not, without the attestation (maintainer decision). The
order SHALL record that an Admin submitted it.

Submitting SHALL set the status `SUBMITTED`. It SHALL record the user and the time, and either the
FiringChief's attestation or that an Admin submitted it.

The following rules SHALL be blocking:
- a FiringChief's attestation SHALL be confirmed (`400 Bad Request` naming `attestation`);
- the order SHALL be `DRAFT` or `RETURNED` (`409 Conflict`, `orders.invalidTransition`);
- for a FiringChief, the orders SHALL be open (`409 Conflict`, `orders.closed`);
- the version SHALL be current (`409 Conflict`, `orders.modified`);
- every entry SHALL meet the data rules on the current data (`409 Conflict`,
  `orders.entriesInvalid`). The response SHALL list each entry with its reason. These entries
  block the submission:
  - an entry of an arquebusier still in the registry whose owned weapon was removed;
  - an entry whose loaned weapon was removed from the registry;
  - an entry whose rental model is no longer offered.

#### Scenario: FiringChief submits with warnings
- **WHEN** a FiringChief submits a `DRAFT` order with two `ACTIVE` entries that have `COURSE_MISSING`, confirming the attestation
- **THEN** the dialog listed both warnings, the order becomes `SUBMITTED` with the FiringChief and the time, and the warnings remain

#### Scenario: Attestation missing
- **WHEN** a FiringChief submits without confirming the attestation
- **THEN** the request is rejected with `400 Bad Request` naming `attestation`, and the order is unchanged

#### Scenario: Removed owned weapon blocks the submission
- **WHEN** an owned weapon used by an `OWNED` entry is removed from the registry, and the FiringChief submits the order
- **THEN** the request is rejected with `409 Conflict` and `orders.entriesInvalid`, listing that entry with `ownedWeaponMissing`

#### Scenario: Resubmitting a returned order
- **WHEN** a FiringChief fixes a `RETURNED` order and submits it while the orders are open
- **THEN** the order becomes `SUBMITTED`

#### Scenario: Admin submits on the comparsa's behalf
- **WHEN** an Admin submits a `DRAFT` order while the orders are closed, without an attestation
- **THEN** the order becomes `SUBMITTED`, recording the Admin, the time and that an Admin submitted it

#### Scenario: FiringChief cannot submit with closed orders
- **WHEN** a FiringChief submits a `DRAFT` order while the orders are closed
- **THEN** the request is rejected with `409 Conflict` and `orders.closed`

### Requirement: Reviewing orders (UC-15)
Only Admins SHALL review orders. An Admin SHALL:
- validate an order that is `SUBMITTED`, `DRAFT` or `RETURNED`. It becomes `VALIDATED`. Validating
  an order that was never submitted closes it for a comparsa that did not submit it (maintainer
  decision);
- return a `SUBMITTED` or `VALIDATED` order with a `returnReason`. It becomes `RETURNED`, and its
  FiringChiefs see the reason until it is submitted again.

Each review SHALL record the Admin and the time, and SHALL carry the order's version. The
following rules SHALL be blocking:
- any other move SHALL be rejected (`409 Conflict`, `orders.invalidTransition`);
- an outdated version SHALL be rejected (`409 Conflict`, `orders.modified`);
- the `returnReason` SHALL be 1 to 500 characters after trimming (`400 Bad Request` naming
  `returnReason`);
- validating SHALL require every entry to meet the data rules, as for a submission
  (`409 Conflict`, `orders.entriesInvalid`).

Reviews SHALL NOT depend on whether the orders are open. A FiringChief SHALL receive
`403 Forbidden`.

#### Scenario: Admin validates
- **WHEN** an Admin validates a `SUBMITTED` order
- **THEN** the order becomes `VALIDATED` and its FiringChiefs see it read-only

#### Scenario: Admin returns with a reason
- **WHEN** an Admin returns a `SUBMITTED` order with the reason "Check the flask of two arquebusiers"
- **THEN** the order becomes `RETURNED`, and its FiringChiefs see the reason on the order

#### Scenario: Return without a reason
- **WHEN** an Admin returns an order with a reason of only spaces
- **THEN** the request is rejected with `400 Bad Request` naming `returnReason`

#### Scenario: Admin closes an order never submitted
- **WHEN** the orders are closed and an Admin validates the `DRAFT` order of a comparsa that never submitted it
- **THEN** the order becomes `VALIDATED`, without a submission or attestation

#### Scenario: Returning a draft is blocking
- **WHEN** an Admin returns a `DRAFT` order
- **THEN** the request is rejected with `409 Conflict` and `orders.invalidTransition`

#### Scenario: Validating an invalid order is blocking
- **WHEN** an Admin validates an order with an entry whose rental model is no longer offered
- **THEN** the request is rejected with `409 Conflict` and `orders.entriesInvalid`, listing that entry

#### Scenario: FiringChief cannot review
- **WHEN** a FiringChief validates or returns an order through the API
- **THEN** the API responds `403 Forbidden` and the order is unchanged

### Requirement: Order visibility (BR-12)
An Admin SHALL see every order of every edition. A FiringChief SHALL see only the orders of the
comparsas in their scope, for editions that are not `DRAFT`, read-only when they may not edit
them. An order outside the user's scope, or of a draft edition for a FiringChief, SHALL be answered
as if it did not exist (`404 Not Found`). This rule SHALL be blocking and enforced on the server.

As exceptions, for loans:
- the FiringChiefs of the borrower's comparsa SHALL see the lender's name and comparsa, or the
  external owner's name;
- the FiringChiefs of a lender's comparsa SHALL see each loan of their arquebusiers' owned weapons,
  with the weapon, the borrower's name and the borrower's comparsa, but not the rest of the
  borrower's order.

The entries of an order SHALL be sorted by last name and first name, in the Spanish alphabetical
order. The names of arquebusiers no longer in the registry come from the entry's copy.

#### Scenario: Another comparsa's order
- **WHEN** a FiringChief requests the order of a comparsa not assigned to them
- **THEN** the API responds `404 Not Found`

#### Scenario: Lender's comparsa sees the loan
- **WHEN** a FiringChief of "Comparsa Sintética Sur" opens their order, and an owned weapon of one of their arquebusiers is lent to an arquebusier of "Comparsa Sintética Norte"
- **THEN** the page lists the weapon as lent, with the borrower's name and "Comparsa Sintética Norte", and shows no other data of that order

#### Scenario: Past editions
- **WHEN** a FiringChief opens the order of their comparsa for a `CLOSED` edition
- **THEN** the order is shown read-only

### Requirement: Entries after registry changes (BR-13, BR-14)
Entries SHALL keep the edition's figures correct when the registry changes:
- **transfer**: an entry SHALL stay in the order of the comparsa it was added to (BR-13). The
  arquebusier's later entries SHALL go to the new comparsa;
- **deletion while the orders are open** (maintainer decision): deleting an arquebusier SHALL
  remove their entry in the edition in progress, with its loan, in whatever status its order is.
  The order SHALL keep its status, and its totals SHALL no longer count the entry. Before the
  deletion, the registry's confirmation SHALL say which comparsa's order loses the entry (see the
  arquebusier registry capability);
- **deletion otherwise**: the entries of a deleted arquebusier SHALL be kept with all their values
  and their copy of the identity and weapon data (see "Entry history"). This applies to the edition
  in progress once its orders are closed, and to every other edition (maintainer decision). They
  SHALL be shown as "No longer in the registry" and SHALL count in the totals. Admins SHALL still
  be able to edit them, as any order (BR-10). They SHALL have no compliance warnings and no
  first-year flag. A missing owned weapon of such an entry SHALL NOT block a submission or
  validation;
- **owned weapon removed**: an `OWNED` entry or a loan whose owned weapon is removed from the
  registry, or deleted with its owner, SHALL keep its weapon source and its copy of the weapon. It
  SHALL be shown as "weapon removed". While the entry's arquebusier is in the registry, it SHALL
  block the next submission or validation of its order until it is changed;
- **status change**: changing an arquebusier's registry status SHALL NOT change their entries.

These effects SHALL happen in the same transaction as the registry change, and SHALL NOT make the
registry change fail. No registry change SHALL anonymise an entry, and none other than the
deletion SHALL remove one.

#### Scenario: Current entry removed with the arquebusier
- **WHEN** the orders are open and an arquebusier with a 2 kg `ACTIVE` entry in the `DRAFT` order of the edition in progress is deleted, after confirming
- **THEN** the entry no longer exists, the order stays `DRAFT`, and its totals no longer count the 2 kg

#### Scenario: Current entry removed from a submitted order
- **WHEN** the orders are open and an arquebusier whose entry is in a `SUBMITTED` order of the edition in progress is deleted
- **THEN** the entry is removed, the order stays `SUBMITTED`, and the removal is recorded in the audit trail

#### Scenario: Orders closed keep the entry
- **WHEN** the orders of the edition in progress are closed and an arquebusier with an entry in it is deleted
- **THEN** the deletion succeeds, and the entry stays in the order with its values and its copy, marked "No longer in the registry"

#### Scenario: Past entries keep the history
- **WHEN** an arquebusier with a 2 kg `ACTIVE` entry in the `VALIDATED` 2030 order is deleted while 2031 is in progress
- **THEN** the 2030 order still counts 2 kg, and the entry shows their name and national ID from its copy, marked "No longer in the registry"

#### Scenario: No entry can be removed by hand
- **WHEN** a user asks the API to remove an entry
- **THEN** no such operation exists, and the entry is unchanged

#### Scenario: Lender deleted
- **WHEN** an arquebusier whose owned weapon is lent in the current edition is deleted
- **THEN** the deletion succeeds, and the borrower's entry shows the loaned weapon as removed, with its copy of the lender and the weapon

### Requirement: Order totals and dashboard (UC-16)
For every order, the system SHALL compute these totals from its entries, and SHALL never store
them:
- the number of `ACTIVE` and of `RESERVE` entries;
- the powder in kilograms;
- the caps boxes of each `capsType`;
- the weapon rentals of each model;
- the flask rentals of each size;
- the loans, and the `OWNED` entries;
- the `ACTIVE` entries with at least one compliance warning, for the edition in progress.

The orders overview of an edition SHALL list, for an Admin, every active comparsa and every
comparsa with an order in the edition. For a FiringChief, it SHALL list the comparsas in their
scope. Each row SHALL have the comparsa, the order status or "not prepared", and its totals.
Admins SHALL also see the number of orders in each status, "not prepared" included, and the totals
of all the orders of the edition. The totals SHALL include every prepared order, whatever its
status.

#### Scenario: Admin dashboard
- **WHEN** an Admin opens the orders of the current edition, with one order `VALIDATED`, one `SUBMITTED` and one comparsa not prepared
- **THEN** the page shows one validated, one submitted and one not prepared, each comparsa with its totals, and the edition totals of both orders

#### Scenario: Totals by model
- **WHEN** two entries rent "ARCABUZ MORO DIESTRO" and one "TRABUCO CRISTIANO ZURDO"
- **THEN** the totals show 2 and 1 rentals of those models

#### Scenario: FiringChief overview
- **WHEN** a FiringChief assigned to two comparsas opens the orders
- **THEN** only their two comparsas are listed, and no Federation totals are shown

### Requirement: Order changes are audited
Every preparation, entry addition, entry edit, submission, validation and return SHALL be recorded
in the audit trail in the same transaction as the change. So SHALL the removal of a current-edition
entry by an arquebusier's deletion while the orders are open, with the order and the comparsa and
without personal data. Each entry SHALL record the acting user,
the action, the order and the comparsa. Entry edits SHALL record the entry and the names of the
changed fields, without personal values. Status changes SHALL record the previous and the new
status. A submission SHALL record whether a FiringChief attested it or an Admin submitted it, and
the number of `ACTIVE` entries with warnings. A return SHALL NOT copy the reason into the audit
entry. Refreshing the copies of identity and weapon data SHALL NOT record entries of its own. An
operation that changes nothing SHALL NOT record an entry. A rejected request SHALL NOT record an
entry.

#### Scenario: Submission audited
- **WHEN** a FiringChief submits an order
- **THEN** one audit entry records the FiringChief, the order, the comparsa, `DRAFT` to `SUBMITTED`, the attestation and the number of entries with warnings

#### Scenario: Admin submission audited
- **WHEN** an Admin submits an order on the comparsa's behalf
- **THEN** one audit entry records the Admin, the order, the comparsa, `DRAFT` to `SUBMITTED` and that an Admin submitted it

#### Scenario: External owner not in the audit trail
- **WHEN** a FiringChief saves a loan from an external owner
- **THEN** the audit entry names the changed fields and contains neither the owner's name nor their national ID

### Requirement: Orders screens
The UI SHALL offer every signed-in user an "Orders" item in the main navigation, opening the orders
overview of the current edition. Each edition that is not `DRAFT` SHALL link from its detail page
to its own orders overview. With no current edition, the page SHALL say so and link to the
editions.

The **overview** SHALL follow the list template for FiringChiefs and the dashboard template for
Admins. It SHALL show whether the orders are open, the order rows with their status and totals,
and a "Prepare order" action for comparsas not prepared, when the user may prepare them. A
FiringChief with a single comparsa whose order is prepared SHALL be taken directly to it.

The **order page** SHALL follow the detail template. Its header SHALL show the comparsa with its
logo, the edition, the order status and whether the user may edit it, and why not. Its key facts
SHALL show the totals. It SHALL show:
- the return reason, while the order is `RETURNED`;
- a warning message with the number of `ACTIVE` entries with compliance warnings, linking to them;
- a message listing the entries that block the submission, when there are any;
- the entries as a table: name, first-year flag, status, powder, caps, weapon, flask and warnings,
  each entry with an "Edit" action when the user may edit it. Entries of arquebusiers no longer in
  the registry SHALL be marked so. No entry SHALL have a remove action;
- the arquebusiers not in the order, with "Add";
- the weapons of its arquebusiers lent to others.

The actions SHALL be confirmed in a dialog:
- "Submit order" for FiringChiefs. The submit dialog SHALL list the pending warnings and SHALL
  require the attestation checkbox;
- for Admins, "Submit on behalf of the comparsa" for a `DRAFT` or `RETURNED` order, without the
  attestation;
- for Admins, "Validate" for a `SUBMITTED`, `DRAFT` or `RETURNED` order. For an order that was not
  submitted, the dialog SHALL say that the comparsa did not submit it;
- for Admins, "Return", whose dialog SHALL require the reason.

A `SUBMITTED` order submitted by an Admin SHALL say so.

The **entry panel** SHALL be a side panel (a bottom sheet on phones):
- choosing `RESERVE` SHALL clear and disable powder, caps, weapon and flask;
- the weapon SHALL offer the arquebusier's owned weapons, the models offered for rental, or a
  loan;
- for a loan, the user SHALL type the owner's DNI/NIE. When the lookup finds the owner, the user
  chooses one of the listed weapons. Otherwise the panel SHALL ask for the external owner's name,
  surnames and the weapon's model, number and ownership guide.

The forms SHALL validate the same blocking rules as the server before submitting, and SHALL show a
rejected change's translated reason. A refusal because the orders closed or the order changed
SHALL reload the order and show it in its new state. Numbers and dates SHALL be shown in the user's
language. The pages SHALL work on a phone (NFR-01). Every text SHALL be available in es-ES,
ca-ES-valencia and en. The pages SHALL pass automated accessibility checks (NFR-07).

#### Scenario: FiringChief prepares and edits on a phone
- **WHEN** the seeded FiringChief opens "Orders" on a 360 px wide screen while the orders are open, prepares the order and edits an entry
- **THEN** the order page shows the pre-filled entries without horizontal scrolling of the page, and the entry is saved from a bottom sheet

#### Scenario: Reserve clears the fields
- **WHEN** a user sets an entry to `RESERVE` in the panel
- **THEN** powder, caps, weapon and flask are cleared and disabled

#### Scenario: Loan from an unknown owner
- **WHEN** a user types a valid DNI in the loan lookup that no arquebusier has
- **THEN** the panel asks for the external owner's name, surnames and the weapon's model, number and ownership guide

#### Scenario: Submit dialog
- **WHEN** a FiringChief chooses "Submit order" on an order with three entries with warnings
- **THEN** the dialog lists the three entries and their warnings in words, and "Submit" is enabled only after the attestation checkbox is checked

#### Scenario: Admin validates an order never submitted
- **WHEN** an Admin chooses "Validate" on a `DRAFT` order
- **THEN** the dialog says that the comparsa did not submit the order, and confirming makes it `VALIDATED`

#### Scenario: Read-only after closing
- **WHEN** a FiringChief opens their `SUBMITTED` order after the orders are closed
- **THEN** the page says the orders are closed and offers no edit, add or submit action

#### Scenario: No current edition
- **WHEN** a user opens "Orders" while no edition is in progress
- **THEN** the page says that no edition is in progress and links to the editions

### Requirement: Synthetic order data
The synthetic seed SHALL create:
- `VALIDATED` orders of the closed past edition, so the first-year flag is known in the current
  edition, and some seeded arquebusiers have no `ACTIVE` entry in it;
- orders of the current edition in `DRAFT` and `SUBMITTED`, and a seeded comparsa with no order;
- entries covering every weapon source and flask, both caps types, `RESERVE` entries and entries
  with compliance warnings;
- a loan between two seeded comparsas, and a loan from a synthetic external owner with a valid
  synthetic DNI;
- an entry of the past edition whose arquebusier is no longer in the registry, kept with its
  synthetic copy of the identity;
- `ACTIVE` entries without powder and without a weapon.

It SHALL use fixed identifiers, SHALL be safe to run again, and SHALL NOT contain real names,
national IDs, weapon numbers or ownership guides.

#### Scenario: Seeding twice
- **WHEN** the seed runs twice
- **THEN** the orders, entries and loans exist once

#### Scenario: Seeded orders on the dashboard
- **WHEN** an Admin opens the orders of the seeded current edition
- **THEN** the dashboard shows orders in `DRAFT` and `SUBMITTED` and a comparsa not prepared

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
