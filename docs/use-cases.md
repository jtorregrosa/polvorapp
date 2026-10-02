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
| UC-10 | Create and configure an edition: festival dates, order and correction windows, available rental models, prices, calendar milestones | AD | MVP |
| UC-11 | Move the edition through its windows (open → corrections → locked → closed); lock / unlock the registry | AD | MVP |

## C. Yearly order (replaces the per-arquebusier Google Form)

| ID | Use case | Actor | Scope |
|---|---|---|---|
| UC-12 | Prepare the comparsa order: one entry per arquebusier (active/reserve, kg, caps, weapon source, flask), **pre-filled from last edition** | FC | MVP |
| UC-13 | Register a weapon loan (owned weapon → borrower, possibly from another comparsa) | FC | MVP |
| UC-14 | Submit the order with attestation, showing pending warnings; edit again while the correction window is open | FC | MVP |
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

- On distribution day the **FiringChief validates the identity** of their arquebusiers; nobody signs.
  The only paper document is the proxy authorisation (UC-19), for the exceptional case.
- Today the Federation records flask assignments with a laptop and a spreadsheet; UC-20 printable
  lists cover the MVP, UC-21 adds the offline capture later.
