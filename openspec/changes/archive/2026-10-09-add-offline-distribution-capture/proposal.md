# Proposal

## Why

The powder is handed out far from town, alongside the Guardia Civil, where coverage is poor or absent
(Q-17, glossary *Distribution*). Today the Federation writes on a laptop spreadsheet the rented
flask each person takes (Q-26), plus two traceability codes whose meaning is still unknown
(Q-12/Q-43). The printed powder list (UC-20) leaves those columns empty, so the record of who took
which flask lives outside PolvorApp.

UC-21 brings that capture into the app, offline, and syncs it later. NFR-03 and ADR-0011 already
reserve the approach: a PWA with IndexedDB. The maintainer chose this change as the first one after
the MVP (2026-10-09). It also decided that this change:
- covers the **powder day only**;
- lets **several devices** capture at the same time;
- stores the day's list on the device, **bounded** in time.

Capability (from `docs/mvp.md`, "After the MVP"): **`distribution`**. It also touches **`platform`**
(the installable application) and **`audit-privacy`** (GDPR export). It implements UC-21 and stores
the *Handover* of `docs/data-model.md`. It keeps BR-05 (powder kg), BR-06 and the proxies'
"Proxies that no longer hold", BR-12 (Admins only, here) and BR-14 (erased entries). It follows
ADR-0004 (cookie session, anti-forgery), ADR-0011 (PWA, IndexedDB), NFR-03, NFR-01 and NFR-07, and
SEC-05, SEC-06, SEC-08 and SEC-09. It adds a new measure, **SEC-14**, for personal data kept on a
device.

## What Changes

- **Prepare a device for the day**: on the powder day's card, an Admin downloads the powder list
  for offline use. The download is audited and sent with `no-store`. The device keeps it in
  IndexedDB together with the handovers already recorded.
  - The list holds the same rows and columns as the printed powder list (UC-20) and the same
    global number.
  - It keeps the minimum identity data needed to check a person on the day: name and DNI/NIE.
- **Powder handover capture, online or offline**: a capture screen at 360 px shows the day's
  holders by slot and comparsa, with search by number, name or DNI/NIE. Recording a handover
  stores on the device:
  - who collected: the holder or their valid powder proxy;
  - the rented flask number, required when the entry rents a flask;
  - traceability 1 and 2, optional free text until Q-43 is answered;
  - the time, the number and the kilograms from the list.

  A handover not yet synced can be corrected or undone on the device.
- **Sync**: as soon as the device is online with a valid session, pending handovers are sent in
  batches. Each one has an id generated on the device, so sending it twice records it once. Each
  handover is accepted or refused on its own, with a reason:
  - already recorded by another device;
  - flask number already given in that day;
  - not in the list any more;
  - edition not in progress.

  Refused handovers stay on the device as conflicts for an Admin to fix or discard. A session that
  expires while offline loses nothing: the queue survives signing in again.
- **Several devices**: each device downloads the list and captures on its own. Two rules make
  conflicts visible, never silent, and are **blocking** data-integrity rules: one handover per
  entry and day, and a rented flask number given once per day.
- **Handovers online**: the distribution page shows "N of M delivered" for the powder day. The
  capture screen, while online, lets an Admin undo a synced handover. The undo is confirmed and
  audited. A powder day with handovers can no longer be deleted.
- **Lists show what was captured**: the downloaded powder list (Excel and PDF) fills in the flask
  number and traceability columns from the recorded handovers, and marks who collected. Its
  numbering stays derived. The handover keeps a copy of the number shown when it was captured.
- **Data on the device (new SEC-14)**:
  - the list and handovers belong to the Admin who downloaded them;
  - they are cleared when that Admin signs out (refused while handovers are unsynced, unless the
    Admin confirms the loss) or when another user signs in;
  - the list is cleared once the day's handovers are synced and the Admin closes the capture, and
    in any case 7 days after the download;
  - the service worker still never caches `/api` responses.
- **App shell offline**: the installed app opens the capture screen without connectivity from its
  precache. Every other screen keeps needing the network.
- **GDPR**: a person's data export lists their powder handovers (year, kilograms, flask number,
  traceability, holder or proxy). Handovers hold no identity, so erasure keeps them as anonymised
  history linked to the anonymised entry. The new table joins the personal-data contract test.

## Non-goals

- **The weapons day.** It needs the `RentalWeapon` entity and the weapon numbers, which are left
  for a later change. The weapons list is unchanged.
- **FiringChief access to capture or to handovers.** UC-21 is an Admin task; FiringChiefs keep
  their current distribution page.
- **The meaning and validation of traceability 1/2 (Q-43).** They stay optional free text, at most
  50 characters, until the Federation answers.
- **Signatures and identity checks by the app.** The FiringChief validates identities on site
  (Q-26).
- **Flask returns to the rental company.** Out of scope (glossary *WeaponReturn*).
- **Offline use of any other screen**, background sync from the service worker, and push
  notifications.
- **Encryption of the on-device data beyond the browser's own storage.** Mitigated by retention,
  clearing and the device's own lock (see `design.md`).

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `distribution`:
  - new requirements: offline capture package, powder handovers, handover sync and conflicts,
    handover screens, data kept on the device, handovers audited;
  - modified requirements: Distribution lists (filled from handovers), Global numbering (copy kept
    at handover), Distribution screens (capture entry point, delivered count).
- `platform`: *Installable application* — the app shell opens the capture screen offline. `/api`
  responses are still never cached.
- `audit-privacy`: *Exporting a person's data (UC-26)* — adds the powder handovers sheet.
- `design-system`: *Status semantics* — the handover states on a capture device (to deliver,
  pending, synced, conflict).

## Impact

- **Backend, `Distribution` module**:
  - new `Handover` entity and table `distribution.handovers`, with a migration, cross-schema
    foreign keys to `orders.edition_entries`, and unique indexes per (day, entry) and per (day,
    flask number);
  - endpoints for the capture package, batched sync, the handover list and undo;
  - audit actions and a rate limit;
  - list documents filled from handovers;
  - `DistributionPersonalData` exports the handovers;
  - seed handovers for the past edition.
- **Frontend, `features/distribution`**:
  - capture route and screen, on-device store (IndexedDB through a small wrapper, checked with
    Context7), sync engine, connectivity and pending status, conflicts view;
  - delivered count and undo on the distribution page;
  - sign-out guard;
  - translations in es-ES, ca-ES-valencia and en.
- **PWA**: precache and navigation fallback so the capture route opens offline. No API runtime
  caching.
- **Docs**:
  - `docs/compliance.md` (SEC-14), `docs/data-model.md` (Handover implemented),
    `docs/glossary.md`, `docs/use-cases.md` (UC-21);
  - `docs/mvp.md` row, `docs/open-questions.md` (Q-43 still open, now non-blocking);
  - design guide for the capture screen.
- **Dependencies**: possibly `idb` (about 1 KB, IndexedDB promises). It does not overlap any fixed
  library, so no ADR is needed, but it is confirmed in task 1.
