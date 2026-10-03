# federation-catalog Specification

## Purpose
Holds the Federation's reference data that every other capability builds on: the comparsas with
their side, the FiringChiefs assigned to each comparsa (which determines their comparsa scope,
BR-12) and the shared weapon model catalogue (UC-24, BR-07).

## Requirements

### Requirement: Comparsas
A `Comparsa` SHALL have a `name` and a `side`. The name SHALL be 1 to 100 characters after trimming
and SHALL be unique across the Federation, compared case-insensitively. The side SHALL be `MOORISH`
or `CHRISTIAN`. Each comparsa SHALL also have an `active` flag. These rules SHALL be blocking.

#### Scenario: Admin creates a comparsa
- **WHEN** an Admin creates a comparsa named "Comparsa Sintética Norte" with side `CHRISTIAN`
- **THEN** the comparsa is stored as active and returned with its identifier

#### Scenario: Duplicate name is blocking
- **WHEN** an Admin creates or renames a comparsa to "comparsa sintética norte" while "Comparsa Sintética Norte" exists
- **THEN** the request is rejected with `409 Conflict` and nothing is changed

#### Scenario: Invalid side or empty name is blocking
- **WHEN** an Admin submits a comparsa with side `NEUTRAL`, or a name that is empty or only spaces
- **THEN** the request is rejected with `400 Bad Request` naming the invalid field

### Requirement: Comparsa management by Admins
Admins SHALL be able to create comparsas, edit a comparsa's name and side, deactivate and
reactivate it, and delete it (see "Deleting comparsas and weapon models"). Only Admins SHALL have
these operations. A FiringChief SHALL receive `403 Forbidden` from the API and SHALL NOT see these
actions in the UI. The UI SHALL ask for confirmation before a deactivation. Deactivating a comparsa
SHALL keep its FiringChief assignments. Deactivation is for a comparsa that no longer takes part
but whose history must stay visible. Deletion is for a comparsa entered by mistake or never used.

#### Scenario: Admin edits a comparsa
- **WHEN** an Admin changes the side of a comparsa from `CHRISTIAN` to `MOORISH`
- **THEN** the comparsa is updated and the change is audited with its previous and new values

#### Scenario: Admin deactivates a comparsa
- **WHEN** an Admin deactivates a comparsa and confirms
- **THEN** the comparsa becomes inactive, it keeps its assignments, and it is still listed when inactive comparsas are included

#### Scenario: Admin reactivates a comparsa
- **WHEN** an Admin reactivates an inactive comparsa
- **THEN** the comparsa becomes active again

#### Scenario: FiringChief cannot manage comparsas
- **WHEN** a signed-in FiringChief tries to create, edit, deactivate, reactivate or delete a comparsa through the API
- **THEN** the API responds `403 Forbidden` and nothing is changed

### Requirement: Comparsa visibility (BR-12)
The list of comparsas SHALL be sorted by name. It SHALL be filterable by side and by active state,
and by default it SHALL show only active comparsas. An Admin SHALL see every comparsa. A FiringChief
SHALL see only the comparsas assigned to them, active or not, and read-only. A comparsa outside the
user's scope SHALL be answered as if it did not exist (`404 Not Found`). This rule is blocking and
SHALL be enforced on the server.

#### Scenario: Admin lists comparsas
- **WHEN** an Admin opens the comparsas page
- **THEN** every active comparsa is listed with its name, side and state
- **AND** the Admin can include the inactive ones and filter by side

#### Scenario: FiringChief lists comparsas
- **WHEN** a FiringChief assigned to one comparsa opens the comparsas page
- **THEN** only that comparsa is listed and no create, edit or assignment action is offered

#### Scenario: FiringChief opens another comparsa
- **WHEN** a FiringChief requests a comparsa they are not assigned to
- **THEN** the API responds `404 Not Found` and the UI shows the not-found page

#### Scenario: FiringChief without assignments
- **WHEN** a FiringChief with no assignments opens the comparsas page
- **THEN** an empty state explains that no comparsa is assigned to them yet and that an Admin must assign one

### Requirement: FiringChief assignments
An Admin SHALL be able to assign a user to a comparsa as its FiringChief and remove that
assignment. A comparsa MAY have several FiringChiefs, and a user MAY be assigned to several
comparsas. The following rules SHALL be blocking:
- only a user whose role is `FIRING_CHIEF` SHALL be assignable (`409 Conflict` otherwise);
- a `DEACTIVATED` user or an inactive comparsa SHALL NOT receive new assignments (`409 Conflict`);
- a user or comparsa that does not exist SHALL be answered with `404 Not Found`.

