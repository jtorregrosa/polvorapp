# Spec Delta

## MODIFIED Requirements

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
