# Use Cases

> Status: **v1.0** (after round 5).
> Actors: `FC` = FiringChief, `AD` = Admin (Federation), `SYS` = system.
> Scope: `MVP` = first release, used by the Federation and all comparsas · `L` = later · `OUT` = out of scope.
>
> Decision (round 3): there is **no separate single-comparsa pilot**. The MVP includes the full
> Federation workflow; the FiringChief of Contrabandistas acts as first tester. No hard deadline.

## Festival-year timeline

```
 Oct ─────── Nov–Dec ─────── Jan–Feb ─────────────────── Mar–Apr ─────────── Apr ─────── After
 License      Course,         Orders: open window         Powder              FESTIVAL    Close
 renewals     sign-up and     → corrections window        distribution                    edition
              license         → lock                      (flask assignment)
              deadlines
 UC-01..09    UC-10, UC-23    UC-11..17, UC-28            UC-18..21
```

## A. Registry (not edition-scoped)

| ID | Use case | Actor | Scope |
|---|---|---|---|
| UC-01 | Register an arquebusier (federationId, DNI/NIE validation, contact, gender, ID photo) | FC | MVP |
| UC-02 | Update an arquebusier / renew license (type, dates, front & back photos) | FC | MVP |
| UC-03 | Record training course done + date | FC | MVP |
| UC-04 | Register an owned weapon (model, weapon number, ownership guide) | FC | MVP |
| UC-05 | Set an arquebusier as Active or Reserve; delete when they leave the Federation | FC | MVP |
| UC-06 | Alerts dashboard: license expired / expiring / pending, no course, under age | FC, AD | MVP |
| UC-07 | Statistics and equality report (age brackets, gender, course, owned weapons, first year) | FC, AD | MVP |
| UC-08 | Internal comparsa notes | FC | L |
| UC-09 | Bulk import arquebusiers from a spreadsheet (initial load): the PolvorApp template, one comparsa per file, checked first and imported all or nothing, new arquebusiers only (`add-registry-import`) | AD | MVP |
| UC-29 | Transfer an arquebusier to another comparsa (between editions) | AD | MVP |
| UC-30 | Generate printable arquebusier badges (PDF, one or many), replacing today's manual design | AD | MVP |

## B. Edition setup (Federation)

| ID | Use case | Actor | Scope |
|---|---|---|---|
| UC-10 | Create and configure an edition: festival dates, planned order window, available rental models, prices, calendar milestones | AD | MVP |
| UC-11 | Move the edition through its lifecycle (draft → in progress → closed) and open / close its orders; lock / unlock the registry | AD | MVP |

## C. Yearly order (replaces the per-arquebusier Google Form)

| ID | Use case | Actor | Scope |
|---|---|---|---|
| UC-12 | Prepare the comparsa order: one entry per arquebusier (active/reserve, kg, caps, weapon source, flask), **pre-filled from last edition** | FC | MVP |
| UC-13 | Register a weapon loan (owned weapon → borrower, possibly from another comparsa) | FC | MVP |
| UC-14 | Submit the order with attestation, showing pending warnings; edit again while the orders are open (reopened as a correction window) | FC | MVP |
| UC-15 | Review orders: validate or return with comments; edit any order even when locked (exceptional cases) | AD | MVP |
| UC-16 | Federation dashboard: order status per comparsa, totals (kg, caps, rentals by model) | AD | MVP |
| UC-17 | Exports: powder supplier, weapon rental company, Arms Authority, per-comparsa lists (Excel / PDF) | AD | MVP |
| UC-28 | Comparsa billing summary: amount owed to the Federation (powder + caps + rentals) | FC, AD | MVP |

## D. Distribution

| ID | Use case | Actor | Scope |
|---|---|---|---|
| UC-18 | Schedule distribution days and slots per comparsa | AD | MVP |
| UC-19 | **Exceptional:** register a pickup proxy for an arquebusier who cannot attend, and print the pre-filled authorisation form to sign on paper | FC | MVP |
| UC-20 | Generate printable distribution lists with global numbering (incl. proxies) | AD | MVP |
| UC-21 | Record on site, **offline**, the rental flask number assigned to each arquebusier (and other handover data); sync later | AD | L |
| UC-22 | Return of rented weapons and flasks | — | OUT (handled by the rental company) |

## E. Cross-cutting

