# Spec Delta

## MODIFIED Requirements

### Requirement: Exporting a person's data (UC-26)
Admins SHALL be able to download, for a `nationalId` and a request reference, a ZIP file with:
- an Excel workbook in the Admin's language, with one sheet per category that holds data:
  - the registry record (identity, contact, birth date, gender, status, comparsa, license and
    course);
  - the owned weapons;
  - the edition entries (year, comparsa, status, powder, caps, weapon source, flask, rental model
    and the identity and weapon copy);
  - the loans in which they are the lender (year, weapon model, number, ownership guide and their
    identity as stored);
  - the pickup authorisations in which they take part (year, type, and whether they are the
    holder or the proxy);
  - the powder handovers of their entries (year, date and time collected, kilograms, rented flask
    number, traceability 1 and 2, and whether they collected it or their proxy did) and those they
    collected as a proxy (year, date and time collected, rented flask number, as proxy);
  - an "About this data" sheet stating the controller, the purposes, the recipient categories and
    the retention, from translated texts;
- the person's stored photos as JPEG files named by their kind.

The file SHALL hold only the person's own data. Other people's names, DNI/NIE, contact and weapon
data SHALL be left out: the other party of a loan or a pickup authorisation appears only by its
role. Text cells SHALL be neutralised against formula injection. The file SHALL be built on
request, never stored, and sent with `Cache-Control: no-store`. The download SHALL be audited
before the file is sent, with the user, the request reference, the categories and their row
counts, and no `nationalId` or name. A file that cannot be audited SHALL NOT be sent
(`503 Service Unavailable`). A `nationalId` with no data SHALL be answered `404 Not Found`, and an
invalid one `400 Bad Request`. A FiringChief SHALL receive `403 Forbidden`.

#### Scenario: Export of an arquebusier
- **WHEN** an Admin exports the data of a synthetic arquebusier with an ID photo, two owned weapons and entries in 2029 and 2030
- **THEN** the ZIP holds a workbook with the registry, weapons, entries and "About this data" sheets, and the ID photo

#### Scenario: Borrower's identity left out
- **WHEN** an Admin exports the data of an external owner who lent a weapon to a synthetic arquebusier
- **THEN** the loans sheet shows the owner's own data and the weapon, and not the borrower's name or DNI/NIE

#### Scenario: Export is audited without identity
- **WHEN** an Admin exports a person's data with the reference "REQ-2030-07"
- **THEN** one audit entry records the user, "REQ-2030-07", the categories and their row counts, and no DNI/NIE or name

#### Scenario: Audit unavailable
- **WHEN** the audit entry of an export cannot be stored
- **THEN** the response is `503 Service Unavailable` and no file is sent

#### Scenario: Nothing to export
- **WHEN** an Admin exports a valid DNI that appears nowhere
- **THEN** the response is `404 Not Found`

#### Scenario: Handovers in the export
- **WHEN** an Admin exports the data of a synthetic arquebusier whose 2030 powder was collected by their proxy with flask "P-117"
- **THEN** the handovers sheet has one row for 2030 with "P-117" that says their proxy collected it, and not the proxy's name or DNI/NIE

#### Scenario: Handover collected for someone else
- **WHEN** an Admin exports the data of a synthetic arquebusier who collected another holder's powder as proxy with flask "P-117"
- **THEN** the handovers sheet has one row with the year, the time, "P-117" and "proxy", and neither the holder's kilograms, traceability codes, name nor DNI/NIE
