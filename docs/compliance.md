# Compliance: Regulations and Data Protection

> Status: **v1.0** — pending review by the Federation.
> This is a design guide, **not legal advice**. The Federation (as data controller) should validate
> it, ideally with its data protection advisor.

## 1. Sector regulations (context, to be confirmed with the Federation)

PolvorApp does not grant or check authorisations; it **supports** the Federation in gathering
the information that authorities require. Relevant frameworks (Spain):

- Weapons regulation (Reglamento de Armas, RD 137/1993, or the regulation replacing it): muzzle-loading
  weapons, AE licenses, ownership guides.
- Pyrotechnics and cartridges regulation (RD 989/2015): black powder handling and distribution.
- Authorisation of the festival firing acts by the Arms Authority (Intervención de Armas, Guardia Civil),
  based on the lists the Federation submits.

Implications for the product:
- Exports for the Arms Authority must match the **format required by the authority** (template pending, Q-16).
- The data behind each submitted list must be **reproducible** (edition snapshot + audit log).
- The app never marks someone as "authorised to fire"; it only shows **warnings** (BR-04).

## 2. Personal data inventory

| Data | Why | Sensitivity |
|---|---|---|
| Name, surnames | Identification in lists | Normal |
| DNI/NIE | Required by suppliers and the Arms Authority | **High** (identity document) |
| Birth date | Legal-age check (the `UNDER_AGE` warning) and age brackets of the statistics | Normal (the age is derived, never stored; the registry list never returns the birth date) |
| Email, phone | Contact by the FiringChief | Normal |
| Gender | Equality reports | Normal (aggregate use only: the statistics return counts and comparsa names only, never a person; `add-compliance-insights`) |
| ID photo | Required by the Federation for the arquebusier file | **High** (image; not biometric processing) |
| License photos (front/back) | Proof of a valid license | **High** (official document) |
| License type and dates, course date | Compliance warnings | Normal |
| Owned weapons, ownership guides | Required for acts authorisation | **High** (weapon ownership) |
| Orders, loans, proxies | Operation of the edition | Normal |
| External weapon owners (`add-comparsa-orders`): name, surnames, DNI/NIE, weapon model, number and ownership guide | A weapon lent by someone who is not in PolvorApp; the Arms Authority lists will need it | **High** (identity document, weapon ownership) of people who are **not arquebusiers**; typed by the borrower's FiringChief, kept with the loan, replaced or deleted only while the order is editable, then kept as the edition's history |

No special-category data (Art. 9 GDPR) is processed. Photos are **not** used for automated identification.

## 3. Roles (GDPR)

| Party | Role |
|---|---|
| Federation | **Data controller** |
| Comparsas (via FiringChiefs) | Act on behalf of the Federation within the app ❓ (processors vs. joint controllers — to be decided by the Federation) |
| Hosting / email providers | **Processors** — need a data processing agreement (DPA), EU hosting |
| PowderSupplier, WeaponRentalCompany, Arms Authority | **Recipients** of exports |

## 4. Legal basis (proposal)

