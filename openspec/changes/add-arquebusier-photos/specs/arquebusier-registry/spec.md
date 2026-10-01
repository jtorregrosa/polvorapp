# Spec Delta

## ADDED Requirements

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
- the ID photo SHALL be shown with the personal data;
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
- **THEN** the arquebusier is registered with that ID photo and the detail page shows it

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

## MODIFIED Requirements

### Requirement: Arquebusier visibility (BR-12)
An Admin SHALL see every arquebusier. A FiringChief SHALL see only the arquebusiers of the
comparsas in their scope. An arquebusier outside the user's scope SHALL be answered as if it did
not exist (`404 Not Found`) by every operation on it, including its photos. This rule SHALL be
blocking and SHALL be enforced on the server.

The list SHALL be sorted by last name and then first name, in the Spanish alphabetical order. It
SHALL be filterable by comparsa and by status. In the UI it SHALL be searchable by name, nationalId
or federationId, ignoring letter case and accents. Each row SHALL show the name, the nationalId, the
federationId, the comparsa, the status, the license status and whether the arquebusier has an ID
photo.

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

#### Scenario: Rows say whether there is an ID photo
- **WHEN** an Admin lists arquebusiers, one with an ID photo and one without
- **THEN** each row says whether the arquebusier has an ID photo, and the list response contains no image

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

### Requirement: Synthetic registry data
The synthetic seed SHALL create fictional arquebusiers in the seeded comparsas, with valid
synthetic DNI and NIE values, covering `ACTIVE` and `RESERVE`, every license status, no license,
course done and not done, and owned weapons including a pistol. It SHALL give some of them an ID
photo and license photos, and leave others without photos. The photos SHALL be generated
placeholder images made of flat shapes, with no faces, text of real documents or real photos. It
SHALL use fixed identifiers and SHALL be safe to run again. It SHALL NOT contain real names,
national IDs, contact data, ownership guides or images.

#### Scenario: Seeding twice
- **WHEN** the seed command runs twice on the same database and storage
- **THEN** the same arquebusiers, owned weapons and photos exist once each

#### Scenario: Seeded FiringChief sees their arquebusiers
- **WHEN** the seeded FiringChief "Jefa Sintética Dos" signs in and opens the arquebusiers page
- **THEN** only the seeded arquebusiers of the comparsas assigned to them are listed

#### Scenario: Seeded photos
- **WHEN** an Admin opens the detail page of a seeded arquebusier that has photos
- **THEN** the synthetic ID photo and license photos are shown, and some other seeded arquebusiers show "No ID photo"
