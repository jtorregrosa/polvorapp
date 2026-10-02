# Spec Delta

## MODIFIED Requirements

### Requirement: Arquebusier visibility (BR-12)
An Admin SHALL see every arquebusier. A FiringChief SHALL see only the arquebusiers of the
comparsas in their scope. An arquebusier outside the user's scope SHALL be answered as if it did
not exist (`404 Not Found`) by every operation on it, including its photos. This rule SHALL be
blocking and SHALL be enforced on the server.

The list SHALL be sorted by last name and then first name, in the Spanish alphabetical order. It
SHALL be filterable by comparsa, by status and by license state: expired, pending or no license.
In the UI it SHALL be searchable by name, nationalId or federationId, ignoring letter case and
accents. Above the list, the UI SHALL show counters for active, reserve, expired-license,
pending-license and no-license arquebusiers within the user's scope. Each counter SHALL also be a
filter that the user can switch on and off, and its pressed state SHALL be exposed to assistive
technology. The UI SHALL announce the number of listed arquebusiers whenever a filter or the search
changes it. Each row SHALL show the name, the comparsa, the nationalId, the federationId, the status,
the license status with its expiry date and whether the arquebusier has an ID photo.

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
the course state. Its key facts SHALL show the license expiry with how much of the license's validity
has passed, the federationId and nationalId, the course date and the number of owned weapons. A
compliance warning (license not valid, no course) SHALL be shown as a warning that never blocks
saving (BR-04).

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
- **THEN** the page shows a warning that the license is not valid, the license state uses the destructive colour, and every edit can still be saved

#### Scenario: Delete from More actions
- **WHEN** a FiringChief chooses "Delete from the registry" in "More actions" on an arquebusier of their comparsa and confirms
- **THEN** the arquebusier is deleted, the list opens and announces the deletion

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
