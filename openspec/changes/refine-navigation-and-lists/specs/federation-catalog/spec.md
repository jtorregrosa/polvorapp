# Spec Delta

## MODIFIED Requirements

### Requirement: Comparsa visibility (BR-12)
The list of comparsas SHALL be sorted by name, and every column SHALL be sortable; the side SHALL be a
tag (see "Tags for fixed values"). It SHALL be filterable by side and by active state,
and by default it SHALL show only active comparsas. An Admin SHALL see every comparsa. A FiringChief
SHALL see only the comparsas assigned to them, active or not, and read-only. A comparsa outside the
user's scope SHALL be answered as if it did not exist (`404 Not Found`). This rule is blocking and
SHALL be enforced on the server.

#### Scenario: Admin lists comparsas
- **WHEN** an Admin opens the comparsas page
- **THEN** every active comparsa is listed with its name, side as a tag and state
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

### Requirement: Weapon catalogue access
Every signed-in user SHALL be able to list weapon models. The API list SHALL be sorted by label,
filterable by kind and active state, and by default it SHALL return only active models. The
weapon catalogue page SHALL list every model, active and inactive, by default, with a filter to
show only the active ones kept in the address. Only Admins
SHALL create, edit, deactivate, reactivate and delete models. FiringChiefs SHALL receive `403 Forbidden`
from these operations and SHALL NOT see the weapon catalogue pages in the UI navigation. The UI
SHALL show kind, side, handedness and size as translated labels, and the Federation `label` as
entered. In the list, the kind, the side and the rentable flag SHALL be tags (see "Tags for fixed
values") and the state a status badge; every column SHALL be sortable.

#### Scenario: FiringChief reads the catalogue through the API
- **WHEN** a signed-in FiringChief requests the weapon models
- **THEN** the active models are returned

#### Scenario: FiringChief cannot change the catalogue
- **WHEN** a signed-in FiringChief tries to create, edit, deactivate, reactivate or delete a model
- **THEN** the API responds `403 Forbidden` and nothing is changed

#### Scenario: Admin deactivates a model
- **WHEN** an Admin deactivates a model and confirms
- **THEN** the model is inactive, still listed on the weapon catalogue page with the inactive state, hidden when the Admin filters to active models only, and no longer returned by the API's default list

#### Scenario: Catalogue page lists inactive models by default
- **WHEN** an Admin opens the weapon catalogue page without filters
- **THEN** active and inactive models are listed, the inactive ones with the inactive status badge

#### Scenario: Only active models
- **WHEN** an Admin chooses "Only active" on the weapon catalogue page
- **THEN** only active models are listed and the filter is kept in the address

#### Scenario: Admin edits a model
- **WHEN** an Admin changes the label and size of a model
- **THEN** the model is updated and the change is audited with its previous and new values

### Requirement: Logo display
The UI SHALL show a comparsa's logo next to its name in the comparsas list and in the record header
of the comparsa detail page. A comparsa without a logo SHALL show a neutral placeholder in the same
place, so names stay aligned. Wherever the comparsa name is shown next to it, the logo SHALL be
decorative and SHALL NOT repeat the name to assistive technology. The logo SHALL be shown on a light
tile that keeps any logo visible in both themes, including a dark logo with a transparent
background in the dark theme, and SHALL keep its shape inside the tile. When a logo cannot be
loaded, the placeholder SHALL be shown instead of a broken image.

For Admins, the comparsa detail page SHALL have a logo section where the logo itself offers to add,
replace and remove it (see "Picture actions"), with no separate buttons. To add a logo the Admin
SHALL choose an image file and MAY crop it freely before uploading; the
crop SHALL start with the whole image selected. Removing a logo SHALL ask for confirmation first.
FiringChiefs SHALL see the logo but SHALL NOT be offered these actions. Every text SHALL be
available in es-ES, ca-ES-valencia and en.

#### Scenario: Logo in the list
- **WHEN** a FiringChief opens the comparsas page and their comparsa has a logo
- **THEN** the row shows the logo next to the comparsa name

#### Scenario: Placeholder without a logo
- **WHEN** an Admin opens the detail page of a comparsa without a logo
- **THEN** the record header shows the placeholder next to the name, and the logo section's placeholder is an "Add logo" button

#### Scenario: Admin adds a logo from the detail page
- **WHEN** an Admin chooses a PNG image in the logo section, keeps the whole image selected and confirms
- **THEN** the logo is uploaded, and the record header and the comparsas list show it

#### Scenario: Removing a logo asks first
- **WHEN** an Admin opens the menu of a comparsa's logo, chooses to remove it and then cancels the confirmation
- **THEN** the logo is kept

#### Scenario: Actions on the logo
- **WHEN** an Admin opens the detail page of a comparsa with a logo
- **THEN** the logo section shows the logo as a button that opens a menu with "Replace" and "Remove", and no other replace or remove button

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

For Admins, the comparsas page SHALL have a "Federation logo" section where the logo itself offers
to add (with free cropping, starting with the whole image), replace and remove it (removal
confirmed in a dialog), as in "Picture actions". FiringChiefs
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