Assigning an existing assignment SHALL succeed without creating a duplicate. Removing an
assignment that does not exist SHALL succeed without changing anything. Existing assignments SHALL
be kept when the user is deactivated, when the comparsa is deactivated or when the user's role
changes to `ADMIN`. They SHALL be removed when the comparsa is deleted. While the user is an Admin they have no effect, because an Admin's scope is
every comparsa. Only Admins SHALL list, create or remove assignments. FiringChiefs SHALL receive
`403 Forbidden`.

#### Scenario: Admin assigns a FiringChief
- **WHEN** an Admin assigns the FiringChief "Jefe Sintético Uno" to the active comparsa "Comparsa Sintética Norte"
- **THEN** the assignment is stored and audited with the comparsa and the user

#### Scenario: Invited FiringChief can be assigned
- **WHEN** an Admin assigns a `FIRING_CHIEF` user whose status is `INVITED`
- **THEN** the assignment is stored, so the user has their scope from their first sign-in

#### Scenario: Assigning an Admin is blocking
- **WHEN** an Admin tries to assign a user whose role is `ADMIN` to a comparsa
- **THEN** the request is rejected with `409 Conflict` and a translated explanation

#### Scenario: Assigning a deactivated user or to an inactive comparsa is blocking
- **WHEN** an Admin tries to assign a `DEACTIVATED` FiringChief, or any FiringChief to an inactive comparsa
- **THEN** the request is rejected with `409 Conflict` and a translated explanation

#### Scenario: Repeated assignment
- **WHEN** an Admin assigns a FiringChief to a comparsa they are already assigned to
- **THEN** the request succeeds, only one assignment exists and no additional audit entry is recorded

#### Scenario: Admin removes an assignment
- **WHEN** an Admin removes a FiringChief from a comparsa and confirms
- **THEN** the assignment is deleted and the removal is audited with the comparsa and the user

#### Scenario: Several FiringChiefs and several comparsas
- **WHEN** an Admin assigns two FiringChiefs to one comparsa and one of them also to a second comparsa
- **THEN** the first comparsa lists both FiringChiefs and that FiringChief's detail lists both comparsas

#### Scenario: FiringChief cannot manage assignments
- **WHEN** a signed-in FiringChief calls an assignment endpoint, including for their own comparsa
- **THEN** the API responds `403 Forbidden` and nothing is changed

### Requirement: Assignments determine the comparsa scope
The comparsas assigned to a FiringChief SHALL be exactly the comparsas their comparsa scope grants
(BR-12). An assignment or removal SHALL take effect on the FiringChief's next request, without
signing in again.

#### Scenario: Scope granted by an assignment
- **WHEN** an Admin assigns a signed-in FiringChief to a comparsa
- **THEN** that FiringChief's next request for that comparsa is answered and the comparsa appears in their list

#### Scenario: Scope removed with the assignment
- **WHEN** an Admin removes the only assignment of a signed-in FiringChief
- **THEN** that FiringChief's next request for that comparsa responds `404 Not Found` and their list is empty

#### Scenario: Scope of a user promoted to Admin
- **WHEN** a FiringChief assigned to one comparsa is changed to the `ADMIN` role
- **THEN** their next request is scoped to every comparsa and the kept assignment has no effect

### Requirement: Managing assignments from the comparsa and from the user
The comparsa detail page SHALL show Admins the comparsa's FiringChiefs, each with their name, email
and user status. From that page an Admin SHALL be able to add a FiringChief by choosing among
`FIRING_CHIEF` users who are not deactivated and not yet assigned, and to remove a FiringChief.
The user detail page of a `FIRING_CHIEF` user SHALL show Admins that user's comparsas and SHALL
let them add and remove comparsas in the same way. For an `ADMIN` user who still has assignments,
the page SHALL say that they have no effect. Removals SHALL be confirmed. A rejected change SHALL
show its translated reason.

#### Scenario: Assign from the comparsa page
- **WHEN** an Admin adds "Jefa Sintética Dos" from the FiringChiefs section of a comparsa
- **THEN** that user is listed in that section and the comparsa appears on their user detail page

