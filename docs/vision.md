# PolvorApp — Product Vision

> Status: **v1.0** (discovery closed).

## Context

San Vicente del Raspeig celebrates its Moros y Cristianos festival, coordinated by the
**Federación Unión de Comparsas de Moros y Cristianos Ber-Largas** (the *Federation*). About
**20 comparsas** take part, each with **10–60 arquebusiers** (≈ 600–800 in total). Arquebusiers fire
black powder in the Embajadas (Moorish and Christian) and the Dianas, where a shooting contest is held.

Handling powder and weapons is regulated: every arquebusier needs a valid license, a mandatory
training course and must be of legal age; the Federation must report to the Arms Authority
(Intervención de Armas, Guardia Civil) to get the acts authorised.

## Problem

Arquebusier data is spread across:

- an **external Federation app** (official member roster, license dates and photos), accessible only to comparsa secretaries;
- **per-comparsa spreadsheets** with different structures;
- a yearly **Google Form** opened by the Federation to collect orders;
- **global spreadsheets** rebuilt by the Federation for suppliers and authorities;
- **paper forms** for proxy pickups.

Consequences: the same data is typed several times, versions diverge, errors (e.g. malformed IDs,
expired licenses) are found late, and each FiringChief and the Federation spend a lot of time
consolidating and validating.

## Vision statement

> **For** the Federation staff and the FiringChiefs of every comparsa,
> **who** manage arquebusiers, licenses, powder and weapons every festival year,
> **PolvorApp** is a single management portal
> **that** keeps one source of truth and turns the yearly orders into ready-to-send reports,
> **unlike** today's spreadsheets, forms and emails.

## Goals (success in one year)

1. **Single source of truth** for arquebusiers, licenses, courses, orders, rentals and pickups.
2. **No more spreadsheets or forms** exchanged between comparsas and the Federation.
3. **One management portal** for the Federation and FiringChiefs, with role-based access.

## Users

| User | Scope | Main needs |
|---|---|---|
| Admin (Federation) | All comparsas | Configure the edition, validate data, aggregate orders, generate reports for supplier/rental/authority, organise distributions and courses. |
| FiringChief (one or more per comparsa) | Own comparsa | Keep the arquebusier list up to date, see license and course alerts, submit the yearly order, manage loans and proxy pickups. |

Arquebusiers **do not** access the application.

PolvorApp becomes the **authoritative source for arquebusier data** (licenses, photos, course).
The Federation's external app remains the source for everything else; the link between both is the
`federationId`.

External recipients (Arms Authority, PowderSupplier, WeaponRentalCompany) receive **exported documents**, not accounts.

## Scope boundaries (initial)

In scope (to be prioritised in the MVP round):

- Arquebusier registry per comparsa (identity, license with photos, ID photo, course done + date, owned weapons).
- Compliance warnings and alerts (expired or expiring license, missing course, under age) and **email notifications**.
- Yearly edition: powder order (0/1/2 kg), percussion caps, weapon and flask rentals, weapon loans, reserve arquebusiers — **replacing the per-arquebusier Google Form**.
- Billing summary per comparsa (amount owed to the Federation).
- Order workflow and **locking** of the registry and of each edition by Admins.
- Federation aggregation and exports (supplier, rental company, Arms Authority).
- Distribution days: slots per comparsa, printable lists, exceptional proxies; later, offline on-site capture.

Out of scope (for now): arquebusier self-service, payments/fees, course session management,
shooting contest, return of rented material (handled by the rental company), comparsa-internal
activities (lunch), general comparsa management.

## Constraints

- UI in **Spanish, Valencian and English** (i18n from day one).
- Handles personal data (national ID, birth date, license photos) → GDPR/LOPDGDD compliance.
- Code, specs and repository artifacts in **English**.
- Development driven by **OpenSpec** specs and the **ECC** toolkit.

## Rollout strategy and key risk

- **Decision:** a single MVP covering the **full Federation workflow** for all 20 comparsas (no
  separate single-comparsa pilot). The Contrabandistas FiringChief is the first tester, with real
  data (56 arquebusiers in 2026).
- **Timeline:** no hard deadline; quality over speed. Realistic target: festival **2028**.
- **Key risk — adoption:** the Federation is not yet aware of the project, yet it would host and pay
  for it and be the GDPR data controller. The pilot must produce a convincing demo, and hosting,
  cost and data protection must be solved in a way the Federation can accept.
