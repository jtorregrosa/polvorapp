# Glossary

Ubiquitous language for PolvorApp. Every spec, model, table and UI label must use these terms.
The **Code term** column is the identifier to use in code. Spanish terms with no faithful English
equivalent are kept as-is (e.g. `Comparsa`).

> Status: **v1.0** (after round 5) — items marked ❓ are pending confirmation (see `open-questions.md`).

## Organisations and people

| Spanish (source) | Code term | Definition |
|---|---|---|
| Unión / Federación Unión de Comparsas Ber-Largas | `Federation` | Umbrella body that coordinates all comparsas, aggregates orders, organises training courses and deals with suppliers and authorities. Owner and host of PolvorApp and GDPR data controller. |
| Comparsa | `Comparsa` | A festival troupe/association. There are ~20. Each belongs to one `Side`. Example: Contrabandistas. |
| Logo / escudo de la comparsa | `logo` (of a `Comparsa`) | The comparsa's emblem, an optional PNG uploaded by an Admin. Shown next to its name; never a Federation or real emblem in the repository. |
| Bando (Moro / Cristiano) | `Side` (`MOORISH`, `CHRISTIAN`) | The side a comparsa belongs to. Usually goes with a weapon kind (Moorish → arcabuz, Christian → trabuco), but PolvorApp does not enforce it: Federation labels such as "ARCABUZ CRISTIANO" exist (Q-53). |
| Comparsista / Socio | `Member` | Person registered in a comparsa. Managed in the Federation's external app, **not** in PolvorApp. |
| ID Unión | `federationId` | ID of the person's record in the Federation's external app. Mandatory cross-reference. |
| Arcabucero | `Arquebusier` | Member registered for firing activities. PolvorApp is the **authoritative source** for arquebusier data. An arquebusier is **not** a `User`: never invited, never signs in. |
| Género (Hombre / Mujer / Sin definir) | `Gender` (`MALE`, `FEMALE`, `UNSPECIFIED`) | Kept only for equality reports (Q-32). |
| Jefe de Disparo | `FiringChief` | Comparsa officer responsible for its arquebusiers: data, orders, pickups. A comparsa can have several. Accountable for their arquebusiers meeting the requirements. A FiringChief who also fires (powder, weapon) is **also** an `Arquebusier`: two separate records (`User` and `Arquebusier`) with no link between them (Q-52). |
| Administrador (Unión) | `Admin` | Federation user with full access: validates, locks, aggregates, exports. Only Federation role for now. |
| Intervención de Armas (Guardia Civil) | `ArmsAuthority` | Government office that authorises the festival firing acts. Receives reports; not a user. |
| Proveedor de pólvora | `PowderSupplier` | Company that sells the powder to the Federation. Not a user. |
| Proveedor / empresa de alquiler de armas | `WeaponRentalCompany` | Company that rents weapons to the Federation, numbers them and checks their return. Not a user. |

## Accounts and access

| Spanish | Code term | Definition |
|---|---|---|
| Usuario | `User` | Someone who signs in to PolvorApp: an `Admin` or a `FiringChief`. Accounts exist only by invitation. |
| Invitado / Activo / Desactivado | `UserStatus` (`INVITED`, `ACTIVE`, `DEACTIVATED`) | Derived, never stored: `INVITED` until the invitation is accepted (no password yet), `ACTIVE` afterwards, `DEACTIVATED` when an Admin blocks the account (sessions end, data kept). |
| Invitación | `Invitation` | Emailed single-use link (valid 7 days) with which an invited user sets a password and enrols an authenticator. Resending replaces the previous link. |
| Verificación en dos pasos | `TwoFactor` | Mandatory 6-digit code from an authenticator app (TOTP) after the password (ADR-0004). |
| Código de recuperación | `RecoveryCode` | One of 10 single-use codes shown once at enrolment, used to sign in without the phone. |
| Dispositivo recordado | `RememberedDevice` | Browser the user chose to trust for 30 days: the code is not asked there. Forgotten on password change, 2FA reset, deactivation and "sign out everywhere". |
| Registro de auditoría | `AuditEntry` | Append-only record of a write, export or security event: who (`actorUserId`), what (`action`, `entityType`, `entityId`), when, comparsa and trace id. Never contains secrets. |

## Licensing and compliance