#### Scenario: Assign from the user page
- **WHEN** an Admin adds a comparsa from the Comparsas section of a FiringChief's user detail page
- **THEN** the comparsa is listed in that section and the FiringChief appears on the comparsa detail page

#### Scenario: Candidates exclude ineligible users and comparsas
- **WHEN** an Admin opens the add-FiringChief choice on a comparsa page, or the add-comparsa choice on a user page
- **THEN** deactivated users, users with the `ADMIN` role, inactive comparsas and existing assignments are not offered

#### Scenario: Rejected assignment shows its reason
- **WHEN** an assignment is rejected because the user was deactivated after the page loaded
- **THEN** the page shows the translated reason and the list reflects the current assignments

### Requirement: Weapon models
A `WeaponModel` SHALL have:
- a `kind`: `TRABUCO`, `ARCABUZ` or `PISTOL`;
- a `side`: `MOORISH` or `CHRISTIAN`;
- a `handedness`: `RIGHT` or `LEFT`;
- a `size`: `NORMAL` or `SMALL`;
- a `rentable` flag;
- a `label` in the Federation's naming (for example "TRABUCO CRISTIANO DIESTRO (PEQUEÑO)");
- an `active` flag.

The following rules SHALL be blocking:
- the label SHALL be 1 to 100 characters after trimming and unique, compared case-insensitively;
- a `PISTOL` SHALL NOT be rentable (BR-07);
- for `TRABUCO` and `ARCABUZ`, side, handedness and size SHALL be required, and the combination of
  kind, side, handedness and size SHALL be unique;
- for `PISTOL`, side, handedness and size SHALL be optional.

The kind and the side MAY be combined freely.

#### Scenario: Admin creates a rentable model
- **WHEN** an Admin creates a model with kind `TRABUCO`, side `CHRISTIAN`, handedness `LEFT`, size `SMALL`, rentable, label "TRABUCO CRISTIANO ZURDO (PEQUEÑO)"
- **THEN** the model is stored as active and returned with its identifier

#### Scenario: Rentable pistol is blocking
- **WHEN** an Admin creates or edits a model of kind `PISTOL` with `rentable` set
- **THEN** the request is rejected with `400 Bad Request` naming `rentable`

#### Scenario: Pistol without attributes
- **WHEN** an Admin creates a model of kind `PISTOL`, not rentable, labelled "PISTOLA", without side, handedness or size
- **THEN** the model is stored

#### Scenario: Missing attributes are blocking
- **WHEN** an Admin creates a model of kind `ARCABUZ` without a handedness
- **THEN** the request is rejected with `400 Bad Request` naming `handedness`

#### Scenario: Duplicate combination or label is blocking
- **WHEN** an Admin creates an `ARCABUZ` `MOORISH` `RIGHT` `NORMAL` model while one exists, or reuses an existing label in a different letter case
- **THEN** the request is rejected with `409 Conflict` and a translated explanation

#### Scenario: Free kind and side combination
- **WHEN** an Admin creates a model of kind `ARCABUZ` with side `CHRISTIAN`, labelled "ARCABUZ CRISTIANO DIESTRO"
- **THEN** the model is stored

#### Scenario: Invalid enum value is blocking
- **WHEN** an Admin submits a model with kind `CANNON` or size `LARGE`
- **THEN** the request is rejected with `400 Bad Request` naming the invalid field

### Requirement: Weapon catalogue access
Every signed-in user SHALL be able to list weapon models. The list SHALL be sorted by label,
filterable by kind and active state, and by default it SHALL show only active models. Only Admins
SHALL create, edit, deactivate, reactivate and delete models. FiringChiefs SHALL receive `403 Forbidden`
from these operations and SHALL NOT see the weapon catalogue pages in the UI navigation. The UI
SHALL show kind, side, handedness and size as translated labels, and the Federation `label` as
entered.

#### Scenario: FiringChief reads the catalogue through the API
- **WHEN** a signed-in FiringChief requests the weapon models
- **THEN** the active models are returned

#### Scenario: FiringChief cannot change the catalogue
- **WHEN** a signed-in FiringChief tries to create, edit, deactivate, reactivate or delete a model
- **THEN** the API responds `403 Forbidden` and nothing is changed

#### Scenario: Admin deactivates a model
- **WHEN** an Admin deactivates a model and confirms
- **THEN** the model is inactive, hidden from the default list and shown when inactive models are included