| ID | Use case | Actor | Scope |
|---|---|---|---|
| UC-23 | Email notifications: license expiring, upcoming windows/deadlines, order returned/validated | SYS | MVP |
| UC-24 | Manage users, comparsas, FiringChief assignments, weapon catalogue | AD | MVP |
| UC-25 | Audit log (who changed what) | AD | MVP |
| UC-26 | GDPR requests: export or erase a person's data | AD | MVP |
| UC-27 | Switch UI language (es / ca-valencia / en) | FC, AD | MVP |
| — | Shooting contest | — | OUT (maybe later) |
| — | Course sessions and enrolment | — | OUT (only done + date) |
| — | Payments / arquebusier fees | — | OUT (only the billing summary, UC-28) |

## Notes

- **UC-10 (create and configure an edition)**: new editions start with the prices and rentable weapon models of the previous edition (if any), all editable. Festival dates and milestone dates are independent of the calendar; window dates (when orders open and close) are a published plan only — they do not automatically open or close orders.
- **UC-11 (edition lifecycle and orders)**: the edition moves from `DRAFT` (in preparation) to `IN_PROGRESS` (current, one at a time) to `CLOSED`, by hand, one step at a time. Admins also toggle the orders open and closed (the `ordersOpen` flag), as often as needed; reopening after a review is the corrections window. The registry lock (independent of the edition) is also toggled by Admins.
- **UC-12 (prepare the comparsa order)**, change `add-comparsa-orders`:
  - A FiringChief prepares the order while the orders are open; an Admin at any time once the
    edition has started. Preparing creates one entry per arquebusier of the comparsa, `ACTIVE` and
    `RESERVE` alike.
  - Each entry is pre-filled from the arquebusier's latest earlier entry: powder, caps, flask and
    the weapon source. An owned weapon is kept only while still owned, and a rental model only while
    still offered. Loans are never copied, and nothing is carried over (BR-11).
  - Arquebusiers registered or transferred in later are listed as "not in the order" and added by
    hand. Nothing changes an order by itself.
  - The entry status starts as the registry status and is independent afterwards. An `ACTIVE`
    entry may have no powder (a shooter, such as a comparsa captain) or no weapon (a powder carrier).
  - Entries are never removed by hand. Deleting an arquebusier while the orders are open removes
    their entry of the edition in progress after a warning; every other entry stays as history.
- **UC-13 (weapon loan)**, change `add-comparsa-orders`: the borrower's FiringChief sets the entry's
  weapon source to "loan" and types the owner's DNI/NIE. A registered owner, of any comparsa, is
  found by an exact match and one of their owned weapons is chosen; someone outside PolvorApp is
  typed in (name, surnames, DNI/NIE, weapon model, number and ownership guide). There is no limit,
  and one weapon may be lent to several arquebusiers. The lender's FiringChiefs see the loans of
  their arquebusiers' weapons with the borrower's name and comparsa only.
- **UC-14 (submit the order)**, change `add-comparsa-orders`:
  - The FiringChief submits a `DRAFT` or `RETURNED` order with the attestation, seeing the
    `ACTIVE` entries with compliance warnings; warnings never block. Entries that are inconsistent
    with the registry or the edition (a weapon no longer owned, a model no longer offered) do.
  - Editing a `SUBMITTED` order while the orders are open sends it back to `DRAFT`, to be
    submitted again. A `VALIDATED` order is read-only for FiringChiefs; so is every order once the
    orders are closed.
- **UC-15 (review orders)**, change `add-comparsa-orders`: an Admin validates a `SUBMITTED` order,
  or one never submitted (`DRAFT` or `RETURNED`, to close the open ones), and returns a `SUBMITTED`
  or `VALIDATED` order with a reason the FiringChief reads. An Admin may also submit on the
  comparsa's behalf, without the attestation (recorded as an Admin submission), and edits any order
  at any time without changing its status.
- **UC-16 (orders dashboard)**, change `add-comparsa-orders`: for an edition (the current one by
  default), each comparsa with its order status, or "not prepared", and its totals: `ACTIVE` and
  `RESERVE` entries, powder, caps by type, weapon rentals by model, flask rentals by size, loans,
  owned weapons and, for the edition in progress, `ACTIVE` entries with warnings. Admins see every
  active comparsa, the counts by status and the edition totals; FiringChiefs see their comparsas
  only. Totals are computed, never stored.
