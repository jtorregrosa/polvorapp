# Current State (As-Is) Analysis

> Status: **v1.0**. Based on the files in `docs/sources/` (not versioned — they contain
> personal data) and the round 1 answers. No personal data is reproduced here.

## 1. Source inventory

| File | Owner | Purpose |
|---|---|---|
| `ARCABUCEROS.xlsx` | Comparsa (FiringChief) | Internal master workbook of the comparsa's arquebusiers. |
| `Arcabuceros Fiestas 2026.xlsx` | Comparsa → Federation | Yearly order list sent to the Federation. |
| `RECOGIDA POLVORA 2026.xlsx` | Federation | Powder distribution list for the comparsa. |
| `Recogida Armas 2026.xlsx` | Federation | Weapon distribution list for the comparsa. |
| `CONTRABANDISTAS (1).pdf` | Federation external app | Official arquebusier roster of the comparsa (56 people). |
| `Autorización recogida pólvora 2026.pdf` | Federation | Paper form to authorise a proxy for powder pickup. |

## 2. Workbook `ARCABUCEROS.xlsx` (comparsa master)

### Sheet `Listado` — arquebusier registry (~85 rows)

| Column | Type / validation | Notes |
|---|---|---|
| APELLIDOS, NOMBRE | text | Surnames and first name. |
| DNI | text | National ID with letter. |
| ID UNIÓN | integer | Member code in the Federation app (`CÓDIGO` in the PDF roster). Natural key shared with the Federation. |
| EMAIL, TELÉFONO | text | Contact. |
| FECHA NACIMIENTO | date | |
| CUMPLEAÑOS, EDAD | **formula** | Next birthday, age. → derived fields. |
| GÉNERO | list: Hombre / Mujer / Sin Definir | Used for statistics. |
| TIPO LICENCIA | list: `AE`, `A-PROF` | |
| FECHA LICENCIA | date | Issue date. |
| CADUCIDAD LICENCIA | **formula** = issue + 60 months | Derived. |
| CADUCIDAD (MESES) | **formula** | Months until expiry. Derived. |
| Nº GUÍA PERTENENCIA | text | Formats observed: `NN-NNN-NN`, `AE-<DNI>-N`, 7 digits. |
| ARMA PROPIA | boolean | Owns a weapon. |
| CURSO | boolean | Mandatory course done. |
| ACTIVO | boolean | |
| PRIMER AÑO | boolean | |

Later sheets reference additional columns (`RESERVA`, `U`, `V`) that were removed or renamed — sign of
manual evolution.

### Sheet `⚠️Avisos` — alerts (Google Sheets `QUERY`)

The FiringChief built these alerts by hand. They are **direct requirements** for PolvorApp:

1. License expiring in **< 12 months** (ordered by expiry).
2. Arquebusiers **not active**.
3. Arquebusiers **with course** (and ownership guide number).
4. Two other boolean-flag lists (columns no longer in `Listado`; ❓ which).
5. Arquebusiers **not in first year** / **in first year**.
6. **Next 10 birthdays** (nice-to-have).

### Sheet `📈Estadísticas`

Totals; age brackets (<25, 25–35, 35–45, >45); gender; with/without course; owned weapons; first-year count.

### Sheet `🗓️Eventos` — yearly calendar

Milestones per edition: call for license requests/renewals (WhatsApp group) → course for newcomers →
deadline for new arquebusiers → deadline to submit licenses to the Federation → arquebusiers' lunch →
Embajada Mora → Embajada Cristiana.

### Sheet `📄Notas`

Dated notes with author (two FiringChiefs of the comparsa → **more than one FiringChief per comparsa**).

### Sheet `🔥Año 2026` — yearly order (per arquebusier)

| Column | Values |
|---|---|
| ACTIVO, RESERVA, ARMA PROPIA | boolean |
| ARMA ID, POLVORERA ID | identifiers of assigned weapon / flask |
| PÓLVORA | `0 kg`, `1 kg`, `2 kg` |
| PISTONES (CAJAS) | number of boxes |
| PISTONES TIPO | `Normales`, `Pequeños` |
| ALQUILER ARMA | `Arcabuz Pequeño`, `Arcabuz Normal`, `Arcabuz Zurdo Pequeño`, `Arcabuz Zurdo Normal`, `Pistola`, `No Alquila` |
| ALQUILER POLVORERA | `1 kg`, `2 kg`, `No alquila` |
| CESIÓN (DNI TITULAR) | ID of the weapon owner lending the weapon |
| RECARGA, SALVAS PATRÓN, ALMUERZO | boolean |

