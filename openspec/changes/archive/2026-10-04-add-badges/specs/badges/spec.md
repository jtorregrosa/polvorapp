# Spec Delta

## Purpose

Lets Admins print the Federation's arquebusier badges (UC-30) from the registry. Badges come out as
an exact-size PDF print sheet, either for a whole comparsa or for a selection of arquebusiers.

## ADDED Requirements

### Requirement: Badge content (UC-30)
An `ArquebusierBadge` SHALL be derived from the registry each time it is generated. Nothing about
it SHALL be stored. Each badge SHALL show:
- a Federation green header band with the header word and the Federation name;
- the Federation logo, when an Admin has uploaded one;
- the arquebusier's ID photo, in a 3:4 portrait slot of about 20 × 26.7 mm;
- these labelled fields, with values as they are in the registry when the document is generated:

  | Field | Value |
  |---|---|
  | Surnames | `lastName` |
  | Name | `firstName` |
  | DNI/NIE | `nationalId` |
  | Code | `federationId` |
  | Expiry date | the current license's `expiresOn`, as `dd/MM/yyyy` |
  | Comparsa | the name of the arquebusier's current comparsa |

- an empty area for the Federation's hand-stamped seal.

A badge SHALL NOT show any other personal data: no birth date, age, phone, email, address, license
type, course, status, compliance warning or owned weapon. Every value SHALL be printed in full, never
cut. A long value SHALL use a smaller type size and, if still too long, a second line, while staying
inside the card's safe area.

#### Scenario: Badge of a complete arquebusier
- **WHEN** an Admin prints the badge of an arquebusier with an ID photo and a license issued until 2033-05-31
- **THEN** the badge shows the photo, the surnames, the name, the DNI/NIE, the federationId, "31/05/2033" and the comparsa name, the header band, the Federation logo when one was uploaded, and an empty seal area

#### Scenario: Long surnames are printed in full
- **WHEN** an Admin prints the badge of an arquebusier whose `lastName` is 100 characters long
- **THEN** the whole `lastName` is printed inside the safe area, and no other field overlaps it

#### Scenario: No other personal data
- **WHEN** an Admin prints the badge of an arquebusier with a phone, an email, an owned weapon and the warning `UNDER_AGE`
- **THEN** none of them appears on the badge

### Requirement: Badge print sheet
Badges SHALL be generated as one PDF of A4 portrait pages, with 10 badges per page in 2 columns and
5 rows.
- **Card**: each badge SHALL be an ISO/IEC 7810 ID-1 card of 85.60 × 53.98 mm, landscape, at exact
  size when the page is printed at 100 % scale.
- **Crop marks**: each page SHALL carry crop marks on every cutting line, outside the cards, so the
  badges can be cut by hand.
- **Safe area**: text, photo and logo SHALL stay at least 3 mm inside the card edge. The header
  band MAY reach the card edge.
- **Order**: badges SHALL fill the pages left to right, then top to bottom, in the batch's order.
  The last page MAY be partly empty, and empty positions SHALL have no crop marks of their own.
- **Fonts**: the PDF SHALL embed its fonts.
- **Photo resolution**: an ID photo SHALL be embedded at a resolution of at least 300 dpi at its
  printed size, and at no more than needed for that.
- **Metadata**: the document's title and metadata SHALL contain no personal data.
- **Deterministic output**: the same data, language and date SHALL produce the same document.

#### Scenario: Eleven badges
- **WHEN** an Admin prints a batch of 11 arquebusiers
- **THEN** the PDF has 2 A4 portrait pages, the first with 10 badges and crop marks, the second with one badge in the top-left position

#### Scenario: Exact size
- **WHEN** a badge sheet is generated
- **THEN** every card is 85.60 × 53.98 mm in the PDF's page coordinates, and no text or image of a card lies within 3 mm of its edge except the header band

### Requirement: Badge batches
An Admin SHALL generate badges for exactly one of these batches:
- **Comparsa**: every arquebusier whose current comparsa is the given one, `ACTIVE` and `RESERVE`
  (maintainer decision), whether the comparsa is active or not, in Spanish alphabetical order of
  `lastName` and then `firstName`.
- **Selection**: the given arquebusiers, from one or several comparsas and whatever their status,
  ordered by comparsa name, then as above. An arquebusier listed twice SHALL get one badge.

A document SHALL hold at most 200 badges. The following rules SHALL be blocking:
- a request naming both a comparsa and a selection, or neither, SHALL be rejected
  (`400 Bad Request` naming `batch`);
- a selection SHALL hold 1 to 200 arquebusier identifiers (`400 Bad Request` naming
  `arquebusierIds`);
