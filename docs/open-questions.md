# Open Questions

Tracker of discovery questions. Answers are integrated into the relevant doc; the source is
`sources/answers.md`.

## Round 2 — Domain and data

| ID | Topic | Answer (summary) | Status |
|---|---|---|---|
| Q-01 | Strategy | Federation not yet aware. Pilot by the Contrabandistas FiringChief; MVP designed for all 20 comparsas. | ✅ |
| Q-02 | Hosting / GDPR | The Federation hosts, pays and is data controller. | ✅ |
| Q-03 | License types | AE = muzzle-loading; A-PROF = professional (police…). | ✅ (A-PROF validity → Q-27) |
| Q-04 | Photos | Store license front/back and ID photo. | ✅ |
| Q-05 | External app | PolvorApp is authoritative for arquebusier data; keep `federationId` reference. | ✅ |
| Q-06 | Roles | Several FiringChiefs per comparsa; Federation = Admin only. | ✅ |
| Q-07 | Weapon catalogue | Side × handedness × size; Christian = trabuco, Moorish = arcabuz; pistols never rented; availability varies per year. | ✅ |
| Q-08 | Weapon number | Engraved on stock; rental numbers stamped by provider; one unit per person, non-transferable; returned and checked by provider. | ✅ |
| Q-09 | Loans | Can cross comparsas; no limit. | ✅ |
| Q-10 | Powder unit | Per arquebusier per year. | ✅ |
| Q-11 | Carryover / payment | No carryover (destroyed). Arquebusiers pay the comparsa; comparsa transfers to Federation. | ✅ (in scope? → Q-31) |
| Q-12 | Traceability | Unknown. | ⏳ ask the Federation |
| Q-13 | Caps | Ordered through the Federation. | ✅ |
| Q-14 | Internal fields | Reserve = 0 kg, can be proxy. Salvas al Patrón = minor act. Lunch out of scope. | ✅ (Recarga → Q-28) |
| Q-15 | Locking | Admins lock each edition and the registry. | ✅ |
| Q-16 | Export templates | Not available yet. | ⏳ later |
| Q-17 | Distribution | Desired; poor or no connectivity on site. | ✅ |
| Q-18 | Age check | FiringChief is accountable → warnings, not blocks. | ✅ |
| Q-19 | Courses | Done + date only. | ✅ |
| Q-20 | Contest | Out of scope for now. | ✅ |
| Q-21 | Notifications | Yes, email. | ✅ |
| Q-22 | Timeline | 2027 or 2028. | ✅ |

## Round 3 — Use cases and remaining details

| ID | Topic | Answer (summary) | Status |
|---|---|---|---|
| Q-23 | Phasing | Agreed, but **P1 and P2 merged into one MVP**; no urgency. | ✅ |
| Q-24 | Google Form | One submission **per arquebusier**, same fields as the order spreadsheet. PolvorApp replaces it. | ✅ |
| Q-25 | Order workflow | Federation opens a corrections window; exceptional cases handled in person (Admin edits). | ✅ |
| Q-26 | Distribution | Federation records rental flask number per person (laptop + spreadsheet). No signatures; FiringChief validates identities. | ✅ |
| Q-27 | License | A-PROF renewed yearly (to confirm). No number, no history. | ✅ |
| Q-28 | Recarga | Not needed. | ✅ |
| Q-29 | Transfers | Yes, between editions. | ✅ |
| Q-30 | federationId | Always exists at registration. | ✅ |
| Q-31 | Money | Billing summary wanted. | ✅ (prices → Q-35) |
| Q-32 | Gender | Keep, for equality reports. | ✅ |
| Q-33 | Proxy form | Paper, and only for people who cannot attend. | ✅ |
| Q-34 | Rental flasks | Numbered, assigned per person, returned; weapon/flask return handled by the rental company. | ✅ |

## Round 4 — Compliance, non-functional and technical

| ID | Topic | Answer (summary) | Status |
|---|---|---|---|
| Q-35 | Prices | Flat prices for powder/kg, caps box, weapon rental, flask rental. | ✅ |
| Q-36 | GDPR | DPO unknown; a data form is signed when joining the Federation. | ✅ (review notice → Federation) |
| Q-37 | Retention | Stop firing ⇒ kept as RESERVE (0 kg) for some years; leave the Federation ⇒ deleted. Single status ACTIVE / RESERVE. | ✅ |
| Q-38 | Auth | Email + password with 2FA. | ✅ |
| Q-39 | Skills | .NET, Angular, React, Node. Maintained by the author. | ✅ |
| Q-40 | Hosting | Federation probably has hosting (external app). Start with Docker locally and container deployment; adapt later. | ✅ |
| Q-41 | Repository | Public. | ✅ |
| Q-42 | Photos | Used to print arquebusier badges worn during the acts. | ✅ (→ Q-45) |
| Q-43 | Traceability | Pending — ask the Federation. | ⏳ |
| Q-44 | Templates | Pending — export templates for supplier, rental company, Arms Authority. | ⏳ |

## Round 5 — Architecture decisions and MVP plan

| ID | Topic | Answer (summary) | Status |
|---|---|---|---|
| Q-45 | Badges | Printed by the Federation, designed manually. PolvorApp generates them (layout in `data-model.md`). | ✅ |
| Q-46 | ADRs | Accepted, with React + Tailwind + shadcn/ui instead of Angular (ADR-0009, ADR-0011). | ✅ |
| Q-47 | License | MIT (ADR-0010). | ✅ |
| Q-48 | MVP plan | Accepted; `add-design-system` inserted as change #2. | ✅ |

## Pending with the Federation (do not block development)

| ID | Topic | Needed before |
|---|---|---|
| Q-43 | Meaning of traceability 1 / 2 at powder handover | `add-offline-distribution-capture` |
| Q-44 | Export templates: powder supplier, rental company, Arms Authority | `add-exports` go-live |
| Q-49 | Badge: labels only in Spanish? (size answered: ID-1 credit card on cardstock, plastic sleeve) | `add-badges` |
| Q-50 | DPO, updated privacy notice, hosting able to run containers | Go-live |
| Q-51 | Federation buy-in (project presentation / demo) | Go-live |
| Q-53 | Official weapon catalogue labels and whether kind must follow the side (Q-07 says Christian = trabuco, but Federation lists show "ARCABUZ CRISTIANO"); PolvorApp allows any combination meanwhile | Go-live |

## Pending for later changes (maintainer)

| ID | Topic | Needed before |
|---|---|---|
| Q-52 | Does a FiringChief's `User` need a link to their own `Arquebusier` record (a FiringChief may also fire)? Today they are separate records | `add-arquebusier-registry` |