| Spanish | Code term | Definition |
|---|---|---|
| Licencia | `License` | Legal authorisation to use black-powder weapons. Has a type, issue date, expiry date, status and front/back photos. Only the current license is kept (no number, no history). |
| AE — licencia de avancarga | `LicenseType.AE` | Muzzle-loading weapons license. The usual one for arquebusiers. Valid 5 years. |
| A-PROF — licencia profesional | `LicenseType.A_PROF` | Professional license (police, etc.). Renewed yearly (Q-27). |
| En trámite | `LicenseStatus.PENDING` | License applied for but not yet issued. |
| Vigente / Caducada | `LicenseStatus.VALID`, `LicenseStatus.EXPIRED` | Derived, never stored: `VALID` through the expiry date, `EXPIRED` afterwards (date in Europe/Madrid). "Expiring soon" is a compliance warning, not a status. |
| Foto de carnet | `idPhoto` | Portrait photo of the arquebusier (3:4), printed on the badge. Optional but expected; a missing one is shown as "No ID photo". Kind `ID`. |
| Foto de la licencia (anverso / reverso) | `frontPhoto`, `backPhoto` | Photos of the front and back of the current license. Need a license; kept on renewal until replaced, deleted with the license. Kinds `LICENSE_FRONT`, `LICENSE_BACK`. |
| Tipo de foto | `ArquebusierPhotoKind` | `ID` \| `LICENSE_FRONT` \| `LICENSE_BACK`; URL slugs `id`, `license-front`, `license-back`. |
| Carnet de arcabucería | `ArquebusierBadge` | Badge issued by the Federation to each arquebusier and worn around the neck during the acts. Shows photo, surnames, name, DNI, federation code, license expiry and comparsa. Generated by PolvorApp (UC-30). |
| Curso de arcabucería | `TrainingCourse` | Mandatory course organised by the Federation. PolvorApp only records **done + date**. |
| Aviso | `ComplianceWarning` | A compliance warning of an arquebusier (BR-04), derived and never stored, that never blocks anything: `LICENSE_MISSING`, `LICENSE_PENDING`, `LICENSE_EXPIRED`, `LICENSE_EXPIRING`, `COURSE_MISSING`, `UNDER_AGE`, `ID_PHOTO_MISSING`, `LICENSE_PHOTOS_MISSING` (conditions in `data-model.md` §5). |
| Caduca pronto | `LICENSE_EXPIRING` | A valid license that expires in less than 12 months. A warning, not a license status. |
| Menor de edad | `UNDER_AGE` | Younger than 18. Arquebusiers must be of legal age (Q-54); PolvorApp warns, the FiringChief is accountable. |
| Guía de pertenencia | `OwnershipGuide` | Official document proving ownership of a personal weapon. Has a number (several formats observed). |

## Festival and acts

| Spanish | Code term | Definition |
|---|---|---|
| Fiestas {año} | `FestivalEdition` | One yearly edition. Orders, rentals, loans and distributions are scoped to an edition. An Admin opens and closes its orders. |
| Estado de edición | `EditionStatus` (`DRAFT`, `IN_PROGRESS`, `CLOSED`) | Lifecycle of an edition. `DRAFT`: in preparation, hidden from FiringChiefs. `IN_PROGRESS`: the current edition, FiringChiefs can see it. `CLOSED`: finished. Admins move it one step at a time, forward or one step back. |
| Pedidos abiertos / cerrados | `ordersOpen` | Flag on the current edition. When true, FiringChiefs may edit orders. When false, they are read-only. Admins toggle it manually; reopening is the corrections window. |
| Edición actual | `currentEdition` | The edition `IN_PROGRESS`. At most one at a time. FiringChiefs edit orders only while its orders are open. |
| Acto | `FestivalAct` | Act where firing happens: Diana Mora, Diana Cristiana, Embajada Mora, Embajada Cristiana, Salvas al Patrón. |
| Diana | `Diana` | Morning act where the shooting contest is held. |
| Salvas al Patrón | `PatronSalute` | Minor firing act in honour of the patron saint. |
| Concurso de disparo | `ShootingContest` | **Out of scope** for now. |
| Evento de calendario | `CalendarMilestone` | Administrative dates of an edition (license renewal call, course, deadlines…). Can trigger notifications. |
| Bloqueo del registro | `RegistryLock` | An independent lock on the registry (not tied to editions). When on, FiringChiefs cannot register, edit, change status, manage weapons or photos; Admins always write. Toggled by Admins and audited. |

## Weapons

