# arquebusier-registry Specification

## Purpose
The Federation-wide registry of arquebusiers: identity data with DNI/NIE validation, the current
license and training course, Active/Reserve status, owned weapons, transfers between comparsas,
deletion and the spreadsheet import of the initial load (UC-01..05, UC-09, UC-29). It is the single source of truth that orders, exports and badges read,
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
not exist (`404 Not Found`) by every operation on it, including its photos. This rule SHALL be
blocking and SHALL be enforced on the server.

The list SHALL be sorted by last name and then first name, in the Spanish alphabetical order. It
SHALL be filterable by comparsa, by status, by license state (expiring soon, expired, pending or no
license) and by compliance warning: any warning, or one chosen warning (see the compliance insights
capability). The comparsa, status, license state and warning filters SHALL be kept in the page
address, so a link can open the list already filtered. In the UI the list SHALL be searchable by
name, nationalId or federationId, ignoring letter case and accents. Above the list, the UI SHALL
show counters for active, reserve, expiring-license, expired-license, pending-license and
no-license arquebusiers within the user's scope. Each counter SHALL also be a filter that the user
can switch on and off, and its pressed state SHALL be exposed to assistive technology. The UI SHALL
announce the number of listed arquebusiers whenever a filter or the search changes it. Each row
SHALL show:
- the name, the comparsa, the nationalId, the federationId and the status;
- the license status with its expiry date. A `VALID` license with the warning `LICENSE_EXPIRING`
  SHALL be shown as "Expiring soon";
- whether the arquebusier has an ID photo;
- the number of the arquebusier's compliance warnings and their names.

The list response SHALL carry each arquebusier's compliance warnings. It SHALL carry neither the
birth date nor any image.

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
- **THEN** an empty state explains that no comparsa is assigned to them yet, and no register action and no counters are offered

#### Scenario: Rows say whether there is an ID photo
- **WHEN** an Admin lists arquebusiers, one with an ID photo and one without
- **THEN** each row says whether the arquebusier has an ID photo, and the list response contains no image

#### Scenario: Counters follow the scope
- **WHEN** the seeded FiringChief "Jefa Sintética Dos" opens the arquebusiers page
- **THEN** the counters count only the arquebusiers of "Comparsa Sintética Norte"

#### Scenario: Counter as a filter
- **WHEN** a user presses the expired-license counter
- **THEN** only arquebusiers whose license status is `EXPIRED` are listed, the counter shows as pressed, the new number of results is announced, and pressing it again lists everyone

#### Scenario: Expiring-license counter
- **WHEN** a user presses the expiring-license counter
- **THEN** only arquebusiers with the warning `LICENSE_EXPIRING` are listed, and their rows show the license as "Expiring soon" with its expiry date

#### Scenario: Filter by warning
- **WHEN** a user chooses the warning `UNDER_AGE` in the warning filter
- **THEN** only arquebusiers with that warning are listed, and the page address keeps the filter

#### Scenario: Any warning
- **WHEN** a user opens the list from a link filtered by any warning
- **THEN** only arquebusiers with at least one compliance warning are listed

#### Scenario: Rows show their warnings
- **WHEN** a user lists an arquebusier with the warnings `COURSE_MISSING` and `ID_PHOTO_MISSING`
- **THEN** the row shows two warnings with their translated names, and the list response contains the warnings but not the birth date

#### Scenario: Filter with no results
- **WHEN** a user combines the expired-license counter with a comparsa that has no expired license
- **THEN** a translated "no arquebusier matches" message is shown with an action that clears the filters

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

