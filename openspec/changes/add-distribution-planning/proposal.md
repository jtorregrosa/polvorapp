# Proposal

## Why

Each festival the Federation hands out the rented weapons on one day and the powder (with the
rented flasks) on another, outside town and with the Guardia Civil, in a slot per comparsa
(`docs/current-state.md` §4 and §6). Today it builds both lists by hand from the comparsas'
spreadsheets, numbers the powder holders across comparsas, and collects paper authorisations for
the few arquebusiers who send someone else (`Autorización recogida pólvora`). PolvorApp now holds
every validated order (#10) and already renders documents (#12), so it can plan the days, keep the
proxies with the rules they must meet, and print the lists and the pre-filled forms from the same
data.

Capability (from `docs/mvp.md`): **`distribution`**, change #13 of the sequence. It implements:
- **UC-18**: schedule the distribution days and the slots per comparsa (Admins).
- **UC-19**: register an exceptional pickup proxy and print its pre-filled authorisation form, to
  be signed on paper (FiringChiefs, and Admins).
- **UC-20**: printable distribution lists with global numbering, proxies included (Admins).

It relies on these rules and decisions:
- **BR-06**: a `PickupProxy` must have an entry (`ACTIVE` or `RESERVE`) in the same edition and the
  same comparsa as the holder. Blocking.
- **Proxy license** (maintainer decision): a proxy must hold an active weapons license on the
  distribution day. This is **blocking**, a deliberate exception to compliance checks (BR-04)
  being warnings: it is a condition to collect for someone else, as the paper form states, not a
  warning about one's own entry.
- **BR-12**: FiringChiefs see their own comparsas' slots and proxies only.
- **SEC-05 / SEC-06**: every write and every document is audited; each document holds only what
  the day needs.
- **ADR-0001** (the module owns its rules and documents' contents), **ADR-0008** (server-side
  documents; the existing Excel and PDF pipeline is reused through a contract), ADR-0002, ADR-0007,
  ADR-0009, ADR-0011.

## What Changes

- **Distribution days** (maintainer decision): an edition in progress has at most one `POWDER`
  and one `WEAPONS` `Distribution`, each with a date and a location, planned by Admins.
- **Slots**: each comparsa gets at most one `DistributionSlot` per day, with its start time. The
  planning page shows the comparsas still without a slot. FiringChiefs see the days and their own
  comparsas' slots.
- **Pickup proxies** (UC-19, BR-06): a holder who cannot attend authorises another entry of the
  same comparsa's order in the edition, `ACTIVE` or `RESERVE`, with an active weapons license, for
  the powder or for a rented weapon. One proxy per holder and type; one proxy may collect for
  several holders; the proxy cannot be someone who is absent themselves; the holder must have
  something to collect. A proxy that stops holding later (the holder's entry or the proxy's
  license changed) is flagged, left out of the lists and its form refused until it is removed.
  FiringChiefs register and remove proxies of their comparsas **while the edition is in progress**,
  whether the orders are open or closed (maintainer decision); Admins always.
- **No reason stored** (maintainer decision, GDPR minimisation): the reason may reveal health data,
  so it is not kept; the form leaves it blank, to be written by hand.
- **Authorisation form** (UC-19): a PDF in the user's language (maintainer decision), with the
  Federation's logo, pre-filled with the edition, the holder's and the proxy's names, DNI/NIE,
  license type and comparsa, with blank lines for the reason, the place and date, and both
  signatures.
- **Federation logo** (maintainer decision): uploaded at run time by an Admin from the comparsas
  page, processed like the comparsa logos and kept in private storage — never committed, as the
  repository is public. The form prints without it until it is uploaded; the badges (#16) will
  reuse it.
- **Distribution lists** (UC-20): one list per day, as Excel and PDF, in the user's language
  (maintainer decision), from the
  `VALIDATED` orders only, with a warning naming the comparsas not validated yet:
  - powder: each `ACTIVE` entry with powder — number, slot, comparsa, name, DNI/NIE, kilograms,
    flask (owned or rented size), blank columns for the flask number and the two traceability
    values (Q-43), and the proxy's name and DNI/NIE;
  - weapons: each entry renting a weapon — number, slot, comparsa, name, DNI/NIE, model, a blank
    column for the weapon number, and the proxy.
- **Global numbering** (maintainer decision): derived each time a list is generated — comparsas in
  slot order (those without a slot last), people in Spanish alphabetical order, numbered from 1
  across the whole day. Nothing is stored; a reprint after changes renumbers, and the page says so.
- **Screens**: a "Distribution" entry in the navigation opening the current edition's distribution
  page (days, slots, lists, proxies), also linked from each edition's page.
- **Audit**: every write (days, slots, proxies) and every document download, without personal data.
- **Edition deletion**: a draft edition with distribution days is in use and cannot be deleted.

## Non-goals

- Recording handovers on the day, offline capture, flask and rental weapon numbers, traceability
  values (UC-21, `add-offline-distribution-capture`; Q-43 still open).
- Storing numbers: the `distributionNumber` is stored only by UC-21.
- Several days per type, slot lengths or capacity planning.
- Digital signatures, or storing the proxy's reason.
- Committing the Federation's logo, or using it anywhere but the documents (the app keeps the
  PolvorApp identity).
- A FiringChief's copy of the distribution lists, or lists of non-validated orders.
- Emailing slots or reminders (#14 `add-notifications`).
- Collection of rented flasks and weapons after the acts (the rental company handles it).

## Capabilities

### New Capabilities
- `distribution`: distribution days and slots, pickup proxies and their rules, the authorisation
  form, the distribution lists with global numbering, permissions, audit, screens and synthetic
  data.

### Modified Capabilities
- `festival-editions`: a `DRAFT` edition that has distribution days is in use and cannot be deleted
  (requirement "Edition management by Admins").
- `federation-catalog`: a new requirement, "Federation logo", uploaded by Admins at run time and
  readable by other capabilities for their documents.

## Impact

- **Backend**:
  - A new module, `Modules/Distribution` (`PolvorApp.Distribution` and `.Contracts`) with a
    `distribution` schema: distributions, slots and pickup proxies, their endpoints, the list and
    form contents, and the audit. It vetoes the deletion of an edition (`IEditionUsage`) or a
    comparsa (`ICatalogUsage`) it references.
  - `Exports.Contracts`: a document rendering contract (tables as Excel and PDF, a simple form as
    PDF), implemented by `Exports` with its existing writers and fonts; the exports' own files do
    not change.
  - `ComparsaOrders.Contracts`: a read contract for the entries of an edition (one comparsa's, or
    by id).
  - `FederationCatalog`: the Federation logo (a settings row with its storage key, endpoints, the
    existing logo pipeline, a read contract for documents).
  - Cross-schema keys from proxies to their entries: a proxy goes with its entry when an
    arquebusier's deletion removes it (BR-14).
- **Frontend**: `features/distribution` (page, sheets, proxy table, downloads), the navigation
  entry, the link on the edition page, the "Federation logo" section on the comparsas page, i18n in
  three locales, the generated client.
- **Docs**: `docs/data-model.md` (slot time, proxy type, no reason), `docs/glossary.md`,
  `docs/use-cases.md` (UC-18..20 notes), `docs/compliance.md` (proxies, documents),
  `docs/design/patterns.md`, the modules README.
- **Security and GDPR**: proxies link two people's data; documents are generated per request,
  never stored, audited and rate limited; FiringChief scope enforced on every route.
