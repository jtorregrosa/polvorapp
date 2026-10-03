# Spec Delta

## Purpose

Produces the files the Federation sends to the powder supplier, the weapon rental company and the
Arms Authority, and each comparsa's list, from the validated orders of a festival edition (UC-17),
holding only what each recipient needs (SEC-06).

## ADDED Requirements

### Requirement: Export definitions
Each export SHALL follow a named, versioned export definition that fixes its rows, columns, column
order and formatting: `powder-supplier`, `rental-company`, `arms-authority` and `comparsa-list`.
While the recipients' templates are pending (Q-44), every definition SHALL be **provisional**. A
provisional export SHALL say so in its file name (`-provisional`), in the first line of the
document, and on the screen that offers it. The definition's name and version SHALL appear in the
document. A file SHALL be generated on each request and SHALL NOT be stored.

#### Scenario: Provisional file
- **WHEN** an Admin downloads the powder supplier export of the edition 2031 as Excel
- **THEN** the file is named `polvorapp-2031-powder-supplier-provisional.xlsx`, and its first line says that it is a provisional format pending the recipient's template, with the definition name and version

#### Scenario: Nothing is stored
- **WHEN** an Admin downloads an export twice
- **THEN** both files are generated from the data at the time of each request, and no file is kept on the server

### Requirement: Orders included
The recipient exports (`powder-supplier`, `rental-company`, `arms-authority`) SHALL include only
the `VALIDATED` orders of the edition (maintainer decision). The `comparsa-list` SHALL be available
for a comparsa's order in any status (see "Comparsa list export"). Entries of
arquebusiers no longer in the registry SHALL be included with their history copy. Comparsas, rows
and totals SHALL follow the Spanish alphabetical order: comparsas by name, people by last name and
first name. An edition without validated orders SHALL produce a file with its headers and no rows,
not an error.

#### Scenario: Only validated orders
- **WHEN** the edition has a `VALIDATED` order of "Comparsa Sintética Norte" and a `SUBMITTED` order of "Comparsa Sintética Sur"
- **THEN** every recipient export of the edition includes Norte's entries and none of Sur's

#### Scenario: No validated order yet
- **WHEN** an Admin downloads the Arms Authority export of an edition without validated orders
- **THEN** the file has its provisional line and headers and no rows

#### Scenario: Entry no longer in the registry
- **WHEN** a validated order has an entry whose arquebusier was deleted from the registry
- **THEN** the exports show that entry with the name and DNI/NIE of its copy

### Requirement: Powder supplier export
The `powder-supplier` export SHALL have one row per comparsa with a validated order — comparsa
name, powder in kilograms, `NORMAL` caps boxes and `SMALL` caps boxes — and a total row. It SHALL
contain no personal data (SEC-06).

#### Scenario: Totals per comparsa
- **WHEN** Norte's validated order has 5 kg, 3 `NORMAL` and 1 `SMALL` caps boxes and Este's has 2 kg and nothing else
- **THEN** the export has the rows "Comparsa Sintética Este 2 0 0" and "Comparsa Sintética Norte 5 3 1" and the total "7 3 1", and no name or DNI/NIE

### Requirement: Rental company export
The `rental-company` export SHALL have one row per entry with weapon source `RENTAL` or a flask
`RENTAL_1KG` or `RENTAL_2KG`: last name and first name, DNI/NIE, comparsa, the rented weapon model
(empty when the weapon is not rented) and the rented flask size (empty when the flask is not
rented). It SHALL NOT contain license, owned weapon or loan data.

#### Scenario: Weapon and flask rentals
- **WHEN** a validated order has one entry renting "ARCABUZ MORO DIESTRO" and a 2 kg flask, one renting only a 1 kg flask, and one with an owned weapon and flask
- **THEN** the export has two rows, the first with the model and "2 kg", the second with no model and "1 kg", and none for the third entry

### Requirement: Arms Authority export
The `arms-authority` export SHALL have one row per `ACTIVE` entry with a weapon (`OWNED`,
`RENTAL` or `LOAN`): last name and first name, DNI/NIE, comparsa, license type and expiry date,
weapon model, weapon number, ownership guide number, and the weapon source. For a loan it SHALL
also give the lender's name and DNI/NIE and the lent weapon's model, number and ownership guide.
A rented weapon's number SHALL be empty until units are assigned at distribution (#13). The
license SHALL come from the registry at the time of the export; an arquebusier in the registry
without a license SHALL read "Sin licencia", and an entry no longer in the registry SHALL have the
license columns empty. Entries without a weapon and `RESERVE` entries SHALL NOT be
listed.

#### Scenario: Owned, rented and lent weapons
- **WHEN** a validated order has an `ACTIVE` entry with an owned weapon, one with a rented model, one with a weapon lent by an external owner, one with no weapon, and a `RESERVE` entry
- **THEN** the export has three rows: the owned weapon with its number and guide, the rental with its model and no number, and the loan with the lender's name and DNI/NIE and the lent weapon's data

#### Scenario: License from the registry
- **WHEN** an arquebusier's license (type `AE`) expires on 2033-05-31
- **THEN** their row shows the type `AE` and the expiry date 31/05/2033

### Requirement: Comparsa list export
The `comparsa-list` export SHALL list every entry of one comparsa's order in the edition, whatever
its status (maintainer decision): last name and first name, DNI/NIE, ID Unión, status, powder,
caps (boxes and type), weapon (source and model, number or lender) and flask, with the order's
totals and its status. While the order is not `VALIDATED` (`DRAFT`, `SUBMITTED` or `RETURNED`) the
list SHALL be a **draft**: its file name SHALL say so (`-draft`) and its first line SHALL say that
the order is not validated yet and may change, with its status. A comparsa without an order in the
edition SHALL be answered `409 Conflict` with `exports.notPrepared`, and nothing SHALL be generated
or audited. The list SHALL be in the language of the user who downloads it; the other exports SHALL
be in Spanish (es-ES), the recipients' language.