#### Scenario: Admin edits a model
- **WHEN** an Admin changes the label and size of a model
- **THEN** the model is updated and the change is audited with its previous and new values

### Requirement: Deleting comparsas and weapon models
An Admin SHALL be able to delete a comparsa or a weapon model after confirming in the UI. Deleting
a comparsa SHALL also remove its FiringChief assignments in the same operation, and SHALL erase its
logo once the deletion is committed. The confirmation SHALL state how many FiringChiefs lose access
to the comparsa. A comparsa or weapon model that other records reference SHALL NOT be deleted
(blocking, `409 Conflict`). Such references are, for example, arquebusiers, owned weapons, edition
availability or orders, which later changes add. A comparsa's own logo SHALL NOT count as such a
reference. In that case the UI SHALL show the translated reason and suggest deactivating instead.
After a deletion:
- the record SHALL no longer be found (`404 Not Found`);
- its name or label MAY be reused;
- FiringChiefs who were assigned to a deleted comparsa SHALL lose its scope on their next request.

Only Admins SHALL delete. FiringChiefs SHALL receive `403 Forbidden`.

#### Scenario: Admin deletes an unused comparsa
- **WHEN** an Admin deletes a comparsa that no other record references and confirms
- **THEN** the comparsa no longer exists, a request for it responds `404 Not Found` and its name can be used for a new comparsa

#### Scenario: Deleting a comparsa removes its assignments
- **WHEN** an Admin deletes a comparsa with two assigned FiringChiefs, after a confirmation stating that two FiringChiefs lose access
- **THEN** both assignments are removed and each of those FiringChiefs' next request no longer includes that comparsa

#### Scenario: Deleting a comparsa erases its logo
- **WHEN** an Admin deletes an unused comparsa that has a logo and confirms
- **THEN** the comparsa no longer exists and its logo image is erased

#### Scenario: Referenced comparsa cannot be deleted
- **WHEN** an Admin deletes a comparsa that another record references
- **THEN** the request is rejected with `409 Conflict`, nothing is deleted, the logo is kept and the UI suggests deactivating the comparsa

#### Scenario: Admin deletes an unused weapon model
- **WHEN** an Admin deletes a weapon model that no other record references and confirms
- **THEN** the model no longer exists and its label can be used for a new model

#### Scenario: Referenced weapon model cannot be deleted
- **WHEN** an Admin deletes a weapon model that another record references
- **THEN** the request is rejected with `409 Conflict`, nothing is deleted and the UI suggests deactivating the model

#### Scenario: Deleting something that does not exist
- **WHEN** an Admin deletes a comparsa or weapon model id that does not exist
- **THEN** the API responds `404 Not Found`

### Requirement: Catalogue changes are audited
Every creation, edit, deactivation, reactivation and deletion of a comparsa or weapon model, every
upload, replacement and removal of a comparsa logo, and every assignment and removal of a
FiringChief, SHALL be recorded in the audit trail in the same transaction as the change. Each entry
SHALL record the acting Admin, the action, the target and, for comparsa changes, logo changes and
assignments, the comparsa concerned. Logo entries SHALL say whether a previous logo was replaced,
and SHALL NOT contain image data, stored names, sizes or dimensions. An operation that changes
nothing (a repeated assignment, an edit with identical values, deactivating an inactive record)
SHALL NOT record an entry. A rejected logo upload SHALL NOT record an entry.

#### Scenario: Assignment audited with its comparsa
- **WHEN** an Admin assigns a FiringChief to a comparsa
- **THEN** an audit entry with action `FiringChiefAssigned`, the Admin as actor, the comparsa id and the assigned user id is recorded

#### Scenario: Deletion audited with a snapshot
- **WHEN** an Admin deletes a comparsa with assigned FiringChiefs
- **THEN** one audit entry with action `ComparsaDeleted` records the comparsa id, its name, side and active state, whether it had a logo, and the ids of the users whose assignments were removed

#### Scenario: Logo replacement audited
- **WHEN** an Admin uploads a logo for a comparsa that already has one
- **THEN** one audit entry with action `ComparsaLogoUploaded`, the Admin as actor and the comparsa id records that a logo was replaced, without any image data or stored name

#### Scenario: Logo removal audited
- **WHEN** an Admin removes a comparsa's logo
- **THEN** one audit entry with action `ComparsaLogoRemoved`, the Admin as actor and the comparsa id is recorded

