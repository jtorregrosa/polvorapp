# Spec Delta

## ADDED Requirements

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
