# Spec Delta

## MODIFIED Requirements

### Requirement: Synthetic catalogue data
The synthetic seed SHALL create fictional comparsas of both sides with plausible festival names,
including one inactive. No seeded comparsa SHALL bear the name of a comparsa of San Vicente del
Raspeig. The scenarios dataset SHALL create four of them: Cruzados (Christian), Abencerrajes
(Moorish), Hospitalarios (Christian) and Zegríes (Moorish, inactive). The full dataset SHALL also
create sixteen more, all active: Tercios, Ballesteros, Corsarios, Labradores, Mozárabes, Caballeros
de Sant Jordi, Almirantes and Guardia del Rey (Christian), and Almohades, Nazaríes, Mudéjares,
Bereberes, Beduinos, Kábilas, Califas and Sarracenos (Moorish).

It SHALL assign the seeded FiringChiefs so that one comparsa has two FiringChiefs and one FiringChief
has two comparsas. The full dataset SHALL also assign one seeded FiringChief to each of the sixteen
added comparsas.

It SHALL give every seeded comparsa but Abencerrajes a generated logo: an invented heraldic emblem
with motifs of the comparsa's side, on a transparent background, without text, letters or any real
emblem, generated for the project and kept with the repository's synthetic data. At least one logo,
Cruzados', SHALL be dark enough to need the light tile in the dark theme. Logos SHALL pass the same
logo rules as an upload.

It SHALL also create a weapon catalogue covering every kind, including a non-rentable pistol and an
inactive model. It SHALL use fixed identifiers and SHALL be safe to run again. The seed SHALL NOT
contain real comparsa names, real comparsa logos or real Federation data. Production data SHALL be
entered by an Admin.

#### Scenario: Seeding twice
- **WHEN** the seed command runs twice on the same database
- **THEN** the same comparsas, logos, assignments and models exist once each

#### Scenario: Seeded FiringChief scope
- **WHEN** the seeded FiringChief `jefe.uno@polvorapp.example` signs in
- **THEN** only Cruzados and Abencerrajes, the comparsas assigned to them, are listed

#### Scenario: Seeded logos
- **WHEN** the seed has run
- **THEN** every seeded comparsa but Abencerrajes has a generated emblem that passes the logo rules, and Cruzados' emblem is dark

#### Scenario: Scenario comparsas
- **WHEN** the scenarios dataset has run
- **THEN** exactly Cruzados, Abencerrajes, Hospitalarios and Zegríes exist, and only Zegríes is inactive

#### Scenario: Full dataset comparsas
- **WHEN** the full dataset has run
- **THEN** the twenty comparsas exist, ten on each side, and each of the sixteen added comparsas has one seeded FiringChief