- **UC-28 (billing summary)**, change `add-billing-summary` (maintainer decisions): what each
  comparsa owes the Federation for its order, by concept (powder, caps, weapon rentals, flask
  rentals) at the edition's flat prices, not per arquebusier — what each arquebusier pays the
  comparsa stays comparsa-internal. The amounts are always computed from the current entries and
  prices, never frozen; they are provisional until the order is validated. The summary is shown on
  the order page and in the orders overview (each comparsa's amount, and for Admins the edition
  billing). There is no download: exports come with UC-17.
- **UC-17 (exports)**, change `add-exports` (maintainer decisions): Admins download the powder
  supplier, rental company and Arms Authority files of an edition, as Excel and PDF, from the
  `VALIDATED` orders only; the exports page warns which comparsas are not validated yet, without
  blocking. Each comparsa's list is available to Admins and its FiringChiefs for an order in any
  status, as a draft until the order is validated. The layouts are provisional until the
  recipients' templates arrive (Q-44). Sending the files is up to the Federation.
- **UC-18 to UC-20 (distribution)**, change `add-distribution-planning` (maintainer decisions): one
  powder day and one weapons day per edition, planned by Admins with a slot per comparsa; the lists
  come from the `VALIDATED` orders, as Excel and PDF, in the user's language, numbered on every print
  (comparsas by slot, people by name), with columns filled in by hand on the day. Pickup proxies are
  registered by the comparsa's FiringChiefs while the edition is in progress, even with the orders
  closed; the proxy must hold an active weapons license on the day (blocking); no reason is stored;
  the authorisation form is printed in the user's language with the Federation's logo once uploaded.
- **UC-30 (badges)**, change `add-badges` (maintainer decisions): Admins print badges as an A4 PDF
  of 10 ID-1 cards with crop marks, either for a whole comparsa (`ACTIVE` and `RESERVE`) or for a
  selection ticked in the registry list, at most 200 per PDF. The labels' language is chosen for
  each download (Q-49 open). A missing photo or issued license prints an empty frame or line, with
  a warning before the download, never a block. Nothing is stored; each download is audited.
- **UC-23 (email notifications)**, change `add-notifications` (maintainer decisions): emails go only to
  `ACTIVE` users, never to arquebusiers. A monthly license digest (first day of the month; counts per
  comparsa of `ACTIVE` arquebusiers with a missing, pending, expired or expiring-within-90-days
  license) goes to FiringChiefs. Opening and closing the orders, and reminders 7 days and 1 day before
  the planned close date for orders not yet submitted, go to FiringChiefs. An order returned or
  validated is told to its comparsa's FiringChiefs (the return reason is read in the app, never
  emailed); a FiringChief's submission is told to the Admins. Milestones marked `notify` are reminded
  7 days ahead to Admins, and to FiringChiefs once the edition is in progress. Each user turns each
  kind of their role on or off on the account page; scheduled emails go out from 08:00 Europe/Madrid.
- **UC-25 (audit log)**, change `add-audit-privacy`: Admins read every audit entry, newest first
  and a page at a time, filtered by period (the last 30 days by default), user, comparsa, area and
  action; each entry opens with its recorded data and trace id. Arquebusier and user pages link to
  their history. Entries cannot be changed or deleted; security events are kept 1 year and the rest
  5 years (maintainer decision, 2026-10-04).
- **UC-26 (GDPR requests)**, change `add-audit-privacy` (maintainer decisions, 2026-10-04):
  - The subjects are arquebusiers, external weapon owners (by DNI/NIE) and users (from their page).
  - Every request carries a reference to the written request, never the person's name or DNI/NIE,
    and is audited; the DNI/NIE never appears in addresses, logs, file names or audit data.
  - The export is a ZIP with a spreadsheet of everything held and the person's photos, built on the
    fly and never stored.
  - An erasure is never blocked, only warned about: the registry record goes as in a deletion
    (BR-14), past entries and loans are anonymised but keep counting, proxies go, and audit entries
    lose the person's names and DNI/NIE. An erased user keeps a row named "Erased user".
- On distribution day the **FiringChief validates the identity** of their arquebusiers; nobody signs.
  The only paper document is the proxy authorisation (UC-19), for the exceptional case.
- Today the Federation records flask assignments with a laptop and a spreadsheet; UC-20 printable
  lists cover the MVP, UC-21 adds the offline capture later.
