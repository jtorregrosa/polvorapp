# Spec Delta

## MODIFIED Requirements

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
- `RENTAL` SHALL name a model offered for rental in the edition (`notOffered`, BR-07), of any kind;
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

#### Scenario: Renting an offered pistol
- **WHEN** a FiringChief saves an `ACTIVE` entry with weapon source `RENTAL` and the "PISTOLA" model, which the edition offers for rental
- **THEN** the entry is saved, and the rental counts in the order's totals and billing like any other rental
