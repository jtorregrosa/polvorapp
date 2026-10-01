# MVP Plan

> Status: **accepted v1.0** (round 5).
> Bridge between discovery docs and OpenSpec: each **capability** becomes a spec
> (`openspec/specs/<capability>/spec.md`) and each **change** an OpenSpec change
> (`openspec/changes/<change-id>/`). Change order = implementation order.

## MVP definition

The MVP lets the Federation and all comparsas run a complete festival edition in PolvorApp —
from arquebusier registry to validated orders, billing summary, exports and distribution lists —
**without spreadsheets or Google Forms**. Offline on-site capture (UC-21) comes after the MVP.

## Capabilities

| Capability | Covers | Use cases | Rules |
|---|---|---|---|
| `platform` | Repo skeleton, containers, CI, i18n scaffolding, UI shell | UC-27 | NFR-* |
| `design-system` | Tokens, `components/app/` layer, design guide, component catalogue | — | ADR-0009, NFR-07 |
| `identity-access` | Users, roles, invitations, 2FA, comparsa scoping | UC-24 (users) | BR-12, SEC-03/04 |
| `federation-catalog` | Comparsas, sides, logos, FiringChief assignments, weapon models | UC-24 (catalog) | BR-07 |
| `arquebusier-registry` | Arquebusiers, status Active/Reserve, license, course, photos, owned weapons, transfers, deletion, import | UC-01..05, UC-09, UC-29 | BR-01..03, BR-13, BR-14 |
| `compliance-insights` | Warnings, alerts dashboard, statistics, equality report | UC-06, UC-07 | BR-04 |
| `festival-editions` | Editions, windows, prices, rental availability, milestones, locking | UC-10, UC-11 | BR-10 |
| `comparsa-orders` | Entries, pre-fill, loans, submit/attest, review, dashboard | UC-12..16 | BR-05, BR-08, BR-09, BR-11 |
| `billing` | Billing summary per comparsa | UC-28 | — |
| `exports` | Export definitions for supplier, rental company, Arms Authority, comparsa | UC-17 | SEC-06 |
| `distribution` | Days, slots, exceptional proxies + PDF, printable lists with numbering | UC-18..20 | BR-06 |
| `notifications` | Scheduled and event-driven emails | UC-23 | — |
| `audit-privacy` | Audit log, GDPR export/erasure | UC-25, UC-26 | SEC-05, SEC-09 |
| `badges` | Printable arquebusier badges | UC-30 | NFR-15 |

## Change sequence

| # | Change id | Capability | Outcome |
|---|---|---|---|
| 1 | `bootstrap-platform` | platform | Solution skeleton, docker compose, CI, i18n (3 locales), empty UI shell, synthetic seed, MIT `LICENSE` — **done**, archived 2026-09-30 |
| 2 | `add-design-system` | design-system | PolvorApp identity (ADR-0012, ember-orange accent), Tailwind tokens (light/dark), shadcn/ui primitives, `components/app/` composites and app layout, `docs/design/` guide, Storybook + axe, lint guardrails — **done**, archived 2026-09-30 |
| 3 | `add-identity-access` | identity-access | Invitations, login + TOTP 2FA, roles, scoping, minimal audit trail — **done**, archived 2026-09-30 |
| 4 | `add-federation-catalog` | federation-catalog | Comparsas, FiringChief assignments, weapon model catalogue — **done**, archived 2026-09-30 |
| 5 | `add-arquebusier-registry` | arquebusier-registry | CRUD, DNI/NIE validation, Active/Reserve, owned weapons — **done**, archived 2026-10-01 |
| 6 | `add-arquebusier-photos` | arquebusier-registry | ID + license photos, crop, EXIF strip, private storage |
| 6b | `add-comparsa-logos` | federation-catalog | Comparsa logo (Admin uploads; reuses the #6 image pipeline and private storage): shown in the comparsa list and detail, in the FiringChief's header, and available to later PDFs |
| 7 | `add-compliance-insights` | compliance-insights | Warnings, alerts dashboard, statistics |
| 8 | `add-registry-import` | arquebusier-registry | Spreadsheet import with validation report |
| 9 | `add-festival-editions` | festival-editions | Editions, windows, prices, availability, locking |
| 10 | `add-comparsa-orders` | comparsa-orders | Entries with pre-fill, loans, submit/attest, review/return |
| 11 | `add-billing-summary` | billing | Amount owed per comparsa |
| 12 | `add-exports` | exports | Export definitions (placeholders until templates arrive, Q-44) |
| 13 | `add-distribution-planning` | distribution | Days, slots, proxies + PDF, printable lists |
| 14 | `add-notifications` | notifications | License expiry, window reminders, order status emails |
| 15 | `add-audit-privacy` | audit-privacy | Audit log viewer, GDPR export/erasure |
| 16 | `add-badges` | badges | Badge PDF generation (layout in `data-model.md`) |

No feature screen is built before #2. Audit logging hooks are built into each change from #3
onwards; #15 adds the viewer and GDPR tooling.

## After the MVP

- `add-offline-distribution-capture` (UC-21): PWA offline capture of flask numbers and handovers.
- Shooting contest, comparsa notes (UC-08), any change requested by the Federation after the first edition.

## Blockers to resolve before go-live (not before starting)

- Export templates from the Federation (Q-44) and traceability meaning (Q-43).
- Federation buy-in, hosting capable of running containers, DPO and updated privacy notice.