Header block: **price per kg** (55 €), **carryover powder** from last year, **requested powder**,
**total**, and a split between **comparsa powder** and **extra powder** with their cost.

> Round 2: there is **never carryover** — leftover powder goes to the powder magazine for destruction.
> Arquebusiers pay their fee to the comparsa, which makes one joint transfer to the Federation.
> `RESERVA` = arquebusier not firing this year (0 kg) kept on the list so they can be a pickup proxy.
> `SALVAS PATRÓN` = another (minor) firing act. `ALMUERZO` = comparsa-internal, out of scope.

Sheets `Fiestas 2026`, `Copia de 🔥Año 2026` and `2026` are variants/copies of the same order →
evidence of the versioning problem.

## 3. Order sent to the Federation (`Arcabuceros Fiestas 2026.xlsx`)

Columns: APELLIDOS, NOMBRE, NÚMERO DNI, LETRA DNI (split!), KG PÓLVORA, CAJAS PISTONES,
ALQUILA ARCABUZ, NÚMERO DE ARMA, Nº GUÍA DE PERTENENCIA, PROPIEDAD / CESIÓN.

- Weapon names in Federation format: `ARCABUZ CRISTIANO DIESTRO`, `ARCABUZ CRISTIANO DIESTRO (PEQUEÑO)`, `NINGUNO`
  → the **side** (Cristiano/Moro) is part of the weapon type.
- Weapon numbers look like `NN-YY` (e.g. `37-17`, `3-25`).
- The comparsa's internal names differ from the Federation's → mapping needed today; one catalogue in PolvorApp.

## 4. Distribution lists (Federation)

**Powder** (`RECOGIDA POLVORA 2026.xlsx`):
- *Holder*: global order number (continues across comparsas, e.g. starting at 83), surnames, name,
  DNI number, DNI letter, kg, **traceability 1**, **traceability 2**, **owned flask**.
- *Authorised proxy*: surnames, name, DNI, letter.

**Weapons** (`Recogida Armas 2026.xlsx`): order number, surnames, name, DNI, letter,
**proxy pickup** ("cesión recogida"), weapon type, weapon reference.

**Proxy form** (PDF): the holder authorises another arquebusier **of the same comparsa**, with a licence,
to pick up the powder, stating a reason; both sign.

## 5. Official roster (Federation external app, PDF export)

Columns: code (Federation ID), surnames, name, license expiry, birth date, DNI, phone. It is the
**authoritative source** for license dates and photos today. Only comparsa secretaries can edit it.

## 6. Current yearly process (inferred — to validate)

```
Oct   Call for license requests/renewals (WhatsApp)
      Federation training course for newcomers
      Deadline: new arquebusiers
      Deadline: licenses submitted to Federation
~Jan  Federation opens Google Form → FiringChief enters per arquebusier:
      kg powder, flask rental, weapon rental or own weapon, loans
      Federation validates, aggregates, produces global spreadsheets →
        PowderSupplier / WeaponRentalCompany / Arms Authority
~Apr  Weapon distribution day (slots per comparsa, proxies allowed)
      Powder distribution day (outside town, with Guardia Civil, slots per comparsa,
        traceability recorded, proxies with signed form)
      FESTIVAL: Dianas (shooting contest), Embajadas
After Leftover powder → powder magazine for destruction (no carryover)
      Rented weapons returned to the rental company, which checks all are back
```

Round 2 confirmations: the Federation can **lock** each edition and the general registry; the
powder distribution site may have **no or poor connectivity**.

## 7. Data quality issues observed

| Issue | Example (anonymised) | Requirement it implies |
|---|---|---|
| DNI stored as text in one file, split number/letter in others, with spaces in another | `12345678Z` / `12345678` + `Z` / `12345678 Z` | Store normalised; format on export. |
| **Non-Latin look-alike character** in DNI letter | Greek `Η` instead of `H` | Validate DNI/NIE check letter. |
| Missing DNI letter | `12345678` | Validation at input. |
| Name spelling differs between files | accents, casing | Single registry; exports derived from it. |
| Expired license still on roster | expiry in 2024 | Alerts + blocking rule for orders. |
| Same phone for two people | — | Warn, don't block. |
| Broken validations (`#REF!`), duplicated sheets | — | Structured data model. |
| Different weapon naming between comparsa and Federation | `Arcabuz Normal` vs `ARCABUZ CRISTIANO DIESTRO` | Shared catalogue. |

## 8. Pain points (from answers)

- **Comparsa**: maintaining and updating data; re-typing it into forms.
- **Federation**: consistency and correctness of the data received from 20 comparsas.
- **Both**: sending spreadsheets and forms back and forth; no single portal.
