# arquebusier-registry Specification

## Purpose
The Federation-wide registry of arquebusiers: identity data with DNI/NIE validation, the current
license and training course, Active/Reserve status, owned weapons, transfers between comparsas and
deletion (UC-01..05, UC-29). It is the single source of truth that orders, exports and badges read,
and FiringChiefs see only their own comparsas in it (BR-12).

## Requirements

### Requirement: Arquebusier data
An `Arquebusier` SHALL have:
- a `federationId`: a whole number from 1 to 999999999;
- a `nationalId`: a DNI or NIE (see "National ID validation (BR-01)");
- a `firstName` and a `lastName`: each 1 to 100 characters after trimming;
- a `birthDate`: not in the future and not before 1900-01-01;
- an optional `email`: a valid address of at most 254 characters;
- an optional `phone`: at most 20 characters, made of digits and spaces with an optional leading `+`;
- a `gender`: `MALE`, `FEMALE` or `UNSPECIFIED`;
- a `comparsa`: an existing comparsa;
- a `status`: `ACTIVE` or `RESERVE`.

Names SHALL reject line breaks and control or invisible characters. Text fields SHALL be stored
trimmed. Age SHALL be derived from `birthDate` and never stored. These rules SHALL be blocking:
a request that breaks one SHALL be rejected with `400 Bad Request` naming each invalid field, and
nothing SHALL be stored.

#### Scenario: FiringChief registers an arquebusier
- **WHEN** a FiringChief of "Comparsa Sintética Norte" registers "Arcabucera Sintética Uno" with federationId 900001, nationalId "12345678Z", birthDate 1990-05-01 and gender `FEMALE` in that comparsa
- **THEN** the arquebusier is stored with status `ACTIVE` and returned with its identifier

#### Scenario: Optional contact data
- **WHEN** an arquebusier is registered without email or phone
- **THEN** the arquebusier is stored with no email and no phone

#### Scenario: Invalid fields are blocking
- **WHEN** an arquebusier is submitted with an empty first name, a birthDate in the future, federationId 0, the email "not-an-email" and gender `OTHER`
- **THEN** the request is rejected with `400 Bad Request` naming `firstName`, `birthDate`, `federationId`, `email` and `gender`, and nothing is stored

### Requirement: National ID validation (BR-01)
The `nationalId` SHALL be normalised before validation and storage: surrounding and inner spaces,
tabs and hyphens SHALL be removed and letters SHALL be upper-cased. Any other separator, such as
a no-break space or a Unicode dash, SHALL make the value invalid. After normalisation it SHALL be
either:
- a DNI: 8 digits followed by the check letter for those digits; or
- an NIE: `X`, `Y` or `Z` followed by 7 digits and the check letter computed after replacing the
  prefix with 0, 1 or 2.

Only ASCII digits and Latin letters SHALL be accepted. Look-alike characters from other scripts
(for example a Cyrillic "Х") and full-width digits SHALL be rejected. This rule SHALL be blocking:
an invalid value SHALL be rejected with `400 Bad Request` naming `nationalId`. The UI SHALL
validate it the same way before submitting.

#### Scenario: Valid DNI with spaces and lower case
- **WHEN** an arquebusier is registered with nationalId " 12345678-z "
- **THEN** it is stored as "12345678Z"

#### Scenario: Valid NIE
- **WHEN** an arquebusier is registered with nationalId "X1234567L"
- **THEN** it is stored as "X1234567L"

#### Scenario: Wrong check letter is blocking
- **WHEN** an arquebusier is registered with nationalId "12345678A"
- **THEN** the request is rejected with `400 Bad Request` naming `nationalId`

#### Scenario: Look-alike character is blocking
- **WHEN** an arquebusier is registered with an NIE whose prefix is the Cyrillic letter "Х" instead of the Latin "X"
- **THEN** the request is rejected with `400 Bad Request` naming `nationalId`

### Requirement: Federation-wide uniqueness (BR-02)
The `nationalId` and the `federationId` SHALL each be unique across the whole Federation, whatever
the comparsa. This rule SHALL be blocking and SHALL hold under concurrent requests: a duplicate
SHALL be rejected with `409 Conflict` naming which of the two values is taken. The response SHALL
NOT reveal the comparsa or any data of the existing arquebusier. The UI SHALL tell a FiringChief
to contact the Federation, and an Admin to search the registry.

#### Scenario: Duplicate nationalId in another comparsa
- **WHEN** a FiringChief registers nationalId "12345678Z" while an arquebusier of a comparsa outside their scope already has it
- **THEN** the request is rejected with `409 Conflict` for `nationalId`, and the response contains no data of the other arquebusier