#### Scenario: Unchanged edit is not audited
- **WHEN** an Admin saves a weapon model without changing any value
- **THEN** no audit entry is recorded

### Requirement: Synthetic catalogue data
The synthetic seed SHALL create fictional comparsas of both sides, including one inactive. It
SHALL assign the seeded FiringChiefs so that one comparsa has two FiringChiefs and one FiringChief
has two comparsas. It SHALL give most seeded comparsas a generated logo: abstract flat shapes on a
transparent background, without text, letters or any real emblem. At least one seeded comparsa SHALL
have no logo, and at least one generated logo SHALL be dark enough to need the light tile in the dark
theme. It SHALL also create a weapon catalogue covering every kind, including a non-rentable pistol
and an inactive model. It SHALL use fixed identifiers and SHALL be safe to run again. The seed SHALL
NOT contain real comparsa names, real comparsa logos or real Federation data. Production data SHALL
be entered by an Admin.

#### Scenario: Seeding twice
- **WHEN** the seed command runs twice on the same database
- **THEN** the same comparsas, logos, assignments and models exist once each

#### Scenario: Seeded FiringChief scope
- **WHEN** the seeded FiringChief "Jefe Sintético Uno" signs in
- **THEN** only the seeded comparsas assigned to them are listed

#### Scenario: Seeded logos
- **WHEN** the seed has run
- **THEN** most seeded comparsas have a generated logo that passes the logo rules, and at least one has none

### Requirement: Comparsa logos
A `Comparsa` SHALL have an optional `logo`: one image that represents the comparsa. Only Admins
SHALL upload, replace and remove a logo. FiringChiefs SHALL receive `403 Forbidden` from these
operations, including for their own comparsas. Uploading a logo for a comparsa that already has one
SHALL replace it, and the previous image SHALL be erased. A logo SHALL be uploadable for an active
or an inactive comparsa. Deactivating or reactivating a comparsa SHALL keep its logo. Uploading,
replacing or removing a logo SHALL NOT change the comparsa's name, side or active state.

The following rules SHALL be blocking:
- a logo operation on a comparsa that does not exist SHALL be answered `404 Not Found`, and nothing
  SHALL be stored;
- reading or removing the logo of a comparsa that has none SHALL be answered `404 Not Found`.

When a logo is replaced or removed, or its comparsa is deleted, the stored image SHALL be erased once
the change is committed. If the erasure fails, the change SHALL still succeed, and the platform's
stored file cleanup SHALL erase the image within 24 hours. The cleanup SHALL NOT erase an image that
a committed comparsa references.

#### Scenario: Admin uploads a logo
- **WHEN** an Admin uploads a valid PNG logo for the comparsa "Comparsa Sintética Norte"
- **THEN** the comparsa has a logo, and its name, side and active state are unchanged

#### Scenario: Admin replaces a logo
- **WHEN** an Admin uploads a new logo for a comparsa that already has one
- **THEN** the new logo is returned for that comparsa and the previous image is erased

#### Scenario: Admin removes a logo
- **WHEN** an Admin removes the logo of a comparsa
- **THEN** the comparsa has no logo and the image is erased

#### Scenario: FiringChief cannot change a logo
- **WHEN** a FiringChief uploads or removes the logo of a comparsa they are assigned to
- **THEN** the API responds `403 Forbidden` and nothing is stored or removed

#### Scenario: Unknown comparsa
- **WHEN** an Admin uploads a logo for a comparsa id that does not exist
- **THEN** the API responds `404 Not Found` and nothing is stored

#### Scenario: Logo kept on deactivation
- **WHEN** an Admin deactivates a comparsa that has a logo
- **THEN** the comparsa keeps its logo

#### Scenario: Erasure retried by the cleanup
- **WHEN** a logo is replaced while erasing the previous image fails
- **THEN** the replacement succeeds, and the previous image is erased by a later cleanup run

### Requirement: Logo validation and processing
The server SHALL accept a logo only when it is a JPEG, PNG or WebP image of at most 10 MB, judged by
its content and not by its name or declared type. SVG and other vector formats SHALL NOT be
accepted. Before storing it, the server SHALL:
- turn it upright according to its embedded orientation;
- re-encode it as PNG, keeping its transparency;
- remove all metadata.