- **Legal obligation / public interest** for the data the authorities require to authorise the acts.
- **Membership relationship** (performance of the arquebusier's participation) for orders and contact.
- **Consent** only where optional (e.g. gender for equality reports, if the Federation prefers).

Arquebusiers do not use the app, so the **privacy notice must be delivered outside it**. Today a
data protection form is signed when joining the Federation as a comparsa member; it must be
**reviewed to cover PolvorApp** (ID documents, photos, weapons data, recipients). The Federation
should appoint or confirm its **DPO** (unknown today).

## 5. Proposed technical and organisational measures

| ID | Measure |
|---|---|
| SEC-01 | EU-hosted infrastructure; TLS everywhere. |
| SEC-02 | Photos stored in private storage, encrypted at rest, served only after an authorisation check. *Implemented (`add-arquebusier-photos`)*: private S3-compatible bucket under random object names; the browser never reaches the storage — the API streams each photo after the same comparsa-scope check as the arquebusier (BR-12), with `Cache-Control: no-store`. **Go-live checks**: encryption at rest on the bucket, no public policy or ACL, versioning off (or non-current versions expired within a day, otherwise deleted photos survive), one bucket per environment, a key limited to the bucket. |
| SEC-03 | Least privilege: FiringChiefs only access their own comparsa (BR-12). *Orders* (`add-comparsa-orders`): the only cross-comparsa read is the lender lookup. It needs a valid DNI/NIE sent in the request body, answers only an exact match with names, comparsa and weapons (no ownership guide, birth date, contact or license), is rate-limited per user and is audited. The lender's FiringChiefs see a loan of their arquebusier's weapon with the borrower's name and comparsa only. |
| SEC-04 | Email + password with **mandatory 2FA (TOTP) for every user**. Invitation-only accounts. |
| SEC-05 | Audit log of every change and every export (who, when, what). Viewing the dashboard, the warnings or the statistics writes and exports nothing, so it is not audited (`add-compliance-insights`); downloadable statistics, when #12 adds them, are exports and must be audited and suppress small cells. A spreadsheet import (`add-registry-import`) records one registration entry per arquebusier (marked as an import) and one import entry with the comparsa and the count, without personal values or the file name; checking a file and downloading the template write nothing and are not audited. Edition creation, status changes, offered model changes, milestone additions/edits/removals, opening and closing orders, and registry lock toggles are audited (`add-festival-editions`). Order preparation, entry additions and edits (the names of the changed fields, never the values), submissions (attested by a FiringChief or made by an Admin, with the number of entries with warnings), validations and returns (never the reason text) are audited with the order and the comparsa, and the arquebusier's deletion entry lists the current-edition entries and orders it removed (`add-comparsa-orders`); the orders overview writes nothing and is not audited. Each lender lookup records the user and whether an arquebusier was found, never the DNI/NIE; it is audited because it reveals data outside the user's comparsa (`add-comparsa-orders`). Every export download is audited before the file is sent — the user, the definition and its version, the format, the edition, the comparsa and order status for a comparsa list, and the number of rows, never names or DNI/NIE — and a file that cannot be audited is not sent (`add-exports`). |
| SEC-06 | Exports contain only the columns required by each recipient (data minimisation). Provisional definitions (`add-exports`): the powder supplier gets totals per comparsa without personal data; the rental company, name, DNI/NIE, comparsa, rented model and flask size; the Arms Authority, additionally the license type and expiry, the weapon's number and ownership guide, and a loan's lender; a comparsa's list goes only to its FiringChiefs and Admins. Files are generated on request, never stored, sent with `Cache-Control: no-store`, and their text cells are neutralised against formula injection. |
| SEC-07 | Encrypted daily backups with tested restore. |
| SEC-08 | Retention: arquebusiers who stop firing stay as RESERVE at the comparsa's discretion; when they leave the Federation they are **deleted** (BR-14). Old license photos are deleted when new ones are uploaded and when the license is removed; a renewal without new photos keeps the previous ones until they are replaced (accepted, maintainer decision: they stay part of the current license record and the UI reminds the user to replace them). Replaced, removed and deleted images are erased right after the change; if that fails, an hourly sweep erases images no record references (worst case about 2 h). Optional: warn FiringChiefs about arquebusiers in RESERVE for more than N years. *Order history* (`add-comparsa-orders`, maintainer decision): every edition entry and loan keeps its own copy of the arquebusier's or lender's name, DNI/NIE, `federationId` and weapon data, so past editions still read correctly after a person leaves the registry. Deleting an arquebusier while the orders of the edition in progress are open removes their entry of that edition; every other entry is kept until a GDPR erasure request (SEC-09, #15). The Federation must state this retention and its legal basis (e.g. the records owed to the Arms Authority) in the privacy notice (Q-50), which must also cover external weapon owners. |
| SEC-09 | Data subject rights: export and erasure (UC-26). |
| SEC-10 | Record of processing activities and, given ID documents and weapons data, a **DPIA** is recommended. |
| SEC-11 | No real personal data in the repository, test fixtures or non-production environments — critical because the **repository is public**. Synthetic seed data only. The comparsas' real workbooks are imported only in production; tests build synthetic workbooks at run time and no workbook file is committed (`add-registry-import`). |
| SEC-12 | EXIF metadata (e.g. GPS) stripped from uploaded photos. *Implemented*: the browser uploads only the cropped image, and the server re-encodes every photo as a fresh JPEG without EXIF, XMP, IPTC, ICC or comments, after turning it upright. |