#### Scenario: Duplicate federationId
- **WHEN** an Admin changes an arquebusier's federationId to one that another arquebusier has
- **THEN** the request is rejected with `409 Conflict` for `federationId` and nothing is changed

#### Scenario: Concurrent duplicates
- **WHEN** two requests register the same nationalId at the same time
- **THEN** exactly one succeeds and the other is rejected with `409 Conflict`

### Requirement: Registering and editing arquebusiers (UC-01, UC-02)
A FiringChief SHALL be able to register arquebusiers in, and edit arquebusiers of, the comparsas
in their scope. An Admin SHALL be able to do so for every comparsa. The following rules SHALL be
blocking:
- a FiringChief SHALL NOT register an arquebusier in a comparsa outside their scope; the comparsa
  SHALL be answered as not found (`404 Not Found`);
- a new arquebusier SHALL NOT be registered in an inactive comparsa (`409 Conflict`);
- editing SHALL NOT change the comparsa; only a transfer does (see "Transfer between comparsas
  (UC-29, BR-13)");
- an edit SHALL carry the version of the arquebusier it was based on. An edit based on an outdated
  version SHALL be rejected with `409 Conflict`, so a change made meanwhile by another user is
  never overwritten silently.

Arquebusiers of an inactive comparsa SHALL stay editable. Saving without changing any value SHALL
succeed without recording anything.

#### Scenario: FiringChief edits their arquebusier
- **WHEN** a FiringChief changes the phone of an arquebusier of their comparsa
- **THEN** the arquebusier is updated

#### Scenario: Registering outside the scope
- **WHEN** a FiringChief registers an arquebusier in a comparsa they are not assigned to
- **THEN** the request is answered `404 Not Found` and nothing is stored

#### Scenario: Registering in an inactive comparsa
- **WHEN** an Admin registers an arquebusier in an inactive comparsa
- **THEN** the request is rejected with `409 Conflict` and a translated explanation

#### Scenario: Outdated edit
- **WHEN** two FiringChiefs of the same comparsa open the same arquebusier, the first saves a change and the second then saves theirs
- **THEN** the second save is rejected with `409 Conflict`, and the UI explains that the arquebusier changed and reloads it

### Requirement: Arquebusier visibility (BR-12)
An Admin SHALL see every arquebusier. A FiringChief SHALL see only the arquebusiers of the
comparsas in their scope. An arquebusier outside the user's scope SHALL be answered as if it did
not exist (`404 Not Found`) by every operation on it. This rule SHALL be blocking and SHALL be
enforced on the server.

The list SHALL be sorted by last name and then first name, in the Spanish alphabetical order. It
SHALL be filterable by comparsa and by status. In the UI it SHALL be searchable by name, nationalId
or federationId, ignoring letter case and accents. Each row SHALL show the name, the nationalId, the
federationId, the comparsa, the status and the license status.

#### Scenario: FiringChief lists arquebusiers
- **WHEN** a FiringChief assigned to two comparsas opens the arquebusiers page
- **THEN** the arquebusiers of both comparsas are listed and of no other comparsa, and they can filter by either comparsa

#### Scenario: FiringChief opens another comparsa's arquebusier
- **WHEN** a FiringChief requests, edits or deletes an arquebusier of a comparsa outside their scope
- **THEN** the API responds `404 Not Found`, nothing is changed, and the UI shows the not-found page

#### Scenario: Admin filters by comparsa
- **WHEN** an Admin filters the arquebusiers by "Comparsa Sintética Sur" and status `RESERVE`
- **THEN** only the reserve arquebusiers of that comparsa are listed

#### Scenario: Accent-insensitive search
- **WHEN** a user searches "garcia" in the arquebusiers list
- **THEN** arquebusiers whose last name contains "García" are listed

#### Scenario: FiringChief without assignments
- **WHEN** a FiringChief with no comparsa assigned opens the arquebusiers page
- **THEN** an empty state explains that no comparsa is assigned to them yet and no register action is offered

### Requirement: Current license (UC-02, BR-03)
An arquebusier SHALL have at most one license, the current one. The license has no number and no
history. A license SHALL have a `type` (`AE` or `A_PROF`) and SHALL be either pending or issued:
- a pending license SHALL have no dates;
- an issued license SHALL have an `issuedOn` date, not in the future, and an `expiresOn` date later
  than `issuedOn`.

When `expiresOn` is not given, it SHALL default to `issuedOn` plus 5 years for `AE` and plus 1 year
for `A_PROF` (BR-03). The UI SHALL pre-fill it the same way and SHALL let the user change it.
Renewing SHALL replace the previous type and dates. The license MAY be removed when it was entered
by mistake.

The license status SHALL be derived, never stored:
- `PENDING` for a pending license;
- `VALID` while today, in the Europe/Madrid time zone, is on or before `expiresOn`;
- `EXPIRED` afterwards;
- no status when the arquebusier has no license.

The date and type rules SHALL be blocking (`400 Bad Request` naming the field). An expired or
missing license SHALL NOT block registration or editing: compliance warnings belong to the
compliance insights capability.

#### Scenario: AE expiry default
- **WHEN** an AE license issued on 2024-03-10 is saved without an expiry date
- **THEN** it is stored with `expiresOn` 2029-03-10 and status `VALID`

#### Scenario: A-PROF expiry default
- **WHEN** an A_PROF license issued on 2026-02-01 is saved without an expiry date
- **THEN** it is stored with `expiresOn` 2027-02-01

#### Scenario: Edited expiry date
- **WHEN** an AE license issued on 2024-03-10 is saved with `expiresOn` 2028-12-31
- **THEN** `expiresOn` 2028-12-31 is kept

#### Scenario: Pending license
- **WHEN** a license of type `AE` is saved as pending
- **THEN** it has no dates and its status is `PENDING`

#### Scenario: Expired license is derived
- **WHEN** an arquebusier's license has `expiresOn` before today
- **THEN** its status is `EXPIRED` and the arquebusier can still be edited

#### Scenario: Invalid license dates are blocking
- **WHEN** a license is saved with `expiresOn` before `issuedOn`, or with `issuedOn` in the future, or as pending with dates
- **THEN** the request is rejected with `400 Bad Request` naming the invalid field

### Requirement: Training course (UC-03)
An arquebusier SHALL have an optional `trainingCompletedOn` date. An empty value SHALL mean the
course is not done. The date SHALL NOT be in the future (blocking, `400 Bad Request`). PolvorApp
SHALL record only whether the course was done and when.

#### Scenario: Course recorded
- **WHEN** a FiringChief records that an arquebusier completed the course on 2025-11-15
- **THEN** the arquebusier shows the course as done on that date

#### Scenario: Course date in the future is blocking
- **WHEN** a course date after today is saved
- **THEN** the request is rejected with `400 Bad Request` naming `trainingCompletedOn`

### Requirement: Active and Reserve status (UC-05)
The `status` SHALL be `ACTIVE` (fires) or `RESERVE` (does not fire but stays on the list). It
SHALL be the arquebusier's only status. A new arquebusier SHALL be `ACTIVE` unless `RESERVE` is
chosen. A FiringChief SHALL be able to change the status of the arquebusiers in their scope, and
an Admin of any arquebusier. A person who leaves the Federation SHALL be deleted, not given another
status.

#### Scenario: Move to reserve
- **WHEN** a FiringChief sets an arquebusier of their comparsa to `RESERVE`
- **THEN** the arquebusier is listed with status `RESERVE` and keeps all their data

#### Scenario: Unknown status is blocking
- **WHEN** a status `INACTIVE` is submitted
- **THEN** the request is rejected with `400 Bad Request` naming `status`

### Requirement: Owned weapons (UC-04)
An arquebusier SHALL have zero or more `OwnedWeapon`s. Each one SHALL have:
- a `WeaponModel` from the catalogue, of any kind including `PISTOL`;
- a `weaponNumber` of 1 to 30 characters;
- an `ownershipGuideNumber` of 1 to 30 characters, stored trimmed and upper-cased.

The users who can edit an arquebusier SHALL be able to add, edit and remove that arquebusier's
owned weapons. The following rules SHALL be blocking:
- the weapon model SHALL exist (`400 Bad Request` naming `weaponModelId`);
- an owned weapon SHALL get only an active model when it is added or its model is changed
  (`409 Conflict`). An existing owned weapon SHALL keep an inactive model while it is not changed;
- the `ownershipGuideNumber` SHALL be unique across the whole Federation, compared ignoring letter
  case (`409 Conflict`). As with BR-02, the response SHALL NOT reveal the other owner;
- the `weaponNumber` SHALL NOT need to be unique.

The model's kind, side, handedness and size SHALL be shown as translated labels, with the
Federation label of the model.

#### Scenario: FiringChief adds an owned weapon
- **WHEN** a FiringChief adds to an arquebusier of their comparsa a weapon of the active model "ARCABUZ MORO DIESTRO", weaponNumber "1234" and ownershipGuideNumber "ab-123-45"
- **THEN** the owned weapon is stored with ownershipGuideNumber "AB-123-45" and listed on the arquebusier

#### Scenario: Owned pistol
- **WHEN** an owned weapon of the model "PISTOLA" is added
- **THEN** the owned weapon is stored

#### Scenario: Inactive model is blocking for new weapons
- **WHEN** an owned weapon is added with an inactive weapon model
- **THEN** the request is rejected with `409 Conflict` and a translated explanation

#### Scenario: Existing weapon keeps an inactive model
- **WHEN** an Admin deactivates a model that an owned weapon uses, and a FiringChief then edits that weapon's number without changing the model
- **THEN** the weapon is updated and keeps the inactive model

#### Scenario: Duplicate ownership guide is blocking
- **WHEN** an owned weapon is added with ownershipGuideNumber "ab-123-45" while another owned weapon in any comparsa has "AB-123-45"
- **THEN** the request is rejected with `409 Conflict` for `ownershipGuideNumber` and the response contains no data of the other owner

#### Scenario: Same weapon number in two weapons
- **WHEN** two owned weapons with different ownership guides have the weaponNumber "1234"
- **THEN** both are stored

#### Scenario: FiringChief cannot touch another comparsa's weapons
- **WHEN** a FiringChief adds, edits or removes an owned weapon of an arquebusier outside their scope
- **THEN** the API responds `404 Not Found` and nothing is changed

### Requirement: Transfer between comparsas (UC-29, BR-13)
An Admin SHALL be able to transfer an arquebusier to another comparsa. The arquebusier SHALL keep
all their data, license, course, status and owned weapons. The following rules SHALL be blocking:
- the target comparsa SHALL exist (`404 Not Found`) and SHALL be active (`409 Conflict`);
- the target SHALL differ from the current comparsa (`409 Conflict`);
- only Admins SHALL transfer. A FiringChief SHALL receive `403 Forbidden` and SHALL NOT see the
  action in the UI.

After a transfer, the FiringChiefs of the previous comparsa SHALL no longer see the arquebusier
on their next request, and those of the new comparsa SHALL see it. A transfer SHALL affect future
editions only (BR-13).

#### Scenario: Admin transfers an arquebusier
- **WHEN** an Admin transfers an arquebusier with one owned weapon from "Comparsa Sintética Norte" to "Comparsa Sintética Sur"
- **THEN** the arquebusier and their owned weapon belong to "Comparsa Sintética Sur"
- **AND** a FiringChief assigned only to "Comparsa Sintética Norte" gets `404 Not Found` for that arquebusier

#### Scenario: Transfer to an inactive comparsa is blocking
- **WHEN** an Admin transfers an arquebusier to an inactive comparsa
- **THEN** the request is rejected with `409 Conflict` and nothing is changed

#### Scenario: FiringChief cannot transfer
- **WHEN** a FiringChief requests a transfer, even between two comparsas both in their scope
- **THEN** the API responds `403 Forbidden` and nothing is changed

### Requirement: Deleting an arquebusier (UC-05, BR-14)
A FiringChief SHALL be able to delete an arquebusier in their scope, and an Admin any arquebusier,
when the person leaves the Federation. The UI SHALL ask for a confirmation that names the
arquebusier and says that the deletion cannot be undone. The confirmation SHALL also remind the
user that an arquebusier who only stops firing is set to `RESERVE`, which keeps their data, and
that registering them again later starts from empty data. Deletion SHALL erase the arquebusier's
personal data and owned weapons. After a deletion:
- the arquebusier SHALL no longer be found (`404 Not Found`);
- their `nationalId`, `federationId` and ownership guide numbers MAY be registered again;
- no audit entry SHALL contain their personal data.

#### Scenario: FiringChief deletes an arquebusier
- **WHEN** a FiringChief deletes an arquebusier of their comparsa who has two owned weapons, and confirms
- **THEN** the arquebusier and both owned weapons no longer exist, and a request for the arquebusier responds `404 Not Found`

#### Scenario: Data can be registered again
- **WHEN** an arquebusier is deleted and a new arquebusier is then registered with the same nationalId and federationId
- **THEN** the new arquebusier is stored

#### Scenario: Confirmation suggests Reserve
- **WHEN** a user opens the delete confirmation of an `ACTIVE` arquebusier
- **THEN** the dialog says that the deletion cannot be undone and that an arquebusier who only stops firing should be set to `RESERVE` instead

#### Scenario: Cancelled deletion
- **WHEN** a user opens the delete confirmation and cancels
- **THEN** nothing is deleted

### Requirement: Comparsas and weapon models in use
A comparsa SHALL be in use while any arquebusier belongs to it, and a weapon model SHALL be in use
while any owned weapon references it. The federation catalog SHALL refuse to delete a comparsa or
weapon model in use (`409 Conflict`). This rule SHALL be blocking and SHALL also hold when the
deletion races with a registration, a transfer or an owned-weapon change. Deactivating a comparsa or
weapon model in use SHALL stay possible.

#### Scenario: Comparsa with arquebusiers cannot be deleted
- **WHEN** an Admin deletes a comparsa that has one arquebusier
- **THEN** the request is rejected with `409 Conflict` and the UI suggests deactivating the comparsa

#### Scenario: Weapon model with owned weapons cannot be deleted
- **WHEN** an Admin deletes a weapon model that one owned weapon references
- **THEN** the request is rejected with `409 Conflict` and nothing is deleted

#### Scenario: Comparsa free again after the last arquebusier leaves
- **WHEN** the only arquebusier of a comparsa is transferred away and an Admin then deletes the comparsa
- **THEN** the comparsa is deleted

### Requirement: Registry changes are audited
Every registration, edit (including license, course and status), transfer and deletion of an
arquebusier, and every addition, edit and removal of an owned weapon, SHALL be recorded in the
audit trail in the same transaction as the change. Each entry SHALL record the acting user, the
action, the arquebusier and the comparsa concerned. A transfer SHALL record the previous and the
new comparsa. To keep erasure complete (BR-14, SEC-09), audit entries SHALL name the changed fields
but SHALL NOT contain personal values: no names, nationalId, federationId, birthDate, contact data,
gender, license dates or weapon and ownership guide numbers. An operation that changes nothing
SHALL NOT record an entry.

#### Scenario: Edit audited without personal values
- **WHEN** a FiringChief changes an arquebusier's nationalId and phone
- **THEN** one audit entry records the FiringChief, the arquebusier, the comparsa and the changed fields `nationalId` and `phone`, and contains neither the old nor the new values

#### Scenario: Transfer audited with both comparsas
- **WHEN** an Admin transfers an arquebusier
- **THEN** an audit entry records the previous and the new comparsa

#### Scenario: Deletion audited without personal data
- **WHEN** an arquebusier is deleted
- **THEN** an audit entry records the actor, the arquebusier identifier, the comparsa and the number of owned weapons removed, and no personal data

### Requirement: Registry screens
The UI SHALL offer, to every signed-in user, an Arquebusiers page in the main navigation, a page to
register an arquebusier and a detail page to edit one. The detail page SHALL group personal data,
license, training course and owned weapons, and SHALL offer the delete action, and for Admins the
transfer action. The comparsa SHALL be chosen among the active comparsas in the user's scope, and
pre-selected when there is only one. The forms SHALL validate the same blocking rules as the
server before submitting and SHALL show a rejected change's translated reason. Dates SHALL be shown
in the user's language. The pages SHALL work on a phone (NFR-01). Every text SHALL be available in
es-ES, ca-ES-valencia and en.

#### Scenario: Single comparsa pre-selected
- **WHEN** a FiringChief assigned to one active comparsa opens the register page
- **THEN** that comparsa is pre-selected

#### Scenario: Client-side validation
- **WHEN** a user types the nationalId "12345678A" and leaves the field
- **THEN** the form shows that the check letter is wrong, without calling the server

#### Scenario: Transfer only for Admins
- **WHEN** a FiringChief opens an arquebusier's detail page
- **THEN** no transfer action is shown

### Requirement: Synthetic registry data
The synthetic seed SHALL create fictional arquebusiers in the seeded comparsas, with valid
synthetic DNI and NIE values, covering `ACTIVE` and `RESERVE`, every license status, no license,
course done and not done, and owned weapons including a pistol. It SHALL use fixed identifiers and
SHALL be safe to run again. It SHALL NOT contain real names, national IDs, contact data or
ownership guides.

#### Scenario: Seeding twice
- **WHEN** the seed command runs twice on the same database
- **THEN** the same arquebusiers and owned weapons exist once each

#### Scenario: Seeded FiringChief sees their arquebusiers
- **WHEN** the seeded FiringChief "Jefa Sintética Dos" signs in and opens the arquebusiers page
- **THEN** only the seeded arquebusiers of the comparsas assigned to them are listed