- an identifier that is not in the registry SHALL be rejected (`400 Bad Request` naming
  `arquebusierIds[i]`), and no badge is generated;
- an unknown comparsa SHALL be answered with `404 Not Found` (`badges.notFound`);
- a comparsa without arquebusiers SHALL be rejected (`409 Conflict`, `badges.nothingToPrint`);
- a comparsa with more than 200 arquebusiers SHALL be rejected (`409 Conflict`, `badges.tooMany`).
  The message SHALL suggest printing a selection instead.

#### Scenario: Whole comparsa, reserves included
- **WHEN** an Admin prints the badges of a comparsa with 12 `ACTIVE` and 3 `RESERVE` arquebusiers
- **THEN** the PDF holds 15 badges in alphabetical order of surnames and names

#### Scenario: Selection across comparsas
- **WHEN** an Admin prints a selection of 2 arquebusiers of "Comparsa Sintética Sur" and 1 of "Comparsa Sintética Norte"
- **THEN** the PDF holds 3 badges, the Norte arquebusier first, then the two Sur arquebusiers in alphabetical order

#### Scenario: Deleted arquebusier in a selection
- **WHEN** an Admin requests a selection that holds the identifier of an arquebusier deleted meanwhile
- **THEN** the request is rejected with `400 Bad Request` naming that identifier, and no document is returned

#### Scenario: Too many in a selection
- **WHEN** an Admin requests a selection of 201 arquebusiers
- **THEN** the request is rejected with `400 Bad Request` naming `arquebusierIds`

#### Scenario: Empty comparsa
- **WHEN** an Admin requests the badges of a comparsa without arquebusiers
- **THEN** the request is rejected with `409 Conflict` and `badges.nothingToPrint`

### Requirement: Badge language
Each request SHALL name the labels' language: `es-ES`, `ca-ES-valencia` or `en` (maintainer
decision while Q-49 is open). Any other value SHALL be rejected (`400 Bad Request` naming
`language`). The language SHALL set the field labels, the header word and the Federation name, as
below. Names, comparsa names and identifiers SHALL never be translated.

| | es-ES | ca-ES-valencia | en |
|---|---|---|---|
| Header word | ARCABUCERO | ARCABUSSER | ARQUEBUSIER |
| Surnames | Apellidos | Cognoms | Surnames |
| Name | Nombre | Nom | Name |
| DNI/NIE | DNI/NIE | DNI/NIE | DNI/NIE |
| Code | Código | Codi | Code |
| Expiry date | Fecha de caducidad | Data de caducitat | Expiry date |
| Comparsa | Comparsa | Comparsa | Comparsa |

The Federation name SHALL be printed as in the Federation's other documents in that language.

#### Scenario: Valencian labels
- **WHEN** an Admin whose language is es-ES prints badges choosing `ca-ES-valencia`
- **THEN** the badges show "ARCABUSSER", "Cognoms", "Nom", "Codi" and "Data de caducitat", and the names and comparsa as in the registry

#### Scenario: Unsupported language
- **WHEN** an Admin requests badges with the language `fr`
- **THEN** the request is rejected with `400 Bad Request` naming `language`

### Requirement: Incomplete badges are warnings (BR-04)
Missing data SHALL never block a badge (maintainer decision, BR-04):
- **No ID photo**: the arquebusier SHALL get an empty photo frame of the slot's size, to paste a
  photo by hand.
- **No issued license** (none, or `PENDING`): the expiry SHALL be an empty line, to fill in by hand.
- **Expired license**: the badge SHALL print its `expiresOn` like any other.
- **No Federation logo**: the badges SHALL print without it, keeping the rest of the layout.

Before the download, the badge screen SHALL say how many badges of the batch have no ID photo, how
many have no issued license, and whether the Federation logo is missing.

A photo the registry holds but that cannot be read or prepared for print (its image is gone or
damaged) SHALL be blocking, so a badge never loses its photo unnoticed (maintainer decision): the
request SHALL be rejected (`409 Conflict`, `badges.photoUnreadable`) with the identifiers of the
arquebusiers concerned, and the badge screen SHALL name them so their photo can be uploaded again
or they can be left out of the selection.

#### Scenario: Arquebusier without photo or license
- **WHEN** an Admin prints the badges of a comparsa where one arquebusier has no ID photo and a pending license
- **THEN** the PDF is returned, and that badge has an empty photo frame and an empty expiry line

#### Scenario: Unreadable photo is blocking
- **WHEN** an Admin prints the badges of a comparsa where one arquebusier's stored ID photo is missing from the storage
- **THEN** the request is rejected with `409 Conflict` and `badges.photoUnreadable` naming that arquebusier, no document is returned, and the badge screen names them