The following rules SHALL be blocking (`400 Bad Request` naming `file` with a reason), and nothing
SHALL be stored:
- a missing file (`required`);
- a file over 10 MB (`tooLarge`);
- a file that is not one of the accepted formats, or cannot be decoded (`unsupportedFormat`);
- an image with more than 40 megapixels (`tooLarge`);
- an image whose long side is under 256 px (`tooSmall`);
- an image whose long side is more than 3 times its short side (`aspectRatio`).

A logo SHALL be scaled down to at most 1024 px on its long side, keeping its shape, and SHALL never
be scaled up. The UI SHALL check the format, the size and the dimensions before uploading.

#### Scenario: Transparency is kept
- **WHEN** an Admin uploads a PNG logo with a transparent background
- **THEN** the stored logo is a PNG whose background is still transparent

#### Scenario: JPEG is re-encoded as PNG
- **WHEN** an Admin uploads a valid JPEG logo with EXIF metadata
- **THEN** the logo is stored and served as PNG without any metadata

#### Scenario: Large logo is scaled down
- **WHEN** an Admin uploads a 3000 × 1500 px logo
- **THEN** it is stored at 1024 × 512 px

#### Scenario: Small logo is blocking
- **WHEN** an Admin uploads a 200 × 200 px image as a logo
- **THEN** the request is rejected with `400 Bad Request` naming `file` with reason `tooSmall`

#### Scenario: Elongated logo is blocking
- **WHEN** an Admin uploads a 1200 × 300 px image as a logo
- **THEN** the request is rejected with `400 Bad Request` naming `file` with reason `aspectRatio`

#### Scenario: SVG is blocking
- **WHEN** an Admin uploads an SVG file declared as `image/png`
- **THEN** the request is rejected with `400 Bad Request` naming `file` with reason `unsupportedFormat`

#### Scenario: Oversized file is blocking
- **WHEN** an Admin uploads a file of 11 MB as a logo
- **THEN** the request is rejected and nothing is stored

### Requirement: Logo access (BR-12)
Logos SHALL be kept in the platform's private storage, never reachable from the browser or the
internet. The API SHALL return a logo only to a signed-in user who can see the comparsa, with the
same `404 Not Found` for a comparsa outside the user's scope. Logo responses SHALL NOT be stored in
any cache. Stored logo names SHALL be random and SHALL NOT be derived from the comparsa. Logo URLs
SHALL contain only the comparsa identifier. When the storage cannot be reached, uploading or reading
a logo SHALL answer `503 Service Unavailable` with a translated, retryable message, and comparsa
operations that do not need the storage SHALL keep working.

A comparsa, as the API returns it in the list and the detail, SHALL say whether it has a logo and
SHALL carry a value that changes whenever the logo changes, without returning any image. Other
capabilities SHALL be able to read a comparsa's logo on the server, for example to print it in a
document, without a user scope; they SHALL apply the user's scope themselves when they act for a
user.

#### Scenario: FiringChief views their comparsa's logo
- **WHEN** a FiringChief requests the logo of a comparsa they are assigned to
- **THEN** the API returns the PNG image with a response that forbids caching

#### Scenario: FiringChief requests another comparsa's logo
- **WHEN** a FiringChief requests the logo of a comparsa they are not assigned to
- **THEN** the API responds `404 Not Found` and returns no image

#### Scenario: Signed-out request
- **WHEN** a request without a session asks for a logo
- **THEN** the API responds `401 Unauthorized` and returns no image

#### Scenario: Storage unavailable
- **WHEN** an Admin uploads a logo while the storage cannot be reached
- **THEN** the API responds `503 Service Unavailable`, nothing is changed, and editing the comparsa's name and side still works

#### Scenario: Logo state in the list
- **WHEN** an Admin lists comparsas and one has a logo and another has none
- **THEN** each row says whether it has a logo, and no image is part of the list response

### Requirement: Logo display
The UI SHALL show a comparsa's logo next to its name in the comparsas list and in the record header
of the comparsa detail page. A comparsa without a logo SHALL show a neutral placeholder in the same
place, so names stay aligned. Wherever the comparsa name is shown next to it, the logo SHALL be
decorative and SHALL NOT repeat the name to assistive technology. The logo SHALL be shown on a light
tile that keeps any logo visible in both themes, including a dark logo with a transparent
background in the dark theme, and SHALL keep its shape inside the tile. When a logo cannot be
loaded, the placeholder SHALL be shown instead of a broken image.

