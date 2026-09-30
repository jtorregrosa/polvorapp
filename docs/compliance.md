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
| Birth date | Legal-age check | Normal |
| Email, phone | Contact by the FiringChief | Normal |
| Gender | Equality reports | Normal (aggregate use only) |
| ID photo | Required by the Federation for the arquebusier file | **High** (image; not biometric processing) |
| License photos (front/back) | Proof of a valid license | **High** (official document) |
| License type and dates, course date | Compliance warnings | Normal |
| Owned weapons, ownership guides | Required for acts authorisation | **High** (weapon ownership) |
| Orders, loans, proxies | Operation of the edition | Normal |

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
| SEC-02 | Photos stored in private storage, encrypted at rest, served only via short-lived authorised URLs. |
| SEC-03 | Least privilege: FiringChiefs only access their own comparsa (BR-12). |
| SEC-04 | Email + password with **mandatory 2FA (TOTP) for every user**. Invitation-only accounts. |
| SEC-05 | Audit log of every change and every export (who, when, what). |
| SEC-06 | Exports contain only the columns required by each recipient (data minimisation). |
| SEC-07 | Encrypted daily backups with tested restore. |
| SEC-08 | Retention: arquebusiers who stop firing stay as RESERVE at the comparsa's discretion; when they leave the Federation they are **deleted** (BR-14). Old license photos are deleted on renewal. Optional: warn FiringChiefs about arquebusiers in RESERVE for more than N years. |
| SEC-09 | Data subject rights: export and erasure (UC-26). |
| SEC-10 | Record of processing activities and, given ID documents and weapons data, a **DPIA** is recommended. |
| SEC-11 | No real personal data in the repository, test fixtures or non-production environments — critical because the **repository is public**. Synthetic seed data only. |
| SEC-12 | EXIF metadata (e.g. GPS) stripped from uploaded photos. |
