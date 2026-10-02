# Spec Delta

## ADDED Requirements

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

## MODIFIED Requirements

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