#### Scenario: A comparsa's list
- **WHEN** a user downloads Norte's list for the edition 2031 in ca-ES-valencia while the order is `VALIDATED`
- **THEN** the file lists every entry of Norte's order, with its totals, with the headings in Valencian, and is not marked as a draft

#### Scenario: Draft list
- **WHEN** the FiringChief of Norte downloads Norte's list while the order is `DRAFT`
- **THEN** the file is named `polvorapp-2031-comparsa-list-comparsa-sintetica-norte-draft-provisional.xlsx`, and its first line says that the order is a draft, not validated yet, and may change

#### Scenario: No order
- **WHEN** a user asks for the list of a comparsa that has no order in the edition
- **THEN** the API responds `409 Conflict` with `exports.notPrepared`, and nothing is generated or audited

### Requirement: Excel and PDF
Every export SHALL be available as Excel (`.xlsx`) and as PDF (maintainer decision), with the same
rows and columns. The Excel file SHALL have one worksheet with a header row and typed cells
(numbers as numbers, dates as dates). The PDF SHALL be A4, landscape when it has more than six
columns, repeating the header on every page and numbering the pages. An unknown format or
definition SHALL be answered `404 Not Found`.

#### Scenario: Same export in both formats
- **WHEN** an Admin downloads the rental company export as Excel and as PDF
- **THEN** both list the same rows, in the same order, with the same columns

#### Scenario: Unknown format
- **WHEN** a user asks for an export with the format `csv`
- **THEN** the API responds `404 Not Found`

### Requirement: Who may export (BR-12)
Only Admins SHALL download the `powder-supplier`, `rental-company` and `arms-authority` exports,
of any edition. A FiringChief SHALL receive `403 Forbidden` for them. Admins SHALL download the
`comparsa-list` of any comparsa; a FiringChief only of a comparsa in their scope, and only of an
edition that is not `DRAFT`; any other request SHALL be answered `404 Not Found` (BR-12).

#### Scenario: FiringChief and a recipient export
- **WHEN** a FiringChief requests the Arms Authority export
- **THEN** the API responds `403 Forbidden`, and nothing is generated or audited

#### Scenario: FiringChief and another comparsa's list
- **WHEN** a FiringChief requests the list of a comparsa not assigned to them
- **THEN** the API responds `404 Not Found`

#### Scenario: FiringChief downloads their list
- **WHEN** the FiringChief of Norte requests Norte's list of the edition in progress, whose order is submitted
- **THEN** the file is returned, marked as a draft

### Requirement: Exports are audited
Every export returned SHALL be recorded in the audit trail before the file is sent (SEC-05): the
user, the definition and its version, the format, the edition, the comparsa and the order status
for a comparsa list, and the number of rows. The entry SHALL contain no personal data. A refused request SHALL NOT be
recorded.

#### Scenario: Download audited
- **WHEN** an Admin downloads the Arms Authority export as PDF
- **THEN** one audit entry records the Admin, `arms-authority`, its version, `PDF`, the edition and the number of rows, and no name or DNI/NIE

### Requirement: Exports are protected in transit
Export responses SHALL carry `Cache-Control: no-store`, SHALL require a signed-in user like every
API route, and SHALL be limited per user (`429 Too Many Requests` beyond the limit), because
generating them is expensive and they hold personal data.

#### Scenario: Not cached
- **WHEN** a user downloads an export
- **THEN** the response carries `Cache-Control: no-store`

#### Scenario: Too many exports
- **WHEN** a user requests more exports in a minute than the limit allows
- **THEN** the API responds `429 Too Many Requests`

### Requirement: Exports screens
Admins SHALL have an "Exports" page per edition, linked from the edition's orders overview. It
SHALL show:
- that the formats are provisional until the recipients' templates arrive;
- which comparsas have no validated order yet, by status (not prepared, draft, submitted,
  returned), as a warning that does not block the downloads;
- for each recipient export, what it contains, and a download as Excel and as PDF;
- the list of each comparsa with an order, as Excel and as PDF, marked as a draft while the order
  is not validated.

The order page SHALL offer "Download list" as Excel and as PDF to Admins and to the comparsa's
FiringChiefs, for an order in any status; while the order is not validated, the action SHALL say
that the list is a draft. A download SHALL show that it is in progress and, if it fails, the
translated reason. The pages SHALL work on a 360 px wide phone (NFR-01), every text SHALL be
available in es-ES, ca-ES-valencia and en, and the pages SHALL pass automated accessibility checks
(NFR-07).

#### Scenario: Admin sees what is missing
- **WHEN** an Admin opens the exports of the edition with Norte validated, Sur submitted and Este not prepared
- **THEN** the page warns that Sur (submitted) and Este (not prepared) will not be included, and every download is available

#### Scenario: FiringChief downloads from the order page
- **WHEN** a FiringChief opens their `VALIDATED` order
- **THEN** the page offers "Download list" as Excel and as PDF, and no recipient export

#### Scenario: Draft list from the order page
- **WHEN** a FiringChief opens their `DRAFT` order
- **THEN** the page offers "Download list" as Excel and as PDF, saying that the list is a draft

#### Scenario: FiringChief has no exports page
- **WHEN** a FiringChief opens the exports page of an edition
- **THEN** the page says it is for Admins only, as the other Admin pages do, and the orders overview shows them no link to it