The license `frontPhoto` and `backPhoto` belong to the current license (see "Arquebusier photos
(UC-01, UC-02)"):
- removing the license SHALL erase both photos in the same change, and the UI SHALL warn about it
  before saving;
- changing the license type or dates, as in a renewal, SHALL keep the photos until new ones are
  uploaded (maintainer decision). After such a change, the UI SHALL remind the user to replace the
  photos if the license was renewed.

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

#### Scenario: Removing the license erases its photos
- **WHEN** a FiringChief removes the license of an arquebusier that has a `frontPhoto` and a `backPhoto`, after the UI warned that the photos will be deleted
- **THEN** the arquebusier has no license and no license photos, and both images are erased

#### Scenario: Renewal keeps the photos until replaced
- **WHEN** a FiringChief changes the `issuedOn` and `expiresOn` of a license that has both photos
- **THEN** both photos are kept, and the UI reminds the user to replace them if the license was renewed

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
personal data, owned weapons and photos. After a deletion:
- the arquebusier SHALL no longer be found (`404 Not Found`), nor SHALL their photos;
- their stored photo images SHALL be erased (see "Stored photo cleanup (BR-14, SEC-08)");
- their `nationalId`, `federationId` and ownership guide numbers MAY be registered again;
- no audit entry SHALL contain their personal data.

#### Scenario: FiringChief deletes an arquebusier
- **WHEN** a FiringChief deletes an arquebusier of their comparsa who has two owned weapons, and confirms
- **THEN** the arquebusier and both owned weapons no longer exist, and a request for the arquebusier responds `404 Not Found`

#### Scenario: Photos are erased with the arquebusier
- **WHEN** an arquebusier with an `idPhoto`, a `frontPhoto` and a `backPhoto` is deleted
- **THEN** requests for those photos respond `404 Not Found` and the three stored images are erased

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
arquebusier, every addition, edit and removal of an owned weapon, and every upload, replacement
and removal of a photo, SHALL be recorded in the audit trail in the same transaction as the change.
Each entry SHALL record the acting user, the action, the arquebusier and the comparsa concerned. A
transfer SHALL record the previous and the new comparsa. A photo entry SHALL record the photo kind
and whether a previous photo was replaced. An edit that removes the license SHALL record which
photos were removed with it. To keep erasure complete (BR-14, SEC-09), audit entries SHALL name the
changed fields but SHALL NOT contain personal values: no names, nationalId, federationId,
birthDate, contact data, gender, license dates, weapon and ownership guide numbers, image data or
stored photo names. An operation that changes nothing SHALL NOT record an entry.

#### Scenario: Edit audited without personal values
- **WHEN** a FiringChief changes an arquebusier's nationalId and phone
- **THEN** one audit entry records the FiringChief, the arquebusier, the comparsa and the changed fields `nationalId` and `phone`, and contains neither the old nor the new values

#### Scenario: Transfer audited with both comparsas
- **WHEN** an Admin transfers an arquebusier
- **THEN** an audit entry records the previous and the new comparsa

#### Scenario: Deletion audited without personal data
- **WHEN** an arquebusier is deleted
- **THEN** an audit entry records the actor, the arquebusier identifier, the comparsa, the number of owned weapons removed and the number of photos removed, and no personal data

#### Scenario: Photo replacement audited
- **WHEN** a FiringChief replaces an arquebusier's `idPhoto`
- **THEN** one audit entry records the FiringChief, the arquebusier, the comparsa, the kind `ID` and that a photo was replaced, and contains no image data or stored name

#### Scenario: Failed upload is not audited
- **WHEN** a photo upload is rejected with `400 Bad Request`
- **THEN** no audit entry is recorded

### Requirement: Registry screens
The UI SHALL offer, to every signed-in user:
- an Arquebusiers page in the main navigation;
- a page to register an arquebusier;
- a detail page for each arquebusier.

The detail page SHALL be read-only, following the "Detail pages in read mode" template. Its header
SHALL show the ID photo, the comparsa and its side, the full name, the status, the license state and
the course state. A `VALID` license with the warning `LICENSE_EXPIRING` SHALL be shown as "Expiring
soon". Its key facts SHALL show the license expiry with how much of the license's validity has
passed, the federationId and nationalId, the course date and the number of owned weapons.

The detail response SHALL carry the arquebusier's compliance warnings, as derived by the compliance
insights capability. When there is at least one, the page SHALL show them in one warning message.
The message SHALL list each warning in words, with the license expiry date for the expired and
expiring warnings and the age for `UNDER_AGE`. It SHALL say that the warnings never block saving
(BR-04). The page SHALL NOT derive warnings of its own. After an edit, the warnings SHALL be shown
as the server derives them for the saved data.

The detail SHALL group personal data, license, training course and owned weapons. Each group SHALL
have its own edit action, opened in a side panel (a bottom sheet on phones), and owned weapons SHALL
keep their add, edit and remove actions. The status change and the delete action SHALL be in a "More
actions" menu. The transfer action SHALL be offered to Admins only.

The comparsa SHALL be chosen among the active comparsas in the user's scope, and pre-selected when
there is only one. The forms and edit panels SHALL validate the same blocking rules as the server
before submitting and SHALL show a rejected change's translated reason. Dates SHALL be shown in the
user's language. The pages SHALL work on a phone (NFR-01). Every text SHALL be available in es-ES,
ca-ES-valencia and en.

#### Scenario: Single comparsa pre-selected
- **WHEN** a FiringChief assigned to one active comparsa opens the register page
- **THEN** that comparsa is pre-selected

#### Scenario: Client-side validation
- **WHEN** a user types the nationalId "12345678A" in the register form and submits it
- **THEN** the form shows that the check letter is wrong, in the error summary and at the field, without calling the server

#### Scenario: Transfer only for Admins
- **WHEN** a FiringChief opens an arquebusier's detail page
- **THEN** no transfer action is shown, neither in the header nor in "More actions"

#### Scenario: Read-only detail
- **WHEN** a user opens an arquebusier's detail page
- **THEN** the data is shown read-only in sections, with no editable field until the user chooses a section's edit action

#### Scenario: Edit the license in a panel
- **WHEN** a user opens the license section's edit action, chooses `AE` with an issue date and saves
- **THEN** the panel closes, the license section shows the type, the dates and the computed expiry, and the header shows the license as valid

#### Scenario: Compliance warning does not block
- **WHEN** a user opens the detail page of an active arquebusier whose license has expired
- **THEN** the page shows a warning that the license expired on its expiry date, the license state uses the destructive colour, and every edit can still be saved

#### Scenario: Every warning is listed
- **WHEN** a user opens the detail page of a 16-year-old arquebusier without the course, without an ID photo and with a license that expires in three months
- **THEN** the warning message lists the license expiring on its date, the missing course, the age of 16 and the missing ID photo, and the header shows the license as "Expiring soon"

#### Scenario: Warnings follow an edit
- **WHEN** a user records the course date of an arquebusier whose only warning is `COURSE_MISSING`
- **THEN** the warning message disappears after the save

#### Scenario: Delete from More actions
- **WHEN** a FiringChief chooses "Delete from the registry" in "More actions" on an arquebusier of their comparsa and confirms
- **THEN** the arquebusier is deleted, the list opens and announces the deletion

### Requirement: Synthetic registry data
The synthetic seed SHALL create fictional arquebusiers in the seeded comparsas, with valid
synthetic DNI and NIE values. They SHALL cover:
- `ACTIVE` and `RESERVE`;
- every license status, a valid license that expires within 12 months, and no license;
- course done and not done;
- an arquebusier under 18;
- owned weapons, including a pistol.

The seed SHALL give some arquebusiers an ID photo and license photos, and leave others without
photos. It SHALL include an issued license with only one of its two photos. Together, the seeded
arquebusiers SHALL show every compliance warning. The photos SHALL be generated placeholder images
made of flat shapes, with no faces, text of real documents or real photos. Dates SHALL be relative
to the day the seed first runs, so a freshly seeded environment shows every warning whatever the
date. The seed SHALL use fixed
identifiers and SHALL be safe to run again. It SHALL NOT contain real names, national IDs, contact
data, ownership guides or images.

#### Scenario: Seeding twice
- **WHEN** the seed command runs twice on the same database and storage
- **THEN** the same arquebusiers, owned weapons and photos exist once each

#### Scenario: Seeded FiringChief sees their arquebusiers
- **WHEN** the seeded FiringChief "Jefa Sintética Dos" signs in and opens the arquebusiers page
- **THEN** only the seeded arquebusiers of the comparsas assigned to them are listed

#### Scenario: Seeded photos
- **WHEN** an Admin opens the detail page of a seeded arquebusier that has photos
- **THEN** the synthetic ID photo and license photos are shown, and some other seeded arquebusiers show "No ID photo"

#### Scenario: Every warning is seeded
- **WHEN** an Admin opens the start page of a freshly seeded environment
- **THEN** every warning figure counts at least one arquebusier

### Requirement: Arquebusier photos (UC-01, UC-02)
An arquebusier SHALL have up to three photos, one of each kind:
- `idPhoto`: the portrait photo printed on the arquebusier badge;
- `frontPhoto` and `backPhoto`: the front and the back of the current license.

Every photo SHALL be optional (maintainer decision). An arquebusier without photos SHALL be
registered, edited, transferred and deleted like any other. The users who can edit an arquebusier
SHALL be able to upload, replace and remove that arquebusier's photos: a FiringChief for the
comparsas in their scope, an Admin for every comparsa. Uploading a photo of a kind the arquebusier
already has SHALL replace it, and the previous photo SHALL be erased. Uploading, replacing or
removing a photo SHALL NOT change the arquebusier's version, so it never makes a pending edit of
the arquebusier outdated.

The following rules SHALL be blocking:
- a photo operation on an arquebusier outside the caller's scope SHALL be answered as if the
  arquebusier did not exist (`404 Not Found`), and nothing SHALL be stored;
- a `frontPhoto` or `backPhoto` SHALL only be uploaded while the arquebusier has a license,
  pending or issued (`409 Conflict`);
- reading or removing a photo the arquebusier does not have SHALL be answered `404 Not Found`.

#### Scenario: FiringChief uploads an ID photo
- **WHEN** a FiringChief uploads a valid 3:4 portrait image as the `idPhoto` of an arquebusier of their comparsa
- **THEN** the arquebusier has an ID photo, and the arquebusier's version is unchanged

#### Scenario: Admin replaces a license photo
- **WHEN** an Admin uploads a new `frontPhoto` for an arquebusier of any comparsa that already has one
- **THEN** the new photo is returned for that arquebusier and the previous photo is erased

#### Scenario: FiringChief outside the scope
- **WHEN** a FiringChief uploads, reads or removes a photo of an arquebusier of a comparsa they are not assigned to
- **THEN** the API responds `404 Not Found` and nothing is stored, returned or removed

#### Scenario: License photo without a license is blocking
- **WHEN** a `backPhoto` is uploaded for an arquebusier who has no license
- **THEN** the request is rejected with `409 Conflict` and a translated explanation, and nothing is stored

#### Scenario: Removing a photo
- **WHEN** a FiringChief removes the `idPhoto` of an arquebusier of their comparsa
- **THEN** the arquebusier has no ID photo and the photo is erased

#### Scenario: Arquebusier without photos
- **WHEN** an arquebusier is registered without any photo
- **THEN** the arquebusier is stored and can be edited like any other

### Requirement: Photo validation and processing (SEC-12, NFR-15)
The server SHALL accept a photo only when it is a JPEG, PNG or WebP image of at most 10 MB, judged
by its content and not by its name or declared type. Before storing it, the server SHALL:
- turn it upright according to its embedded orientation;
- re-encode it as JPEG;
- remove all metadata, including EXIF location, camera and date data.

The following rules SHALL be blocking (`400 Bad Request` naming `file` with a reason), and nothing
SHALL be stored:
- a missing file (`required`);
- a file over 10 MB (`tooLarge`);
- a file that is not one of the accepted formats, or cannot be decoded (`unsupportedFormat`);
- an image with more than 40 megapixels (`tooLarge`);
- an `idPhoto` that is not 3:4 portrait within a 1 % tolerance (`aspectRatio`);
- an `idPhoto` smaller than 600 × 800 px (`tooSmall`);
- a `frontPhoto` or `backPhoto` whose long side is under 800 px (`tooSmall`), or whose sides differ
  by more than a factor of 2 (`aspectRatio`).

An `idPhoto` SHALL be stored at exactly 3:4, scaled down to at most 1200 × 1600 px. A license photo
SHALL be scaled down to at most 2000 px on its long side. The UI SHALL check the format, the size
and the dimensions before uploading.

#### Scenario: EXIF metadata is removed
- **WHEN** a JPEG with GPS coordinates and camera data in its EXIF metadata is uploaded as an `idPhoto`
- **THEN** the stored photo contains no EXIF or other metadata

#### Scenario: Orientation is applied
- **WHEN** a photo whose EXIF orientation says "rotated 90° clockwise" is uploaded
- **THEN** the stored photo is upright and has no orientation tag

#### Scenario: PNG is re-encoded
- **WHEN** a valid PNG is uploaded as a `frontPhoto`
- **THEN** the photo is stored and served as JPEG

#### Scenario: Wrong ID photo shape is blocking
- **WHEN** a 1000 × 1000 px image is uploaded as an `idPhoto`
- **THEN** the request is rejected with `400 Bad Request` naming `file` with reason `aspectRatio`

#### Scenario: Small ID photo is blocking
- **WHEN** a 300 × 400 px image is uploaded as an `idPhoto`
- **THEN** the request is rejected with `400 Bad Request` naming `file` with reason `tooSmall`

#### Scenario: Disguised file is blocking
- **WHEN** a PDF renamed to "photo.jpg" and declared as `image/jpeg` is uploaded
- **THEN** the request is rejected with `400 Bad Request` naming `file` with reason `unsupportedFormat`

#### Scenario: Oversized file is blocking
- **WHEN** a file of 11 MB is uploaded
- **THEN** the request is rejected and nothing is stored

#### Scenario: Large ID photo is scaled down
- **WHEN** a 3000 × 4000 px image is uploaded as an `idPhoto`
- **THEN** it is stored at 1200 × 1600 px

### Requirement: Private photo access (SEC-02)
Photos SHALL be kept in private storage that is never reachable from the browser or the internet.
The API SHALL return a photo only to a signed-in user who can see the arquebusier (BR-12), with the
same `404 Not Found` for out-of-scope arquebusiers. Photo responses SHALL NOT be stored in any cache.
Stored photo names SHALL NOT contain or be derived from personal data. Photo URLs SHALL contain
only the arquebusier identifier and the photo kind. When the storage cannot be reached, photo
operations SHALL answer `503 Service Unavailable` with a translated, retryable message. Arquebusier
data operations that do not need the storage SHALL keep working.

The arquebusier detail SHALL say which photos exist. The arquebusier list SHALL say for each row
whether the arquebusier has an ID photo, without returning any image.

#### Scenario: FiringChief views a photo of their arquebusier
- **WHEN** a FiringChief requests the `idPhoto` of an arquebusier of their comparsa
- **THEN** the API returns the JPEG image with a response that forbids caching

#### Scenario: Signed-out request
- **WHEN** a request without a session asks for a photo
- **THEN** the API responds `401 Unauthorized` and returns no image

#### Scenario: Storage unavailable
- **WHEN** a user uploads a photo while the storage cannot be reached
- **THEN** the API responds `503 Service Unavailable`, nothing is changed, and editing the arquebusier's other data still works

### Requirement: Photo screens
The register page SHALL let the user add an ID photo. When the arquebusier is registered but the
photo upload fails, the UI SHALL open the new arquebusier's detail page and say that the photo was
not saved and can be added again. On the detail page:
- the ID photo SHALL be shown in the record header;
- the license photos SHALL be shown with the license, and SHALL be offered only while the
  arquebusier has a license.

Each photo SHALL offer actions to add or replace it, and to remove it after a confirmation. To add
a photo, the user SHALL be able to choose an image file or take a picture with the phone camera
(NFR-01). The user SHALL then crop it before uploading: at a fixed 3:4 portrait shape for the ID
photo, and freely for license photos. The user SHALL be able to rotate it by quarter turns. A
missing ID photo SHALL be shown as "No ID photo" in the list rows and on the detail page. When a
photo cannot be loaded, its place SHALL say so instead of showing a broken image. Every text SHALL
be available in es-ES, ca-ES-valencia and en.

#### Scenario: Register with an ID photo
- **WHEN** a FiringChief fills in the register form, chooses an image, crops it and saves
- **THEN** the arquebusier is registered with that ID photo and the detail page shows it in the record header

#### Scenario: Photo upload fails after registration
- **WHEN** the arquebusier is registered but the photo upload is rejected
- **THEN** the detail page of the new arquebusier opens with a message that the photo was not saved and why

#### Scenario: License photos need a license in the UI
- **WHEN** a user opens the detail page of an arquebusier without a license
- **THEN** no license photo action is offered and the license section explains that photos can be added once a license is entered

#### Scenario: Missing ID photo in the list
- **WHEN** a FiringChief opens the arquebusiers list and one of their arquebusiers has no ID photo
- **THEN** that row shows "No ID photo"

#### Scenario: Client-side size check
- **WHEN** a user chooses a 400 × 500 px image for an ID photo
- **THEN** the UI says the image is too small, without uploading it

#### Scenario: Removing a photo asks first
- **WHEN** a user chooses to remove a license photo and then cancels the confirmation
- **THEN** the photo is kept

### Requirement: Stored photo cleanup (BR-14, SEC-08)
When a photo is replaced or removed, its license is removed, or its arquebusier is deleted, the
stored image SHALL be erased once the change is committed. If the erasure fails, the change SHALL
still succeed, and a periodic cleanup SHALL erase every stored photo that no arquebusier references
within 24 hours. A stored image whose record was never committed, for example after a failed
upload, SHALL be erased the same way. The cleanup SHALL NOT erase an image that a committed record
references. It SHALL NOT erase an image uploaded less than one hour earlier.

#### Scenario: Erasure retried by the cleanup
- **WHEN** a photo is replaced while erasing the previous image fails
- **THEN** the replacement succeeds, and the previous image is erased by the next cleanup run

#### Scenario: Upload that was never committed
- **WHEN** an image is stored for an upload whose database change then fails
- **THEN** the upload is answered with an error, and the image is erased by the cleanup once it is more than one hour old

#### Scenario: Referenced photos are kept
- **WHEN** the cleanup runs
- **THEN** every photo still referenced by an arquebusier remains available

### Requirement: Import template (UC-09)
An Admin SHALL be able to download an import template: an `.xlsx` workbook in the user's language
(es-ES, ca-ES-valencia or en). Its first sheet SHALL have one header row with these columns, in
this order (English headers shown; each language uses the labels of the registration form):

| Column | Field | Required |
|---|---|---|
| Federation ID | `federationId` | yes |
| Last names | `lastName` | yes |
| First name | `firstName` | yes |
| DNI/NIE | `nationalId` | yes |
| Date of birth | `birthDate` | yes |
| Gender | `gender` | yes |
| Email | `email` | no |
| Phone | `phone` | no |
| Status | `status` | no (`ACTIVE` when empty) |
| License type | license `type` | no |
| Issue date | license `issuedOn` | no |
| Expiry date | license `expiresOn` | no |
| Course date | `trainingCompletedOn` | no |

The template SHALL:
- offer drop-down lists with the translated values of `gender`, `status` and the license type;
- format the DNI/NIE and phone columns as text, so leading zeros are kept;
- format the date columns as dates;
- include a second sheet that explains, for each column, whether it is required and which values
  and formats it accepts, and states the file limits (see "Import file reading").

The template SHALL hold no arquebusier data, formulas or macros. Downloading it SHALL NOT be
audited, because it holds no data. Only Admins SHALL download it; a FiringChief SHALL receive
`403 Forbidden`.

#### Scenario: Admin downloads the template in Valencian
- **WHEN** an Admin whose language is ca-ES-valencia downloads the import template
- **THEN** the workbook's headers, drop-down values and explanations are in Valencian, and it contains no data rows

#### Scenario: FiringChief cannot download the template
- **WHEN** a FiringChief requests the import template
- **THEN** the API responds `403 Forbidden`

### Requirement: Import file reading (UC-09)
The import SHALL read only `.xlsx` workbooks (Office Open XML spreadsheets without macros) of at
most 2 MB, from the first sheet, with one header row followed by at most 1000 data rows. Rows
whose cells are all empty SHALL be ignored. Each row SHALL be identified in the report by its row
number in the sheet.

Columns SHALL be recognised by their header text in any of the three languages, ignoring letter
case, accents and surrounding spaces, in any order. The following file problems SHALL be blocking
and SHALL be rejected with `400 Bad Request` naming the problem, without reading any row:
- no file, an empty file, or a file over 2 MB;
- a file that is not a readable `.xlsx` workbook, including a macro-enabled workbook, one whose
  uncompressed content is unreasonably large, and one whose XML declares a document type, nests
  unreasonably deep or holds far more rows, cells or texts than the limits allow;
- a missing required column, naming the missing columns;
- the same column twice;
- no data rows, or more than 1000.

Columns that are not recognised SHALL be ignored and listed in the report.

Cell values SHALL be read as follows before validation:
- text SHALL be trimmed; a formula cell SHALL be read by the value saved in the file. A cell with
  an error value (such as `#REF!`), and a formula saved without its value, SHALL be an error of
  that cell (`cellError`), even in an optional column;
- `federationId` and `phone` SHALL accept number cells as well as text;
- a `nationalId` DNI with fewer than 8 digits before its letter SHALL be padded with leading zeros
  before validation, because the check letter does not change;
- dates SHALL accept date cells and text as `dd/mm/yyyy` or `yyyy-mm-dd`. Any other text, such as
  `mm/dd/yyyy`, a plain number and a date cell before 1900-01-01 SHALL be invalid;
- text columns SHALL accept only text: a number, a date or a logical value in a name, email,
  DNI/NIE, gender, status or license type column SHALL be invalid, and so SHALL text over 512
  characters;
- `gender`, `status` and the license type SHALL accept the template's values in any of the three
  languages and the codes (`MALE`, `FEMALE`, `UNSPECIFIED`, `ACTIVE`, `RESERVE`, `AE`, `A_PROF`,
  also written `A-PROF`), ignoring letter case.

The license SHALL be read from its three columns:
- with no license type and no license date, the arquebusier has no license;
- a license type without dates SHALL be a pending license;
- a license type with an issue date SHALL be an issued license, whose expiry SHALL default to the
  BR-03 value when it is empty;
- a license date without a license type SHALL be invalid, and an expiry date without an issue date
  SHALL be invalid.

#### Scenario: Columns in another order and language
- **WHEN** an Admin whose language is es-ES uploads a workbook with English headers in a different order
- **THEN** every column is recognised and the rows are validated

#### Scenario: Missing required column is blocking
- **WHEN** an Admin uploads a workbook without the DNI/NIE column
- **THEN** the request is rejected with `400 Bad Request` naming the missing column, and no row is reported

#### Scenario: Not a spreadsheet
- **WHEN** an Admin uploads a PDF file renamed with the `.xlsx` extension
- **THEN** the request is rejected with `400 Bad Request` saying the file is not a readable workbook

#### Scenario: Too many rows
- **WHEN** an Admin uploads a workbook with 1001 data rows
- **THEN** the request is rejected with `400 Bad Request` saying the file has too many rows

#### Scenario: DNI without its leading zero
- **WHEN** a row has the nationalId "1234567L", whose padded value "01234567L" has a valid check letter
- **THEN** the row is valid and the arquebusier is imported with nationalId "01234567L"

#### Scenario: DNI stored as a number
- **WHEN** a row's DNI/NIE cell is the number 12345678, without a letter
- **THEN** the row has the error that the nationalId is invalid

#### Scenario: Dates as cells and as text
- **WHEN** one row has its birth date as a date cell and another as the text "01/05/1990"
- **THEN** both rows are read with the birth date 1990-05-01

#### Scenario: Ambiguous date text
- **WHEN** a row has the birth date text "05/13/1990"
- **THEN** the row has the error that the birthDate is invalid

#### Scenario: Values in any language
- **WHEN** a row has the gender "Dona" and the status "Reserve"
- **THEN** the row is read with gender `FEMALE` and status `RESERVE`

#### Scenario: Pending license
- **WHEN** a row has the license type "AE" and no license dates
- **THEN** the row is read with a pending AE license

#### Scenario: Issued license with default expiry
- **WHEN** a row has the license type "A-PROF" and the issue date 2026-03-10, with no expiry
- **THEN** the row is read with an A_PROF license expiring on 2027-03-10

#### Scenario: Date without license type
- **WHEN** a row has a license issue date but no license type
- **THEN** the row has the error that the license type is required

### Requirement: Import validation report (UC-09)
An Admin SHALL be able to check an import file for one comparsa without changing anything. The
response SHALL be a report with:
- the number of rows read, of valid rows, of rows with errors and of rows with warnings;
- the ignored columns;
- for each row with an error or a warning: its row number, its last and first name as read, its
  errors and its warnings.

Each error SHALL name the field and the reason, with the same field names and reasons as the
registration form, so a row with several problems lists them all. Each row SHALL be validated with
every blocking rule of a registration. These rules include "Arquebusier data", "National ID
validation (BR-01)", "Current license (UC-02, BR-03)" and "Training course (UC-03)". In addition,
these rules SHALL be blocking:
- a `nationalId` or `federationId` that appears in more than one row SHALL be an error on each of
  those rows;
- a `nationalId` or `federationId` that an arquebusier of any comparsa already has SHALL be an
  error on that row (BR-02). As with a registration, the report SHALL NOT show the existing
  arquebusier's comparsa or data.

The warnings SHALL be the compliance warnings (BR-04) of the row's data, derived as for a
registered arquebusier on today's date in Europe/Madrid. The photo warnings SHALL NOT be listed,
because no import includes photos. The report SHALL instead state that imported arquebusiers have
no photos. Warnings SHALL NOT block the import.

The comparsa SHALL be given with the file. These rules SHALL be blocking:
- the comparsa SHALL be required (`400 Bad Request`);
- it SHALL exist (`404 Not Found`);
- it SHALL be active (`409 Conflict`).

Checking SHALL store nothing, SHALL keep no copy of the file, and SHALL NOT be audited. The
response SHALL NOT be cached. Only Admins SHALL check an import; a FiringChief SHALL receive
`403 Forbidden`.

#### Scenario: Clean file
- **WHEN** an Admin checks a file with 40 valid rows for "Comparsa Sintética Norte"
- **THEN** the report shows 40 rows read, 40 valid and no errors, and nothing is stored

#### Scenario: Every error of a row is reported
- **WHEN** a row has the nationalId "12345678A", an empty first name and the email "not-an-email"
- **THEN** the report lists that row with the errors for `nationalId` (wrong check letter), `firstName` (required) and `email` (invalid)

#### Scenario: Duplicate inside the file
- **WHEN** rows 5 and 9 of the file have the same federationId
- **THEN** both rows have the error that the federationId is repeated in the file

#### Scenario: Arquebusier already registered
- **WHEN** a row's nationalId belongs to an arquebusier of "Comparsa Sintética Sur"
- **THEN** the row has the error that the nationalId is already registered, and the report does not name the other comparsa

#### Scenario: Warnings do not block
- **WHEN** a row describes a 17-year-old arquebusier with an expired license and no course
- **THEN** the row is valid and lists the warnings `LICENSE_EXPIRED`, `COURSE_MISSING` and `UNDER_AGE`, without the photo warnings

#### Scenario: Inactive comparsa
- **WHEN** an Admin checks a file for an inactive comparsa
- **THEN** the request is rejected with `409 Conflict`

#### Scenario: FiringChief cannot check an import
- **WHEN** a FiringChief uploads a file for a comparsa in their scope
- **THEN** the API responds `403 Forbidden` and the file is not read

### Requirement: All-or-nothing import (UC-09)
An Admin SHALL be able to import a file into one comparsa. The server SHALL validate the file again
with every rule of "Import file reading" and "Import validation report", against the registry as
it is at that moment. The outcome SHALL be one of:
- when no row has an error, every row SHALL be registered as a new arquebusier of that comparsa,
  all in one transaction, and the response SHALL give the number of arquebusiers imported;
- when any row has an error, the request SHALL be rejected with `400 Bad Request` carrying the
  report, and nothing SHALL be stored;
- when another registration takes a `nationalId` or `federationId` of the file at the same time,
  the import SHALL be rejected with `409 Conflict`, and nothing SHALL be stored.

The import SHALL only create arquebusiers and SHALL never change or delete an existing one.
Imported arquebusiers SHALL have no owned weapons and no photos. The comparsa rules of "Import
validation report (UC-09)" SHALL apply. Only Admins SHALL import; a FiringChief SHALL receive
`403 Forbidden`. Uploads SHALL be rate-limited per user (`429 Too Many Requests`).

#### Scenario: Admin imports a clean file
- **WHEN** an Admin imports a file with 40 valid rows into "Comparsa Sintética Norte"
- **THEN** 40 arquebusiers are registered in that comparsa and the response says 40 were imported
- **AND** a FiringChief of "Comparsa Sintética Norte" sees them in their list

#### Scenario: One invalid row stops the import
- **WHEN** an Admin imports a file with 39 valid rows and one row with a wrong check letter
- **THEN** the request is rejected with `400 Bad Request` and the report, and no arquebusier is registered

#### Scenario: Registry changed after the check
- **WHEN** an Admin checks a clean file, someone then registers one of its nationalIds by hand, and the Admin confirms the import
- **THEN** the import is rejected with `400 Bad Request`, the report shows that row as already registered, and nothing is stored

#### Scenario: Concurrent imports of the same people
- **WHEN** two Admins import files with the same nationalId at the same time
- **THEN** at most one import stores its arquebusiers, and the other is rejected with `400 Bad Request` or `409 Conflict` and stores nothing

#### Scenario: Importing the same file twice
- **WHEN** an Admin imports a file and then imports the same file again
- **THEN** the second import is rejected with `400 Bad Request`, every row is reported as already registered, and nothing changes

#### Scenario: FiringChief cannot import
- **WHEN** a FiringChief imports a file into a comparsa in their scope
- **THEN** the API responds `403 Forbidden` and nothing is stored

### Requirement: Imports are audited
A completed import SHALL record, in the same transaction as the arquebusiers it creates:
- one registration entry for each arquebusier, like a manual registration, marked as coming from
  an import;
- one import entry with the acting Admin, the comparsa and the number of arquebusiers imported.

As with every registry entry, these entries SHALL NOT contain personal values, the file name or
the file content. A rejected import, a check and a template download SHALL NOT record any entry.

#### Scenario: Import audited
- **WHEN** an Admin imports 3 arquebusiers into "Comparsa Sintética Norte"
- **THEN** three registration entries and one import entry are recorded with the Admin and the comparsa, and none contains a name, nationalId, federationId or date

#### Scenario: Rejected import is not audited
- **WHEN** an import is rejected because one row has an error
- **THEN** no audit entry is recorded

### Requirement: Import screen
The Arquebusiers page SHALL offer an "Import" action to Admins only. It SHALL open an import page
that is reachable by Admins only. A FiringChief who opens its address SHALL see the "not allowed"
page. The import page SHALL let the Admin:
1. choose the comparsa among the active comparsas;
2. download the template in their language;
3. choose an `.xlsx` file. The UI SHALL reject other file types and files over 2 MB before
   uploading them;
4. check the file and read the report;
5. confirm the import, only when the report has no errors, after a confirmation that names the
   comparsa and the number of arquebusiers.

The report SHALL show:
- the summary figures;
- the ignored columns;
- a table of the rows with errors or warnings, giving the row number, the name, and each problem
  in words with its column header.

The table SHALL be filterable to the rows with errors. A file problem SHALL be shown as one
translated message. Changing the comparsa or the file SHALL discard the report. After a successful
import, the arquebusier list SHALL open filtered by that comparsa and announce how many
arquebusiers were imported. When the import is rejected, the page SHALL show the new report.

The page SHALL work on a phone (NFR-01). Every text SHALL be available in es-ES, ca-ES-valencia
and en.

#### Scenario: Import action only for Admins
- **WHEN** a FiringChief opens the Arquebusiers page
- **THEN** no "Import" action is shown

#### Scenario: FiringChief opens the import address
- **WHEN** a FiringChief opens the import page's address directly
- **THEN** the "not allowed" page is shown

#### Scenario: Check, then import
- **WHEN** an Admin chooses "Comparsa Sintética Norte", chooses a clean file with 12 rows, checks it and confirms the import
- **THEN** the arquebusier list opens filtered by "Comparsa Sintética Norte" and announces that 12 arquebusiers were imported

#### Scenario: Errors prevent the confirmation
- **WHEN** the report of a file has two rows with errors
- **THEN** the page lists both rows with each error in words, and the import cannot be confirmed

#### Scenario: Warnings allow the confirmation
- **WHEN** the report of a file has rows with warnings and no errors
- **THEN** the page lists the warnings and the import can be confirmed

#### Scenario: Wrong file type
- **WHEN** an Admin chooses a `.csv` file
- **THEN** the page says that only `.xlsx` files are accepted, without uploading it

#### Scenario: Changing the file discards the report
- **WHEN** an Admin has a report and chooses another file
- **THEN** the report is removed and the new file must be checked before importing
