# Proposal

## Why

Each comparsa keeps its arquebusiers in its own workbook (`docs/current-state.md` §2, about 85 rows
in the sample). Before PolvorApp can replace those workbooks, the Federation has to load about 800
existing arquebusiers. Typing each one through the registration form would take days and would
copy the known data-quality problems by hand: split or spaced DNIs, missing check letters,
look-alike characters and duplicates (`docs/current-state.md` §7). The registry, its validation and
the compliance warnings are now complete (#5–#7). Festival editions and orders (#9, #10) need a
populated registry, so the import is the next step.

Capability (from `docs/mvp.md`): **`arquebusier-registry`**, change #8 of the sequence. It
implements **UC-09** (bulk import of arquebusiers from a spreadsheet, initial load, Admin). It
enforces **BR-01** (DNI/NIE validation) and **BR-02** (Federation-wide uniqueness) as blocking
rules, applies **BR-03** (default license expiry), and shows **BR-04** compliance warnings without
blocking. It keeps **BR-12** on the server, because only Admins can import. It follows ADR-0001
(the import lives in the registry module), ADR-0002, ADR-0007 (three locales), ADR-0008 (Excel
through ClosedXML on the server), and ADR-0009 and ADR-0011 (composites and tokens only).

## What Changes

- **Import template.** An Admin downloads a PolvorApp `.xlsx` template in their language. It has:
  - one sheet with a fixed set of columns;
  - drop-down lists for gender, status and license type;
  - text-formatted ID and phone columns, so leading zeros survive;
  - a second sheet that explains each column.

  The template holds no data.
- **Import of arquebusiers (UC-09)**, for Admins only, into one active comparsa chosen on the
  screen. It covers:
  - the personal data;
  - the status (`ACTIVE` by default);
  - the current license: none, pending, or issued with its dates (BR-03 default expiry);
  - the training course date.

  Owned weapons and photos are added by hand afterwards (maintainer decision). The import only
  creates arquebusiers. A row whose `nationalId` or `federationId` already exists in the registry is
  an error, and existing data is never changed (maintainer decision).
- **Validation report and all-or-nothing import** (maintainer decision). The steps are:
  1. The Admin uploads the file and gets a report for each row: the blocking errors per column and
     the compliance warnings (BR-04).
  2. The Admin can confirm only when no row has an error.
  3. The confirmation sends the same file again. The server validates it again and creates every
     arquebusier in one transaction, or none.

  The report is not stored and not downloadable. The checks are:
  - every rule of the registration form (BR-01, the field rules, the license rules);
  - duplicates inside the file and against the registry (BR-02);
  - file-level problems: wrong format, missing or repeated columns, too many rows.
- **Tolerant reading of the cells.** Columns are recognised by their header in any of the three
  languages, in any order. The reader accepts:
  - dates as date cells, `dd/mm/yyyy` or `yyyy-mm-dd`;
  - numbers stored as numbers;
  - DNIs typed without leading zeros, which are padded (the check letter does not change);
  - the drop-down values in any language and the codes.
- **Audit.** A confirmed import records one `ArquebusierRegistered` entry per arquebusier, as a
  manual registration does, and one summary entry with the comparsa and the count. None of them
  holds personal values. A preview writes nothing and records nothing.
- **Import screen.** Admins open it from the Arquebusiers page. On it they:
  - choose the comparsa;
  - download the template;
  - pick the file;
  - read the report: a summary, the file problems, and the rows with errors or warnings;
  - confirm the import.

  After the import they land on the list filtered by that comparsa. The screen works on a phone
  and is translated into the three locales.

## Non-goals

- **Mapping any spreadsheet's columns on screen**, or reading the comparsas' current workbooks
  as they are. Only the PolvorApp template is read (maintainer decision). The Admin copies the
  columns into it.
- **Updating existing arquebusiers** or synchronising with the Federation's external app. Only new
  arquebusiers are created.
- **Partial imports** that skip rows with errors.
- **Owned weapons, photos and transfers** in the import.
- **Import by FiringChiefs** (UC-09 is an Admin use case).
- **Several comparsas in one file.**
- **Downloading the report** or keeping uploaded files on the server.
- **CSV or `.xls`/`.xlsm` files.** Only `.xlsx` is read.
- **The registry lock** (BR-10, UC-11). It arrives with the editions (#9), and the import will then
  respect it like any other registry write.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `arquebusier-registry`: new requirements for the spreadsheet import (UC-09):
  - the import template;
  - the reading of the file;
  - the validation report;
  - the all-or-nothing import;
  - its audit entries;
  - the import screen.

  The existing requirements do not change.

## Impact

- **Backend**:
  - `Modules/ArquebusierRegistry` gains an `Import` folder. It holds the template writer, the
    workbook reader, the row validation, which reuses `RegistryInput.Read` and `NationalId.Parse`,
    and a batch registration in `ArquebusierAdministration`, which reuses the duplicate checks, the
    write guard and the audit trail.
  - Report rows get their warnings from `IComplianceRules`.
  - No new module, no new table and no migration.
- **Dependencies**: `ClosedXML` (MIT, chosen in ADR-0008) in the registry module, through central
  package management. `write-excel-file` (MIT) is a frontend dev dependency used only by the E2E
  tests to build synthetic workbooks.
- **API**: three new Admin-only endpoints under `/api/arquebusiers/import`:
  - `GET template`;
  - `POST preview`;
  - `POST` (the import itself).

  A new rate-limit policy is added for spreadsheet uploads. `contracts/openapi.json` and the orval
  client are regenerated. The change is additive, with no breaking change.
- **Frontend**:
  - `features/arquebusier-registry` gains the import page, an Admin-only route and an "Import"
    action on the Arquebusiers page;
  - a new `FileField` composite in `components/app/`, with its story and axe test;
  - new `registry:import.*` keys in the three locales.
- **Docs**:
  - `docs/use-cases.md` (UC-09 note);
  - `docs/data-model.md` (import note);
  - `docs/development.md` (API contract, rate-limit variable);
  - `backend/src/Modules/README.md` (spreadsheet uploads);
  - `docs/third-party-licenses.md` (ClosedXML, `write-excel-file`);
  - `docs/compliance.md` (SEC-05, SEC-11 notes);
  - `docs/design/` (the import page pattern and `FileField`);
  - `docs/mvp.md` (status of #8).
- **Security and GDPR**:
  - **Upload limits.** Only Admins can upload. Files are read in memory and never stored, and
    reading them is size- and row-limited and guarded against zip bombs.
  - **No personal data in logs or audit entries.** Report responses are `no-store`.
  - **Template.** The template carries no personal data.
  - **Synthetic data only.** Tests and E2E fixtures generate their workbooks (SEC-11).
- **ADRs**: none new. ClosedXML is already chosen in ADR-0008.