For Admins, the comparsa detail page SHALL have a logo section to add, replace and remove the logo.
To add a logo the Admin SHALL choose an image file and MAY crop it freely before uploading; the
crop SHALL start with the whole image selected. Removing a logo SHALL ask for confirmation first.
FiringChiefs SHALL see the logo but SHALL NOT be offered these actions. Every text SHALL be
available in es-ES, ca-ES-valencia and en.

#### Scenario: Logo in the list
- **WHEN** a FiringChief opens the comparsas page and their comparsa has a logo
- **THEN** the row shows the logo next to the comparsa name

#### Scenario: Placeholder without a logo
- **WHEN** an Admin opens the detail page of a comparsa without a logo
- **THEN** the record header shows the placeholder next to the name, and the logo section offers to add one

#### Scenario: Admin adds a logo from the detail page
- **WHEN** an Admin chooses a PNG image in the logo section, keeps the whole image selected and confirms
- **THEN** the logo is uploaded, and the record header and the comparsas list show it

#### Scenario: Removing a logo asks first
- **WHEN** an Admin chooses to remove a comparsa's logo and then cancels the confirmation
- **THEN** the logo is kept

#### Scenario: FiringChief has no logo actions
- **WHEN** a FiringChief opens the detail page of their comparsa
- **THEN** the logo is shown in the record header and no action to add, replace or remove it is offered

#### Scenario: Dark logo in the dark theme
- **WHEN** a comparsa's logo is black on a transparent background and the user uses the dark theme
- **THEN** the logo is shown on a light tile and remains visible

#### Scenario: Client-side size check
- **WHEN** an Admin chooses a 200 × 150 px image as a logo
- **THEN** the UI says the image is too small, without uploading it

### Requirement: Federation logo
The Federation SHALL have an optional logo, used in the documents that carry it, starting with the
pickup authorisation form (and later the badges). It SHALL be uploaded at run time by an Admin and
kept in the platform's private storage. It SHALL NOT be part of the source code, the seed or any
committed file, because the repository is public and the Federation's crest is not PolvorApp's to
license (maintainer decision).

Only Admins SHALL upload, replace and remove it; FiringChiefs SHALL receive `403 Forbidden`.
Replacing or removing it SHALL erase the previous image once the change is committed, with the same
cleanup guarantee as comparsa logos. It SHALL follow the same validation and processing as comparsa
logos (see "Logo validation and processing"): JPEG, PNG or WebP judged by content, at most 10 MB
and 40 megapixels, long side from 256 px and at most 3 times the short side, turned upright,
re-encoded as PNG keeping transparency, without metadata, scaled down to at most 1024 px. Every
signed-in user SHALL be able to read it, with responses that forbid caching; reading it when there
is none SHALL be answered `404 Not Found`. Other capabilities SHALL read it on the server to print
it. When the storage cannot be reached, uploading or reading it SHALL answer `503 Service
Unavailable`, and documents SHALL then fail with a translated, retryable message rather than be
printed without it. Every upload, replacement and removal SHALL be audited, without the image.

For Admins, the comparsas page SHALL have a "Federation logo" section to add (with free cropping,
starting with the whole image), replace and remove it (removal confirmed in a dialog). FiringChiefs
SHALL NOT be offered these actions. Every text SHALL be available in es-ES, ca-ES-valencia and en,
and the section SHALL pass automated accessibility checks (NFR-07).

#### Scenario: Admin uploads the Federation logo
- **WHEN** an Admin uploads a valid PNG as the Federation logo from the comparsas page
- **THEN** the logo is stored as PNG without metadata, the section shows it, and the upload is audited

#### Scenario: Admin replaces the Federation logo
- **WHEN** an Admin uploads a new Federation logo while one exists
- **THEN** the new logo is used from then on and the previous image is erased

#### Scenario: Invalid image is blocking
- **WHEN** an Admin uploads a 200 × 200 px image as the Federation logo
- **THEN** the request is rejected with `400 Bad Request` naming `file` with reason `tooSmall`, and nothing is stored

#### Scenario: FiringChief cannot change it
- **WHEN** a FiringChief uploads or removes the Federation logo through the API
- **THEN** the API responds `403 Forbidden` and nothing is changed

#### Scenario: No logo yet
- **WHEN** a user requests the Federation logo before any was uploaded
- **THEN** the API responds `404 Not Found`

#### Scenario: Nothing committed
- **WHEN** the repository and the synthetic seed are inspected
- **THEN** they contain no Federation logo or crest image