#### Scenario: Warning before the download
- **WHEN** an Admin opens the badge screen for a batch of 20 with 2 arquebusiers without ID photo and 1 without an issued license, and no Federation logo uploaded
- **THEN** the screen says that 2 badges have no photo, 1 has no expiry date and the Federation logo is missing, and the download is still offered

### Requirement: Badge access and document handling
Only Admins SHALL generate badges (UC-30). A FiringChief's request SHALL be answered with
`403 Forbidden`. A request without a session SHALL be answered with `401 Unauthorized`, or with
`400 Bad Request` when the platform's anti-forgery check refuses it first; it SHALL return no
document either way.

Badge documents SHALL be generated for each request and never stored. Their responses SHALL forbid
caching. File names SHALL contain no personal data: the comparsa's name slug for a comparsa batch,
the word "selection" and the count for a selection, and the date. Badge downloads SHALL share the
per-user document rate limit, answering `429 Too Many Requests` beyond it.

When the storage cannot be read, for the Federation logo or for any ID photo of the batch, the
request SHALL answer `503 Service Unavailable` (`storage.unavailable`) with a translated, retryable
message. It SHALL NOT return a document with the logo or a photo silently left out.

#### Scenario: FiringChief cannot print badges
- **WHEN** a FiringChief requests the badges of their own comparsa
- **THEN** the API responds `403 Forbidden` and returns no document

#### Scenario: Storage down
- **WHEN** an Admin requests badges while the photo storage cannot be reached
- **THEN** the API responds `503 Service Unavailable` with `storage.unavailable`, and nothing is audited as downloaded

#### Scenario: File name
- **WHEN** an Admin downloads the badges of "Comparsa Sintética Norte" on 2031-03-02
- **THEN** the file is named `polvorapp-badges-comparsa-sintetica-norte-20310302.pdf`, and the response forbids caching

### Requirement: Badge downloads are audited
Every badge document returned SHALL be audited before the file is sent, with the batch kind, the
comparsa when there is one, the language, the count and the arquebusiers' identifiers. It SHALL NOT
hold names, DNI/NIE or images. When the audit cannot be written, the API SHALL answer
`503 Service Unavailable` and SHALL NOT return the document. A rejected request SHALL NOT be
audited. The audit log SHALL show the action with a translated label in the three languages.

#### Scenario: Download audited
- **WHEN** an Admin downloads a selection of 3 badges in `en`
- **THEN** one audit entry records the selection, the language `en`, the count 3 and the three arquebusier identifiers, with no name or DNI/NIE

#### Scenario: Audit unavailable
- **WHEN** an Admin requests badges while the audit trail cannot be written
- **THEN** the API responds `503 Service Unavailable` and returns no document

### Requirement: Badge screens
The UI SHALL offer badges to Admins only:
- **Selection**: the arquebusiers list SHALL let Admins select rows, keep the selection while they
  page, sort or filter, show how many are selected, and offer "Print badges" for the selection. It
  SHALL refuse more than 200 and say why. Leaving the list SHALL clear the selection.
- **Whole comparsa**: the arquebusiers list filtered by one comparsa, with nothing selected, and the
  comparsa detail page SHALL offer "Print badges" for that comparsa.
- **Badge sheet**: both actions SHALL open one sheet. It SHALL show the batch (the comparsa, or the
  number selected), the language choice (defaulting to the user's language), the warnings of
  "Incomplete badges are warnings", a reminder to print at 100 % scale, and the download. A refused
  download SHALL show its translated reason.

FiringChiefs SHALL see no selection and no badge action. The screens SHALL work on a phone (NFR-01).
Every text SHALL be available in es-ES, ca-ES-valencia and en.

#### Scenario: Admin prints a selection
- **WHEN** an Admin selects 3 arquebusiers in the list, chooses "Print badges", keeps the language and downloads
- **THEN** a PDF named with "selection" and 3 is saved, and the selection is kept

#### Scenario: Selection kept across pages
- **WHEN** an Admin selects 2 arquebusiers on the first page, moves to the second page and selects 1 more
- **THEN** the list says 3 are selected, and the badge sheet prints those 3

#### Scenario: FiringChief sees no badge action
- **WHEN** a FiringChief opens the arquebusiers list and their comparsa's page
- **THEN** no row checkbox and no "Print badges" action is shown

#### Scenario: Refused download explained
- **WHEN** an Admin downloads the badges of a comparsa with 230 arquebusiers
- **THEN** the sheet says that a document holds at most 200 badges and suggests printing a selection
