# Proposal

## Why

Once the comparsas' orders are validated, the Federation re-types them into spreadsheets for three
recipients: the `PowderSupplier` (the powder to order), the `WeaponRentalCompany` (who rents which
weapon and flask) and the `ArmsAuthority` (who fires with which weapon, to authorise the acts)
(`docs/current-state.md` §6; answers to the discovery questionnaire). PolvorApp now holds every
validated order (#10), so it can produce those files directly, identical for every user and
audited (ADR-0008). The recipients' real templates have not arrived yet (Q-44); the Federation
needs working files now, and changing to the real layouts later must touch only one definition.

Capability (from `docs/mvp.md`): **`exports`**, change #12 of the sequence. It implements:
- **UC-17**: exports for the powder supplier, the weapon rental company, the Arms Authority and
  per-comparsa lists, as Excel and PDF.

It relies on these rules and decisions:
- **SEC-06**: each export holds only the columns its recipient needs (data minimisation). The
  powder supplier gets totals without personal data.
- **SEC-05**: every export is recorded in the audit trail.
- **BR-12**: a FiringChief exports only the lists of their own comparsas.
- **ADR-0008**: server-side generation with ClosedXML (Excel) and QuestPDF (PDF); each export is a
  named, versioned export definition, tested with golden files.

ADRs followed: ADR-0001 (a new `Exports` module, without data), ADR-0002, ADR-0007, ADR-0008,
ADR-0009 and ADR-0011.

## What Changes

- **Export definitions** (Q-44 pending): four named, versioned definitions —
  `powder-supplier`, `rental-company`, `arms-authority` and `comparsa-list` — each with its
  columns, order and formatting. Until the Federation sends the real templates they are
  **provisional**: the file name, the document's first line and the UI say so, so a provisional
  file is never mistaken for the final one. A real template later changes only its definition.
- **Which orders** (maintainer decisions): the recipient exports include only `VALIDATED` orders
  of the edition; before downloading, the Admin sees which comparsas are not validated yet (not
  prepared, draft, submitted or returned), and the download is still allowed. The comparsa list is
  available for an order in any status, marked as a **draft** until the order is validated.
- **Formats** (maintainer decision): every export as Excel (`.xlsx`) and as PDF.
- **Provisional contents** (maintainer decision, SEC-06):
  - powder supplier: per comparsa and in total, the powder in kilograms and the caps boxes of each
    type; no personal data;
  - rental company: one row per entry that rents a weapon or a flask: name, DNI/NIE, comparsa,
    weapon model, flask size;
  - Arms Authority: one row per `ACTIVE` entry with a weapon: name, DNI/NIE, comparsa, license type
    and expiry, weapon model, number and ownership guide, and whether it is owned, rented or lent
    (with the lender's name and DNI/NIE);
  - comparsa list: the order's entries as the order page shows them, with its status.
- **Who** (maintainer decision): Admins download every export of any edition; a FiringChief
  downloads the comparsa list of their own comparsas only.
- **Screens**: an "Exports" page per edition for Admins (linked from the orders overview), and a
  "Download list" action on the order page, which says when the list is a draft.
- **Audit**: every download records the user, the definition and its version, the format, the
  edition and, for a comparsa list, the comparsa, with no personal data.

## Non-goals

- The recipients' final templates (Q-44); they replace the provisional definitions when they
  arrive.
- Sending files to recipients (email, portals): the Federation downloads and sends them.
- Distribution lists, proxy forms and badges (#13, #16), though they reuse the document pipeline.
- Rental unit numbers and flask numbers, assigned at distribution (#13, UC-21).
- Recipient exports of orders that are not validated, or editing data from an export.
- Scheduled or bulk (ZIP) exports.

## Capabilities

### New Capabilities
- `exports`: export definitions and their provisional marking, the orders they include, the four
  recipients' contents, Excel and PDF, permissions, audit, rate limit and screens.

### Modified Capabilities
None. The orders overview gains a link and the order page a download action, specified in
`exports`. The registry's read contract gains the license type, which is an implementation detail.

## Impact

- **Backend**:
  - A new module, `Modules/Exports` (`PolvorApp.Exports` and `.Contracts`), without a schema: the
    export definitions, the Excel and PDF writers and the endpoints.
  - New dependency **QuestPDF** (Community license, already assessed in
    `docs/third-party-licenses.md`); ClosedXML is already used.
  - `ComparsaOrders.Contracts`: a read contract for the validated orders of an edition, and for one
    comparsa's order in any status, with their entries' copies and loans.
  - `ArquebusierRegistry.Contracts`: the license facts gain the license type.
  - `SharedKernel.Auditing`: a standalone audit log for actions that write nothing (exports),
    implemented by `AuditPrivacy`.
  - A per-user rate-limit policy for exports.
- **Frontend**: an exports page (`features/exports`), the download action on the order page, i18n in
  three locales, the generated client.
- **Docs**: `docs/data-model.md` (export definitions), `docs/use-cases.md` (UC-17 notes),
  `docs/compliance.md` (SEC-06 per recipient), `docs/third-party-licenses.md` (QuestPDF in use),
  the modules README.
- **Security and GDPR**: the files contain personal data (except the supplier's); they are
  generated on demand, never stored, sent with `no-store`, and every download is audited.
