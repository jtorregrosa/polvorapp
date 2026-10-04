# Proposal

## Why

The Federation gives each arquebusier a badge (`ArquebusierBadge`) to wear around the neck during
the acts. Today someone designs every badge by hand from the photos and data the comparsas send
(Q-45). PolvorApp already holds that data: the ID photos (#6, NFR-15), the license expiry (#5), the
comparsas and the Federation logo (#13). With them it can print every badge from the same data, at
exact size, and reprint a single badge after a renewal or a new photo. This is the last change of
the MVP sequence.

Capability (from `docs/mvp.md`): **`badges`**, change #16 of the sequence. It implements:
- **UC-30**: generate printable arquebusier badges as a PDF, one or many (Admins).

It relies on these rules and decisions:
- **`docs/data-model.md` §4**: the badge content and physical format. It is a derived document and
  stores nothing.
- **NFR-15**: the 3:4 ID photo of at least 600 × 800 px, so the photo prints sharp.
- **BR-04**: compliance checks are warnings. A badge with a missing photo or no issued license is
  still printed, with a warning before the download (maintainer decision).
- **BR-12**: badges are an Admin tool (UC-30 actor AD). FiringChiefs get no access.
- **SEC-05 / SEC-06**: every download is audited, and the document holds only what the badge shows.
- **Q-49** (badge labels only in Spanish?) is still open with the Federation. Until it is answered,
  the Admin chooses the labels' language for each download: es-ES, ca-ES-valencia or en
  (maintainer decision).
- **Q-61**: the Federation logo uploaded at run time is printed as the coat of arms. It is never
  committed.
- **ADR-0001** (the module owns the badge contents), **ADR-0008** (server-side documents through the
  existing rendering contract), ADR-0005 (image processing), ADR-0009, ADR-0011.

## What Changes

- **Badge** (`docs/data-model.md` §4): an ID-1 card, 85.60 × 53.98 mm, landscape. It has a Federation
  green header band with the word "ARCABUCERO" and the Federation name, and the Federation logo. It
  shows the ID photo in a 3:4 slot, then labelled fields: surnames (`lastName`), name (`firstName`),
  DNI/NIE (`nationalId`), code (`federationId`), license expiry (`expiresOn`) and comparsa. A free
  area is left empty for the Federation's hand-stamped seal. Text and photo stay at least 3 mm from
  the card edge. Values are printed in full, never cut.
- **Print sheet**: an A4 portrait PDF with 10 badges per sheet (2 × 5), each at exact size, with crop
  marks for cutting by hand. It is printed at 100 % scale.
- **Batches** (maintainer decisions):
  - *per comparsa*: every arquebusier of one comparsa, `ACTIVE` and `RESERVE`;
  - *per selection*: the arquebusiers an Admin ticks in the registry list, from one or several
    comparsas, whatever their status.
  - At most 200 badges per document.
- **Language** (maintainer decision, Q-49 open): the Admin chooses es-ES, ca-ES-valencia or en for
  each download. It defaults to the Admin's own language and sets the labels, the header word and the
  Federation name. Names and comparsas are never translated.
- **Incomplete badges** (maintainer decision, BR-04 as warnings): an arquebusier without an ID photo
  gets an empty photo frame. One without an issued license (none, or pending) gets a blank expiry
  line, to fill in by hand. Before the download, the screen says how many badges in the batch are
  incomplete and why, and whether the Federation logo is missing. None of these blocks the download.
- **Screens**:
  - the registry list gains row selection for Admins, with a "Print badges" action for the
    selection;
  - a "Print badges" action on the registry list (filtered by a comparsa) and on the comparsa
    detail page prints the whole comparsa;
  - both actions open one sheet: the batch, the language, the warnings and the download.
- **Audit**: every download is audited, with the arquebusiers' ids, the count, the language and the
  batch kind. The audit holds no names or DNI/NIE.

## Non-goals

- Storing badges, issue dates, badge numbers or a "printed" flag; reprints and "only renewed
  licenses" filters based on print history. A selection covers the reprint case.
- A QR code, barcode or any data beyond `docs/data-model.md` §4.
- Badges for FiringChiefs, or for anyone outside the registry.
- A badge designer or other card layouts; double-sided printing; printing on PVC cards.
- Choosing the badge colours: the Federation green is fixed, and the app keeps the PolvorApp
  identity elsewhere.
- Sending badges by email, or arquebusiers downloading their own.
- Requiring a photo or a valid license to print (compliance stays a warning).

## Capabilities

### New Capabilities
- `badges`: badge content and layout, the print sheet, batches and limits, the labels' language,
  incomplete badges, permissions, audit, the badge screens and the registry list selection.

### Modified Capabilities
- `design-system`: requirement "Data tables". A table may offer row selection with a checkbox per
  row and a "select all on this page" checkbox. Selecting a row never opens the record.

## Impact

- **Backend**:
  - A new module, `Modules/Badges` (`PolvorApp.Badges` and `.Contracts`, no schema): the badge
    endpoint, batch rules, texts, layout data and audit.
  - `ArquebusierRegistry.Contracts`: a read contract for an arquebusier's ID photo bytes, with the
    same retry as the photo endpoint.
  - `Exports.Contracts` and `Exports`:
    - a badge-sheet document (`DocumentBadgeSheet`) rendered by the existing QuestPDF setup and
      fonts;
    - `DocumentImage` accepts JPEG as well as PNG.
  - `SharedKernel.Images`: ID photos are scaled down to the badge's print size before embedding,
    through `IImageNormalizer`.
- **Frontend**:
  - `features/badges` (the badge sheet, the download, i18n in three locales);
  - row selection in `components/app/DataTable`;
  - the registry list and comparsa detail actions;
  - the generated client.
- **Docs**: `docs/data-model.md` §4 (decisions, blank-field rules), `docs/use-cases.md` (UC-30
  notes), `docs/open-questions.md` (Q-49 interim decision), `docs/compliance.md` (badge documents),
  `docs/design/patterns.md` (row selection, badge sheet), the modules README.
- **Security and GDPR**: badges carry name, DNI/NIE and photo. They are generated per request and
  never stored, Admin only, audited, rate limited and capped in size, with `no-store` and file names
  without personal data.
