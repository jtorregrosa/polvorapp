# Spec Delta

## MODIFIED Requirements

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

For Admins, the Settings page (see "Settings screen") SHALL have a "Federation logo" section where the logo itself offers
to add (with free cropping, starting with the whole image), replace and remove it (removal
confirmed in a dialog), as in "Picture actions". FiringChiefs
SHALL NOT be offered these actions. Every text SHALL be available in es-ES, ca-ES-valencia and en,
and the section SHALL pass automated accessibility checks (NFR-07).

#### Scenario: Admin uploads the Federation logo
- **WHEN** an Admin uploads a valid PNG as the Federation logo from the Settings page
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

#### Scenario: Logo no longer on the comparsas page
- **WHEN** an Admin opens the comparsas page
- **THEN** it has no Federation logo section, and the Settings page has it

## ADDED Requirements

### Requirement: Federation settings
The Federation SHALL have one set of settings, changed only by Admins, that other capabilities
read on the server. It SHALL hold:
- **Identity**: the Federation's official name in Spanish and in Valencian, each 1 to 150
  characters after trimming, printed in its documents (English documents use the Spanish form), a
  short name of 1 to 40 characters used in the interface and emails, and an optional public contact
  email address and website, shown at the end of notification emails;
- **Emails**: the sender name shown with the configured sender address (1 to 80 characters,
  without `<`, `>`, `"` or line breaks), and an optional reply-to address. The sender address itself
  SHALL stay a deployment setting, because it depends on the mail domain's configuration;
- **Orders**: how many days before the planned close the first close reminder is sent, from 2 to 14
  (default 7);
- **Calendar**: how many days before a milestone its reminder is sent, from 1 to 14 (default 7);
- the Federation logo (see "Federation logo").

The settings SHALL start with the Federation's current names, the short name "Unión de Comparsas",
the sender name "PolvorApp", no reply-to, no contact data, and 7 and 7 days. They SHALL NOT hold
passwords, keys or other secrets, nor any person's data: the contact address and website SHALL be
the Federation's, and the screen SHALL say so.

These rules SHALL be blocking (`400 Bad Request` naming the field and the reason): lengths and
ranges as above (`required`, `tooLong`, `outOfRange`); email addresses and the website (`https`
only) SHALL be valid (`invalid`). Every signed-in user SHALL be able to read the identity; only
Admins SHALL read the other settings and change any of them, and FiringChiefs SHALL receive
`403 Forbidden`. Changes SHALL be saved per section, SHALL be rejected with `409 Conflict` when
based on an older version, and SHALL be audited with the previous and new values.

#### Scenario: Admin changes the sender name
- **WHEN** an Admin sets the sender name to "Unión de Comparsas · PolvorApp"
- **THEN** later emails are sent from that name at the configured sender address, and the change is audited with the previous and new names

#### Scenario: Reply-to address
- **WHEN** an Admin sets the reply-to address to "secretaria@federacion.example" and a user replies to a notification
- **THEN** the reply is addressed to "secretaria@federacion.example"

#### Scenario: Documents use the configured name
- **WHEN** an Admin changes the Valencian official name and then downloads a pickup authorisation form in Valencian
- **THEN** the form prints the new Valencian name

#### Scenario: Reminder lead time
- **WHEN** an Admin sets the first close reminder to 10 days and the planned close is 10 days ahead with a `DRAFT` order
- **THEN** the order's FiringChiefs receive the first close reminder that day

#### Scenario: Out-of-range value is blocking
- **WHEN** an Admin sets the milestone reminder to 30 days
- **THEN** the request is rejected with `400 Bad Request` naming the field with `outOfRange`, and nothing changes

#### Scenario: Header injection is blocking
- **WHEN** an Admin sets a sender name containing a line break
- **THEN** the request is rejected with `400 Bad Request` naming the field with `invalid`

#### Scenario: FiringChief cannot change settings
- **WHEN** a FiringChief tries to change any setting through the API
- **THEN** the API responds `403 Forbidden` and nothing changes

#### Scenario: Concurrent edit
- **WHEN** two Admins open the emails section and both save, the second based on the older version
- **THEN** the second save is rejected with `409 Conflict` and a translated message to reload

### Requirement: Settings screen
The UI SHALL offer Admins a "Settings" page in the "Administration" section of the main navigation.
It SHALL follow the detail page in read mode: one section per group (Identity, Federation logo,
Emails, Orders, Calendar), each showing its values and edited in a side panel (a bottom sheet on
phones) with its own save, validation messages next to the fields and an error summary. Each
section SHALL say in one sentence where its values are used. The page SHALL be laid out so later
sections can be added without changing the existing ones. FiringChiefs SHALL NOT see the entry
and SHALL get the translated "not allowed" page at its address. The page SHALL work on a phone
(NFR-01), SHALL have no automatically detectable WCAG 2.2 AA violations in either theme (NFR-07),
and every text SHALL be available in es-ES, ca-ES-valencia and en.

#### Scenario: Admin edits the identity
- **WHEN** an Admin opens Settings, edits the short name in the Identity panel and saves
- **THEN** the panel closes, the section shows the new short name, and a save notice is announced

#### Scenario: Validation in the panel
- **WHEN** an Admin clears the Spanish official name and saves
- **THEN** the panel stays open with an error summary and the message next to the field, and nothing is saved

#### Scenario: FiringChief opens the settings
- **WHEN** a FiringChief navigates to the Settings page
- **THEN** a translated "not allowed" page is shown inside the shell and no setting is requested
