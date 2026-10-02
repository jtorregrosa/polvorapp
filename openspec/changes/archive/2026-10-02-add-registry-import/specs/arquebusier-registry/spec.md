# Spec Delta

## ADDED Requirements

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
