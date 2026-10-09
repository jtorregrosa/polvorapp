# Open Questions

Tracker of discovery questions. Answers are integrated into the relevant doc; the source is
`sources/answers.md`, except for maintainer decisions taken later, which are recorded here with
their date.

## Round 2 — Domain and data

| ID | Topic | Answer (summary) | Status |
|---|---|---|---|
| Q-01 | Strategy | Federation not yet aware. Pilot by the Contrabandistas FiringChief; MVP designed for all 20 comparsas. | ✅ |
| Q-02 | Hosting / GDPR | The Federation hosts, pays and is data controller. | ✅ |
| Q-03 | License types | AE = muzzle-loading; A-PROF = professional (police…). | ✅ (A-PROF validity → Q-27) |
| Q-04 | Photos | Store license front/back and ID photo. | ✅ |
| Q-05 | External app | PolvorApp is authoritative for arquebusier data; keep `federationId` reference. | ✅ |
| Q-06 | Roles | Several FiringChiefs per comparsa; Federation = Admin only. | ✅ |
| Q-07 | Weapon catalogue | Side × handedness × size; Christian = trabuco, Moorish = arcabuz; availability varies per year. "Pistols never rented" was dropped: any kind may be rentable, as the Admin sets it per model (maintainer decision, 2026-10-05; `allow-rentable-pistols`). | ✅ |
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
| Q-25 | Order workflow | Federation opens a corrections window; exceptional cases handled in person (Admin edits). **Note**: a corrections window is simply the orders reopened on the `currentEdition` (no separate stage). | ✅ |
| Q-26 | Distribution | Federation records rental flask number per person (laptop + spreadsheet). No signatures; FiringChief validates identities. | ✅ |
| Q-27 | License | A-PROF renewed yearly (confirmed by the maintainer, 2026-10-01). No number, no history. | ✅ |
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
| Q-43 | Meaning of traceability 1 / 2 at powder handover (captured meanwhile as optional free-text codes by `add-offline-distribution-capture`) | Go-live |
| Q-44 | Export templates: powder supplier, rental company, Arms Authority | `add-exports` go-live |
| Q-49 | Badge: labels only in Spanish? (size answered: ID-1 credit card on cardstock, plastic sleeve). Meanwhile the Admin chooses es-ES, ca-ES-valencia or en for each download (maintainer decision, 2026-10-04) | Go-live |
| Q-50 | DPO, updated privacy notice, hosting able to run containers | Go-live |
| Q-51 | Federation buy-in (project presentation / demo) | Go-live |
| Q-53 | Official weapon catalogue labels and whether kind must follow the side (Q-07 says Christian = trabuco, but Federation lists show "ARCABUZ CRISTIANO"); PolvorApp allows any combination meanwhile | Go-live |

## Maintainer decisions

| ID | Topic | Answer (summary) | Status |
|---|---|---|---|
| Q-52 | Link between a FiringChief's `User` and their own `Arquebusier` record (a FiringChief may also fire) | No link: two separate records. A FiringChief who fires is registered like any other arquebusier of their comparsa (`add-arquebusier-registry`). | ✅ |
| Q-54 | Under-18 arquebusiers | Arquebusiers must be of legal age (18); there is no special authorisation for minors. PolvorApp shows the `UNDER_AGE` warning; like every BR-04 check it never blocks (confirmed by the maintainer, 2026-10-02; `add-compliance-insights`). | ✅ |
| Q-55 | Distribution numbering (UC-20) | Derived on every print: comparsas by slot time (those without one last), people by name; nothing stored, a reprint after changes renumbers (maintainer decision, 2026-10-03; `add-distribution-planning`). | ✅ |
| Q-56 | Distribution days per edition | One powder day and one weapons day (maintainer decision, 2026-10-03). | ✅ |
| Q-57 | Who manages pickup proxies, and when | The comparsa's FiringChiefs while the edition is in progress, even with the orders closed or the order validated; Admins in any edition that is not a draft (maintainer decision, 2026-10-03). | ✅ |
| Q-58 | Reason of a pickup proxy | Not stored (GDPR, it may reveal health data): the form leaves it blank, written by hand (maintainer decision, 2026-10-03). | ✅ |
| Q-59 | Language of distribution lists and forms | The user's language (es-ES, ca-ES-valencia, en) (maintainer decision, 2026-10-03). | ✅ |
| Q-60 | Proxy's license | A proxy must hold an active weapons license on the day: **blocking**, a deliberate exception to compliance checks being warnings (maintainer decision, 2026-10-03). | ✅ |
| Q-62 | Recipients of the license reminders | FiringChiefs only, by comparsa; arquebusiers are never emailed (their address is for their FiringChief) (maintainer decision, 2026-10-04; `add-notifications`). | ✅ |
| Q-63 | License reminder cadence | A monthly digest on the first day of the month, counting licenses missing, pending, expired or expiring within 90 days; none when there is nothing to report (maintainer decision, 2026-10-04). | ✅ |
| Q-64 | Managing notifications (answer 21: "poder gestionarlas") | Each user turns each kind of their role on or off; all on by default; Admins mark which milestones are reminded (maintainer decision, 2026-10-04). | ✅ |
| Q-65 | Notification kinds | Orders opened and closed, planned close reminders (7 and 1 day before), order returned or validated (FiringChiefs), order submitted (Admins), milestone reminders 7 days before (maintainer decision, 2026-10-04). | ✅ |
| Q-61 | Federation logo on documents | Uploaded at run time by an Admin into private storage and printed on the form (and later the badges); never committed, as the repository is public (maintainer decision, 2026-10-03). | ✅ |
| Q-66 | When a GDPR erasure is allowed | Always: an open edition never blocks it; the confirmation warns about the editions whose lists will no longer name the person and the entry removed while orders are open (maintainer decision, 2026-10-04; `add-audit-privacy`). | ✅ |
| Q-67 | Whose GDPR requests PolvorApp handles | Arquebusiers and external weapon owners (by DNI/NIE), and users (Admins and FiringChiefs, from their page) (maintainer decision, 2026-10-04). | ✅ |
| Q-68 | Format of a GDPR export | A ZIP with one spreadsheet of everything held and the person's photos (maintainer decision, 2026-10-04). | ✅ |
| Q-69 | Audit retention | Security events 1 year, everything else 5 years, purged daily (maintainer decision, 2026-10-04). | ✅ |
| Q-70 | Which arquebusiers a badge batch covers | A whole comparsa, `ACTIVE` and `RESERVE`, or a selection ticked in the registry list, at most 200 per PDF (maintainer decision, 2026-10-04; `add-badges`). | ✅ |
| Q-71 | Badges with missing data | Printed with an empty photo frame or expiry line to complete by hand, with a warning before the download; never blocked (BR-04). A photo the registry holds but cannot read blocks the download, naming the arquebusiers (maintainer decision, 2026-10-04). | ✅ |
