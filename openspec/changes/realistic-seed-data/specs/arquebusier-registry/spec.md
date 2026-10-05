# Spec Delta

## MODIFIED Requirements

### Requirement: Synthetic registry data
The synthetic seed SHALL create fictional arquebusiers in the seeded comparsas. The scenarios
dataset SHALL create the scenario arquebusiers, which SHALL cover:
- `ACTIVE` and `RESERVE`;
- every license status, a valid license that expires within 12 months, and no license;
- course done and not done;
- an arquebusier under 18;
- owned weapons, including a pistol.

The full dataset SHALL also create a population of 15 to 40 arquebusiers in each of the sixteen
comparsas it adds (see the catalogue's synthetic data). The population SHALL be believable and
consistent:
- each person SHALL have a first name and two surnames from common names of the Alicante area, in
  Spanish or Valencian forms, and a gender that matches the first name;
- ages SHALL range from 16 to 75; an arquebusier under 18 SHALL NOT hold an issued license, and an
  issued license SHALL NOT start before its holder turned 18;
- most SHALL be `ACTIVE` with a valid AE license and the course done; a minority SHALL show each
  compliance warning;
- an owned weapon SHALL match the side of its owner's comparsa, or be a pistol;
- the email, when present, SHALL be derived from the person's name on the reserved
  `polvorapp.example` domain, and the phone, when present, SHALL be a Spanish mobile number.

Every DNI and NIE SHALL have a valid check letter (BR-01) and SHALL come from a number range chosen
because no evidence shows it in use (Spain has no reserved fictional range): DNIs from 99000000 to
99999999 and NIEs from Z9000000 to Z9999999. The DNI numbers 1 to 99, reserved for historical and
royal holders, SHALL NOT be used. They SHALL be
unique and SHALL NOT fall in the ranges the automated tests use for the rows they create.

The seed SHALL give most arquebusiers an ID photo and most licensed arquebusiers both license
photos, and SHALL leave others without photos. It SHALL include an issued license with only one of
its two photos. Together, the scenario arquebusiers SHALL show every compliance warning.
- ID photos SHALL be generated faces of people who do not exist, kept with the repository's
  synthetic data. Each SHALL match the arquebusier's gender and age band, and a face SHALL be
  reused only after every face of that gender and band has been used.
- License photos SHALL be drawn specimens of the card's front and back. They SHALL be filled with
  the arquebusier's own national ID, name, birth date and license type and dates, carry a visible
  "MUESTRA – SIN VALIDEZ" watermark, and show no coat of arms, flag emblem or other official mark.
- No seeded image SHALL be a photograph of a real person or of a real document.

Dates SHALL be relative to the day the seed first runs, so a freshly seeded environment shows every
warning whatever the date. The seed SHALL use fixed identifiers, SHALL be deterministic and SHALL be
safe to run again. It SHALL NOT contain real names of known people, real national IDs, contact data,
ownership guides or images.

#### Scenario: Seeding twice
- **WHEN** the seed command runs twice on the same database and storage
- **THEN** the same arquebusiers, owned weapons and photos exist once each

#### Scenario: Seeded FiringChief sees their arquebusiers
- **WHEN** the seeded FiringChief `jefa.dos@polvorapp.example` signs in and opens the arquebusiers page
- **THEN** only the seeded arquebusiers of Cruzados, the comparsa assigned to them, are listed

#### Scenario: Seeded photos
- **WHEN** an Admin opens the detail page of a seeded arquebusier that has photos
- **THEN** a generated face and both specimen license photos are shown, and some other seeded arquebusiers show "No ID photo"

#### Scenario: Every warning is seeded
- **WHEN** an Admin opens the start page of a freshly seeded environment
- **THEN** every warning figure counts at least one arquebusier

#### Scenario: Realistic names
- **WHEN** an Admin lists the seeded arquebusiers
- **THEN** each has a first name and at least one surname, the Spanish nationals have two, and no name contains "Sintético" or "Sintética"

#### Scenario: Seeded national ID ranges
- **WHEN** the full dataset has run
- **THEN** every seeded DNI is between 99000000 and 99999999 and every seeded NIE between Z9000000 and Z9999999, all with valid check letters and all different

#### Scenario: Full population
- **WHEN** the full dataset has run
- **THEN** each of the sixteen added comparsas has between 15 and 40 arquebusiers, no arquebusier under 18 holds an issued license, and every owned weapon matches its owner's side or is a pistol

#### Scenario: Faces match the person
- **WHEN** the full dataset has run
- **THEN** every seeded ID photo is a generated face of the arquebusier's gender and age band, and no face is used twice while another face of that gender and band is unused

#### Scenario: Specimen license photos
- **WHEN** an Admin opens the license photos of a seeded licensed arquebusier
- **THEN** the front shows that arquebusier's national ID, name, birth date and issue date, the back shows the license type and its valid-from and valid-to dates, and both carry the "MUESTRA – SIN VALIDEZ" watermark