| Spanish | Code term | Definition |
|---|---|---|
| Arma | `Weapon` | A physical unit identified by the number engraved on the stock (`weaponNumber`). |
| Trabuco | `WeaponKind.TRABUCO` | Usually the Christian-side weapon (not enforced, Q-53). |
| Arcabuz | `WeaponKind.ARCABUZ` | Usually the Moorish-side weapon (not enforced, Q-53). |
| Pistola | `WeaponKind.PISTOL` | May be owned; **never rented**. |
| Diestro / Zurdo | `Handedness` (`RIGHT`, `LEFT`) | |
| Normal / Pequeño | `WeaponSize` (`NORMAL`, `SMALL`) | |
| Modelo de alquiler | `WeaponModel` | Kind × side × handedness × size (pistols need none of the last three), with the Federation's label. The catalogue is stable but availability varies per edition. Deactivated when retired, deleted only if never used. |
| Número de arma | `weaponNumber` | Engraved on the stock; for rented weapons it is stamped by the rental company (e.g. `37-17`). |
| Arma propia | `OwnedWeapon` | Weapon owned by an arquebusier, with an ownership guide. |
| Arma de alquiler | `RentalWeapon` | Weapon rented for an edition, assigned to **exactly one** arquebusier, non-transferable, returned to the company after the festival. |
| Cesión de arma | `WeaponLoan` | A weapon lent to an arquebusier for an edition (`LOAN` entries). The lender is a registered arquebusier of any comparsa (`LenderKind.ARQUEBUSIER`) or an **external owner** who is not in PolvorApp (`LenderKind.EXTERNAL`), with name, DNI/NIE and the weapon's model, number and ownership guide. No limit; one weapon may be lent to several borrowers. |
| Búsqueda del propietario | lender lookup | The borrower's FiringChief types the owner's DNI/NIE to find a registered lender and their weapons; otherwise they enter an external owner. Rate-limited and audited. |

## Powder and materials

| Spanish | Code term | Definition |
|---|---|---|
| Pólvora | `Powder` | Black powder, ordered **per arquebusier per edition**: 0, 1 or 2 kg. Leftovers are sent to the powder magazine for destruction — **no carryover**. |
| Pedido | `EditionEntry` | One arquebusier's participation in an edition: status, powder, caps, weapon, flask. |
| Pedido de comparsa | `ComparsaOrder` | All entries of a comparsa for an edition, submitted by a FiringChief to the Federation. |
| Declaración responsable | `attestation` (`attested`) | What a FiringChief confirms when submitting an order: the arquebusiers of the order meet the requirements (license, course and legal age), after seeing the pending warnings, which never block. An Admin submitting on the comparsa's behalf does not attest (`submittedByAdmin`). |
| Primer año | `firstYear` | An arquebusier with no `ACTIVE` entry in an earlier edition. Derived, never stored; unknown until an earlier edition has orders (`data-model.md` §5). |
| Pistones | `PercussionCaps` | Caps, in boxes, type `NORMAL` or `SMALL`. Ordered through the Federation. |
| Cantimplora / Polvorera | `PowderFlask` | Container to carry powder; owned or rented (1 kg or 2 kg). Rented flasks are numbered, assigned to one person at powder distribution and returned to the rental company after the acts. |
| Activo | `ArquebusierStatus.ACTIVE` | Arquebusier who fires. |
| Reserva / Inactivo | `ArquebusierStatus.RESERVE` | Arquebusier who does not fire (0 kg) but stays on the list: inactive for some years, or available as pickup proxy. The only other status besides ACTIVE. |
| Baja de la Unión | — | Person leaves the Federation ⇒ deleted from PolvorApp. |
| Resumen de pago | `BillingSummary` | Amount a comparsa owes the Federation for an edition (powder + caps + rentals). Payments themselves are out of scope. |

## Distribution

| Spanish | Code term | Definition |
|---|---|---|
| Reparto / Recogida | `Distribution` | Scheduled day when the Federation hands out powder (outside town, with Guardia Civil, possibly **without connectivity**) or weapons, in slots per comparsa. |
| Devolución | `WeaponReturn` | After the acts, rented weapons and flasks are returned to the rental company, which checks them. **Out of scope** — the Federation does not manage it. |
| Turno | `DistributionSlot` | Time slot assigned to a comparsa on a distribution day. |
| Nº de orden | `distributionNumber` | Sequential number of each arquebusier in the global powder distribution list. |
| Autorizado / Autorización de recogida | `PickupProxy` | **Exceptional**: arquebusier who collects powder or a weapon on behalf of a holder who cannot attend, with a paper form signed by both. **Must be on the edition list** (active or reserve). |
| Trazabilidad 1 / 2 | `traceability1`, `traceability2` | Codes recorded at powder handover. ❓ meaning |
| Almuerzo | — | Comparsa-internal lunch. **Out of scope.** |
