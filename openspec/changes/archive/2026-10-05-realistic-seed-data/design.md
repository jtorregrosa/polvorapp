# Design

## Context

See proposal.md (Why). Facts that shape the approach:

- **How seeding runs.**
  - `seed` is a host verb (`HostCommands`, `SeedCommand`). It runs every module's `IDataSeeder` by
    `Order`: Identity 10, Catalog 20, Registry 30, Editions 40, Orders 50, Distribution 60,
    Notifications 70.
  - It is allowed in Development, Staging and Testing. Outside Development and Testing, each seeder
    refuses a database that holds rows whose ids it would not create (NFR-13).
  - In compose, `api-seed` runs it with the seed credentials. CI runs
    `docker compose run --rm api-seed` before Playwright.
- **Fixed ids are shared as literals.**
  - Comparsas `0193a100-…-1..4`, users `0193a000-…-1..5` and arquebusier numbers 1–14 are repeated
    as literals in the Catalog, Registry, Order and Distribution seeders, their tests and about 15
    E2E specs.
  - The E2E specs also match seeded **names**: "Sintético Uno, Arcabucero", "Comparsa Sintética
    Norte", "Jefe Sintético Uno", "Admin Sintética", the history copy "DNI/NIE 00000091E" and the
    badge file name `…comparsa-sintetica-este-….pdf`.
- **Backend tests run the whole seed often.** About 45 runs across the seeder tests, each on its own
  database clone and often twice. No other test seeds.
- **Images.**
  - Seeded images go through `IImageNormalizer` (Skia, singleton, one image at a time, 30 s wait).
  - ID photos must be 3:4 and at least 600 × 800 px.
  - License photos need a long side ≥ 800 and a side ratio ≤ 2. Today's synthetic cards are
    1000 × 630.
  - Logos are stored as PNG with transparency, with a long side ≥ 256 (at most 1024).
  - SkiaSharp is referenced only by the API host. The runtime image (`aspnet:…-noble-chiseled-extra`)
    has **no system fonts**: Exports embeds Geist for QuestPDF.
  - Today's images are drawn by `SyntheticPhotos` / `SyntheticLogos` with `SyntheticPng`.
- **Validation the seed must satisfy**, although it writes entities directly:
  - the database checks the national ID format `^([0-9]{8}|[XYZ][0-9]{7})[A-Z]$`, the phone
    format `^[+]?[0-9 ]+$`, lower-case emails, trimmed names and license consistency;
  - national ID and federation ID are unique;
  - order entries must meet the blocking rules of `comparsa-orders` (BR-05, BR-07, BR-09).
- **Ranges the tests already take for the rows they create.**
  - DNI numbers from 1,000, 9,100,000, 20,000,000, 30,000,000, 40,000,000 and 70,000,000; the
    history copy uses 91 and the external owners 92 and 93.
  - Federation IDs from 200,000 and 400,000; the history copy has 100,091.
- **The seeded current edition** offers rentals of models 1, 2, 5, 6 and 7: the right-handed
  trabucos (normal and small), the right-handed arcabuces (normal and small) and the left-handed
  normal arcabuz. Left-handed trabucos are not offered.

## Goals / Non-Goals

**Goals:**
- One switch between a small, stable scenarios dataset (tests, CI) and a realistic full dataset
  (local, staging). The scenarios stay the same in shape, so backend tests and E2E only change
  names.
- Realism without anything real: plausible invented names, consistent dates and orders, ID ranges
  with no evidence of use, generated faces and emblems, specimen documents.
- The same images on every machine, in CI and in staging, from the repository alone.

**Non-Goals:**
- Distribution or notification data for the added comparsas.
- Any change to the application's behaviour, API, schema, screens or translations.
- Faster image normalisation; seeding the full dataset may take a few minutes.

## Decisions

### D1. Two datasets behind one setting

`Seed:Dataset` takes the values `Scenarios` (default) and `Full`. It binds to a `SeedSettings`
record in `SharedKernel/Seeding`. `SeedCommand` validates it before any seeder runs: an unknown
value logs the refusal and exits 1. Seeders read it through `IOptions<SeedSettings>`.

- **Scenarios** reproduces today's rows (same ids, cases and counts) with new names and images.
  - The backend seeder tests, `SeedCommandTests` and the CI E2E job use it, so their counts,
    paging and assertions hold.
  - Running the full population ~45 times would make the test suite minutes slower.
- **Full** = Scenarios + population.
  - Compose defaults `api-seed` to `Full` (`Seed__Dataset: ${SEED_DATASET:-Full}`).
  - CI runs `SEED_DATASET=Scenarios docker compose run --rm api-seed`; the shell variable
    overrides `.env`.
- **Staging guard.** Each seeder builds its id list for the active dataset. A Full staging database
  re-seeded with Scenarios is refused, as any unknown row is today, and the docs say so.

*Alternatives:*
- **Only a full dataset.** Breaks E2E paging assumptions and exact counts (`insights.spec.ts`
  expects 8 warning links) and slows the backend tests.
- **A separate "demo" command.** It would duplicate the guard and the environment rules.

### D2. Scenario comparsas, users and personas

The scenario comparsas keep their ids and roles; only their names change. The roles come from
existing requirements:
- one comparsa of `jefe.uno` has no logo (the platform sidebar scenario);
- one logo is dark (the catalogue's dark-theme tile).

| Id | Old name | New name | Side | Role in the scenarios |
|---|---|---|---|---|
| …0001 | Comparsa Sintética Norte | Cruzados | Christian | both FiringChiefs, dark logo |
| …0002 | Comparsa Sintética Sur | Abencerrajes | Moorish | Jefe Uno, never a logo |
| …0003 | Comparsa Sintética Este | Hospitalarios | Christian | logo, no FiringChief |
| …0004 | Comparsa Sintética Oeste | Zegríes | Moorish | inactive |

The full dataset adds ids `…0005`–`…0020`, all active:
- **Christian:** Tercios, Ballesteros, Corsarios, Labradores, Mozárabes, Caballeros de Sant Jordi,
  Almirantes and Guardia del Rey.
- **Moorish:** Almohades, Nazaríes, Mudéjares, Bereberes, Beduinos, Kábilas, Califas and
  Sarracenos.

No seeded name is a comparsa of San Vicente del Raspeig; some are common festival names elsewhere.

Each seeded user keeps their id and email and gets a new display name:

| Email | Old name | New name |
|---|---|---|
| admin@ | Admin Sintética | Inma Ruiz Bernabeu |
| jefe.uno@ | Jefe Sintético Uno | Joan Moltó Sala |
| jefa.dos@ | Jefa Sintética Dos | Elena Verdú Ivorra |
| invitada@ | Persona Invitada | Sílvia Mora Castelló |
| desactivada@ | Persona Desactivada | Jaume Cortés Puig |

Each scenario arquebusier keeps its number, comparsa, gender, status, license, course and photos.
`NationalId` uses the seeded ranges (D4):

| # | First name | Last name | DNI/NIE number |
|---|---|---|---|
| 1 | Vicent | Sempere Llorens | 99000001 |
| 2 | Amparo | Pastor Gomis | 99000002 |
| 3 | Pau | Alberola Navarro | 99000003 |
| 4 | Remedios | Lledó Pérez | 99000004 |
| 5 | Youssef | El Amrani | NIE Z9000005 |
| 6 | Mari Carmen | Ferrándiz Soler | 99000006 |
| 7 | Josep Ramon | Candela Martínez | 99000007 |
| 8 | Pepa | Mira Carbonell | 99000008 |
| 9 | Toni | Baeza Ripoll | 99000009 |
| 10 | Ioana | Popescu | NIE Z9000010 |
| 11 | Rafael | Climent Esteve | 99000011 |
| 12 | Àlex | Beltrà Riquelme | 99000012 |
| 13 | Francisco | Asensi Mollà | 99000013 |
| 14 | Laia | Sempere Pastor | 99000014 |

The other invented people change too:
- The orders' history copy becomes "Manuel Cerdà Boix", 99000091.
- The external owner becomes "Rosa Maria Agulló Vidal", 99000092.

The single-surname NIE holders (#5, #10) are deliberate: foreign residents usually have one.
Scenario #14 keeps its license expiring at age 16, because the four-warning case (D9 of
add-compliance-insights) needs it; the population rules of the spec apply to the population only.

### D3. People generator: Bogus plus curated lists

`SharedKernel/Seeding/SyntheticPeople` builds the population from a Bogus `Randomizer` seeded with
`SyntheticData.RandomSeed` plus a fixed per-purpose offset, so changing one part does not reshuffle
another.

- **Names.** Curated, frequency-weighted lists live in the code as embedded data:
  - about 70 male and 70 female first names, mixing Spanish and Valencian forms (Vicent, Josep,
    Amparo, Remei…), with a small share of names common among foreign residents;
  - about 150 surnames frequent in the Alicante area. The maintainer's own surnames are excluded.
  - Bogus's `es` locale only tops up the surname pool.
- **Gender.** It follows the first name; the population is about 72 % male, like the faces.
- **Ages.** Ages are 16–75, from bands with the same weights as the faces script.
- **License.** Licenses follow the age:
  - under 18: none, or pending;
  - otherwise issued no earlier than the 18th birthday;
  - AE licenses last 5 years and A-PROF 1 year (`License.cs`).
- **Mix.** About 80 % valid AE, 6 % expiring within 12 months, 6 % expired, 3 % pending and 5 %
  none. About 90 % have the course and about 10 % are `RESERVE`.
- **Contact data.**
  - Email for about 80 %: `nombre.apellido@polvorapp.example`, ASCII-folded and lower-case, with a
    number appended on clashes.
  - A Spanish mobile number for about 90 %, in the format `+34 6xx xxx xxx`.
- **Weapons.** About 30 % own a weapon:
  - the kind follows the side (a trabuco for Christian comparsas, an arcabuz for Moorish ones);
  - 90 % are right-handed, and the size is normal or small;
  - a few own a pistol.
  - The weapon number is random and the guide is `GP-` plus 6 digits; both are unique.
- **Photos.** About 85 % have an ID photo. Of the licensed, about 90 % have both license photos and
  about 3 % only the front.
- **Ids.** Population numbers start at 1001, by comparsa order, so ids are `0193a300-…-{N:D12}`,
  the federation ID is `100_000 + N`, and photos and weapons reuse their formats.
- **FiringChiefs.** The population's FiringChiefs, one per added comparsa, come from the same
  generator.
  - Their ids are `0193a000-…-0000000001NN` (NN = comparsa) and their emails are name-based.
  - The identity seeder creates them; the catalogue assigns them by id.

The generator is a pure function of the dataset and the seed and never reads the database. The
Identity, Catalog, Registry and Order seeders all call it: a module cannot reference another, and
SharedKernel is shared. "Existing rows are left untouched" keeps working.

*Alternative:* Bogus alone. Its `es` locale has Castilian names only and no second surname
convention; the curated lists are what makes the result read as Alicante.

### D4. Seeded national ID ranges

- DNIs are numbers 99,000,000–99,999,999 and NIEs Z9,000,000–Z9,999,999; check letters follow
  BR-01, with Z read as 2.
- Scenarios use 99,000,001–99,000,099, and the population draws without repetition from
  99,100,000 upwards. These ranges are disjoint from every range the tests create rows in.
- **Checked in task 1.2 (2026-10-04).** No public source lists which DNI numbers are issued:
  - The Police assign them in batches to each issuing team, "no necesariamente correlativo al
    anterior" ([Wikipedia: DNI de España](https://es.wikipedia.org/wiki/Documento_nacional_de_identidad_(Espa%C3%B1a))).
  - Numbers 1–99 are reserved for historical and royal holders, so today's seed numbers 1–14
    and 91–93 are replaced.
  - About 85 million electronic DNIs had been issued by December 2021; that counts documents,
    not numbers.
  - No source shows 99,xxx,xxx in use, but none can prove it free either.
  - Z NIEs started in 2022, after Y ran out
    ([Portal de Extranjería](https://www.portalextranjeria.com/el-nuevo-numero-nie-empieza-por-z/)).
    At the pace of X and Y (about 10 million each over 14+ years), Z9xxxxxx will not be reached
    for years.
- **Consequence.** The spec promises ranges with no evidence of use, not "unissued" ones. The
  protection does not depend on the range: a seeded ID is never combined with a real person's
  name, contact data or image.
- The ranges live in one place (`SyntheticNationalIds`).

### D5. Generated images, committed

- **Layout:**
  ```
  backend/synthetic-data/NOTICE.md
  backend/synthetic-data/faces/manifest.json   {"faces":[{"file","gender","ageBand","age","seed"}]}
  backend/synthetic-data/faces/face-0001.jpg …  400 faces, 600 × 800 JPEG (mozjpeg q72), ~11 MB
  backend/synthetic-data/logos/manifest.json   {"logos":[{"comparsa","file","seed","prompt"}]}
  backend/synthetic-data/logos/*.png            19 emblems, transparent PNG, 512 px, ~1.8 MB
  ```
  The notice says what the images are, how they were made and under which license they ship.
- **Generation.** `scripts/generate-seed-images.mjs` replaces `generate-seed-faces.mjs`. It calls
  Z-Image Turbo (Tongyi-MAI, Apache-2.0) through Runpod's public endpoint with fixed seeds.
  - **Work cache.** Raw outputs go to the git-ignored `seed-assets/raw/`, so a rerun never pays
    twice. The 400 raw faces generated on 2026-10-04 are reused as they are.
  - **Optimise step.** It writes the committed files with `sharp` (resolved from
    `frontend/node_modules`):
    - faces are resized to 600 × 800 and encoded with mozjpeg at quality 72;
    - logos have their background removed by a flood fill from the edges over near-white pixels
      (white inside the emblem is kept), are trimmed, scaled to 512 px on the long side and stored
      as palette PNG.
  - **Logo prompts.** Each comparsa has its own prompt: a flat heraldic shield or emblem with motifs
    of its side (crosses, lions, towers, keys, ships for the Christians; crescents, stars,
    scimitars, palms, lamps for the Moors), centred on white, "no text, no letters, no banner".
    Cruzados' prompt asks for black and charcoal tones, so it needs the light tile. Abencerrajes
    gets no logo.
  - **Review.** The maintainer checks the 19 emblems by eye (no text, no likeness to a real
    comparsa's emblem) before they are committed.
- **Reading.** The API project copies `backend/synthetic-data/**` to its output as
  `SyntheticData/` (content, not embedded resources, so nothing is loaded unless the seed reads it).
  The image is about 12 MB larger.
  - `SharedKernel/Seeding/SyntheticImages` reads the manifests from `AppContext.BaseDirectory`.
  - Manifest file names must be plain names (no separators, no `..`, not rooted), and the resolved
    path must stay inside the folder.
  - A missing folder or manifest is a build error. The seed fails naming it, rather than silently
    seeding without images.
  - The test project gets the same files through its reference to the API project.
- **Face assignment.**
  - Each arquebusier with an ID photo gets a face of their gender (any gender for `UNSPECIFIED`)
    and age band on the seed day.
  - Faces are taken in manifest order and reused only when a band runs out. About 420 photos for
    400 faces means a few repeats, mostly among women of the larger bands.
  - The assignment is a pure function of the people list, so it is deterministic.
- **Logos.** The catalogue seeder stores the emblem whose manifest entry names the comparsa,
  keeping the fixed logo ids.
- **Removed:** `SyntheticPhotos` and `SyntheticLogos`. `SyntheticPng` stays only if other code
  still uses it.
- **Licensing.**
  - The model is Apache-2.0 and places no restriction on its outputs.
  - The images ship under the repository's MIT license, and the notice says so.
  - `docs/third-party-licenses.md` lists them under "Data files".
  - They are not personal data: no real person is depicted.

*Alternatives:*
- **A local, git-ignored assets pack.** CI, staging and other clones would see placeholders, and it
  needs a mount, an environment gate and an ADR.
- **Downloading at seed time.** It would make seeding depend on the network and on third-party
  sites.
- **Real comparsa names and emblems.** Third-party assets in a public repository (ADR-0013); the
  maintainer chose invented ones (2026-10-04).

### D6. Specimen license cards

- **Contract.** `SharedKernel/Images/ISpecimenCardPainter`:
  - `Front(SpecimenCardData)` and `Back(SpecimenCardData)` return PNG bytes;
  - `SpecimenCardData` holds the national ID, full name, birth date, license type, issued-on and
    expires-on dates.
- **Implementation.** `PolvorApp.Api/Platform/Images/SkiaSpecimenCardPainter` (singleton), because
  SkiaSharp stays in the host as for the normaliser.
- **Format.** Output is 1000 × 630, the same size as today, so the photo rules and the size
  assertion hold.
- **Layout**, from the Spanish arms license:
  - **Front:** a warm-to-blue background, a red-yellow-red band at the top left, the title
    "LICENCIA DE ARMAS", and labelled fields "N.I.F./N.I.E.", "NOMBRE Y APELLIDOS", "FECHA DE
    NACIMIENTO", "FECHA DE EXPEDICIÓN" and "EL TITULAR".
  - **Back:** the grid with column numbers 1–4 and a type row ("AE" or "A-PROF", valid from,
    valid to, empty restrictions), the "5" observations row, and the legend "1) Tipo de licencia …
    5) Observaciones".
  - **Both sides:** a diagonal, semi-transparent "MUESTRA – SIN VALIDEZ" watermark, repeated.
  - **Never drawn:** coats of arms, the flag emblem, EU stars, "Ministerio del Interior",
    holograms or signatures.
  - The texts are fixed Spanish document wording, not UI text, so they are not translated.
- **Pending licenses.** A pending license has no card, because license photos need an issued
  license (`PhotoStorage.NeedsLicense`), as today.
- **Fonts.**
  - Geist Regular and Bold move from `PolvorApp.Exports/Fonts` to `PolvorApp.SharedKernel/Fonts`,
    as embedded resources with their OFL text, exposed by `EmbeddedFonts`.
  - QuestPDF registration and the painter both load them from there, so there is one copy and no
    system font is needed.
  - The PDF golden files must stay equal in text.

*Alternative:* the current `SyntheticPng` stripes with text drawn by hand. It needs a font
rasteriser; SkiaSharp is already present.

### D7. Orders of the full dataset

The order seeder rebuilds the population with `SyntheticPeople` (D3) and copies identities through
`IArquebusierRoster` as it does today. It uses the population's FiringChiefs as preparers and
submitters, and the seeded Admin as reviewer.

- **Past edition:** one `VALIDATED` order per added comparsa.
  - It holds all its arquebusiers but about 10 %, chosen deterministically. Those become
    first-year in the current edition.
  - Its entries follow the rules below. Submitted and reviewed about 380 days ago, like the
    scenario orders.
- **Current edition**, by comparsa order among the 16:

  | Status | Count | Details |
  |---|---|---|
  | `DRAFT` | 4 | |
  | `SUBMITTED` | 5 | |
  | `RETURNED` | 2 | Reasons such as "Revisad los kilos de los arcabuceros nuevos." and "Faltan las licencias de dos arcabuceros." |
  | `VALIDATED` | 3 | |
  | not prepared | 2 | |

  Each order leaves one or two arquebusiers out ("not in the order").
- **Entry rules:**
  - `RESERVE`: 0 kg, 0 caps, no weapon, no flask (BR-05).
  - `ACTIVE` owner of a trabuco or arcabuz: `OWNED` with that weapon.
  - `ACTIVE` without a weapon:
    - about 80 % rent the offered model of their side, handedness and size (BR-07);
    - left-handed Christians, whose model is not offered, borrow a team-mate's trabuco
      (`LOAN`, BR-09);
    - about 5 % carry no weapon (powder carriers, captains).
  - **Powder:** 2 kg about 60 %, 1 kg about 30 %, 0 kg about 10 %.
  - **Caps:** 0–4 boxes; the type follows the weapon size.
  - **Flask:** `OWNED` for owners, otherwise a rental matching the powder, or none at 0 kg.
- **Warnings.** Arquebusiers with expired or pending licenses keep their entries, so the warnings
  show as they would in reality (BR-04).
- **Ids.** Orders are `0193a700-…-{1000+n}`, entries `0193a710-…-{10000+n}`, and loans use the
  seeder's loan prefix from 1000.
- **Staging guard.** It covers the Full ids.
- **Checking the blocking rules.** A test feeds every generated entry through the same entry and
  loan validation the API uses, so "no blocking problem" is checked, not assumed.

### D8. Seeder changes per module

- **IdentityAccess:**
  - new display names (D2);
  - with Full, one FiringChief per added comparsa (Active, same seed password and key, alternating
    `es-ES` / `ca-ES-valencia`).
- **FederationCatalog:**
  - new names;
  - comparsas 5–20 and their assignments with Full;
  - generated emblems instead of shapes;
  - the guard covers the Full ids.
- **ArquebusierRegistry:**
  - re-skinned scenarios (D2);
  - the population with Full (D3);
  - faces (D5) and painted license cards (D6);
  - a progress log line every 100 photos;
  - the guard covers the Full ids.
- **ComparsaOrders:**
  - the D7 orders with Full;
  - the history copy and external owner of D2;
  - guides `GP-…`.
- **Distribution, FestivalEditions:**
  - same ids and cases;
  - "Almacén de la Federación" and "Paraje del Reparto" for the days;
  - milestone titles: "Convocatoria de licencias", "Curso de formación", "Plazo de nuevos
    arcabuceros", "Reparto de pólvora", "Balance de la edición" and "Entrega de documentación".
- **Notifications:** unchanged. The seeded FiringChief is the same id.

### D9. Tests

- **Backend seeder tests.** They keep using Scenarios and swap name and image assertions. For
  example:
  - "every last name contains Sintétic" becomes "realistic names, none contains Sintétic";
  - comparsa names become the D2 list;
  - ID photos are faces of the right gender.
- **A full-dataset test class runs the seed once** and checks:
  - population per comparsa within 15–40;
  - unique, in-range IDs;
  - no minor with an issued license;
  - weapons that match their side;
  - faces that match gender and band, without early reuse;
  - the order status mix;
  - every entry valid under the API's rules;
  - first-year arquebusiers;
  - FiringChiefs assigned;
  - determinism (two empty databases give identical data);
  - staging guard acceptance.
- **Other unit tests:**
  - `SyntheticPeople`, `SyntheticNationalIds`, `SyntheticImages` (manifest safety, matching);
  - the painter (size, valid PNG, drawn strings through a test seam);
  - `SeedCommand` refusing an unknown dataset.
- **E2E.** A mechanical rename per the D2 tables:
  - 15 specs, the history-copy DNI line and the badge PDF slug `hospitalarios`;
  - ids are unchanged;
  - CI seeds Scenarios.
- **Frontend unit tests and stories** use MSW mocks with their own "Sintético" fixtures. They are
  independent of the seed and stay as they are.

### D10. No API, schema, frontend or i18n change; no audit; no ADR

- The change touches seeders, a shared image service, committed data files and tooling only.
- Seeding writes no audit entries (not a user action).
- No ADR is needed:
  - nothing third-party is committed (ADR-0013 holds);
  - the platform rule that seeders read only the repository's synthetic data is unchanged;
  - SEC-11's wording gains a note about generated images.

## Risks / Trade-offs

- **[The 99M/Z9 ranges are issued after all]** → Task 1.2 checks it before implementation. A change
  costs a constant and the spec text.
- **[A generated name matches a real person]** → Unavoidable with real frequency lists. It is never
  combined with a real ID, contact or image, and the docs state that matches are coincidental.
- **[A generated face resembles a real person]** → Faces come from a model, not from photos of
  anyone. A face flagged as resembling someone is deleted and regenerated with another seed.
- **[A generated emblem resembles a real comparsa's]** → The maintainer reviews the 19 emblems
  before committing (task 2.6), and the prompts avoid the motifs of known local emblems.
- **[The repository grows by ~12 MB, permanently]** → Accepted by the maintainer. The images are
  optimised. Regenerating them is avoided by caching the raw outputs and reusing the committed
  files.
- **[The full seed is slow]** About 1,100 images go through a one-at-a-time normaliser, plus about
  950 order entries: an estimated 1–3 minutes. → It is used only locally and in staging, and
  progress is logged; tests and CI use Scenarios.
- **[Bogus locale data changes between versions and reshuffles names]** → The package is pinned in
  `Directory.Packages.props`, and determinism tests compare two runs on one version.
- **[Moving the Geist fonts changes PDFs]** → The same files and the same registration; golden tests
  catch drift.

## Research (task 1.1, 2026-10-04)

- **Bogus 35.6.5** (latest on NuGet). Its license is MIT; the `LICENSE` file is packed in the
  package and also covers the bundled faker.js data. The API used:
  - `new Randomizer(seed)` is a local seed that ignores the global `Randomizer.Seed`;
  - `Number(min, max)` includes both ends;
  - `Bool(weight)`, `ArrayElement`, `WeightedRandom(items, weights)` and `Shuffle`;
  - `new Faker("es") { Random = new Randomizer(seed) }` gives deterministic locale data.
- **SkiaSharp 4.153.1** (already pinned). The API used:
  - `SKTypeface.FromStream(stream)`, which returns `null` on an invalid font: treat that as an
    error;
  - `new SKFont(typeface, size)` and `canvas.DrawText(text, x, y, SKTextAlign, font, paint)`;
  - `canvas.Save()`, `RotateDegrees`, `Restore()` for the watermark;
  - `SKShader.CreateLinearGradient` for the background;
  - `surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100)` for the output.

  With an explicit typeface, no system font is needed.
- **QuestPDF 2026.9.1.** `FontManager.RegisterFontFromStream(Stream)` exists, so the Geist files
  can live in SharedKernel and be registered from a stream; the resource name no longer has to be
  in the Exports assembly.
- **sharp 0.35.5** (in `frontend/node_modules`). The API used:
  - `resize(600, 800)`, then `.jpeg({ quality: 72, mozjpeg: true })` (measured on the 400 faces:
    10.8 MB);
  - `.ensureAlpha().raw().toBuffer({ resolveWithObject: true })` for the flood fill, and
    `sharp(buffer, { raw: { width, height, channels } })` back;
  - `.trim()`, `.resize({ width: 512, height: 512, fit: 'inside' })` and
    `.png({ palette: true })`;
  - metadata is stripped by default.
- **MSBuild.** A `None` item with `LinkBase="SyntheticData"` and
  `CopyToOutputDirectory="PreserveNewest"` in the API project copies `backend/synthetic-data/**`
  to the output. Referencing projects (the tests) get it through `GetCopyToOutputDirectoryItems`.
  `backend/` is the Docker build context, so the folder is inside it. Publish inclusion is checked
  in task 3.4.

## Measurements (task 4.4, 2026-10-04)

- **Full seed, local Testcontainers (PostgreSQL + MinIO) on the maintainer's machine.** The whole
  `FullRegistrySeedTests` class takes 2 min 13 s. It covers two first-time full seeds on empty
  databases and one rerun, so one first-time full seed is about one minute.
- **Size.** 451 arquebusiers (14 scenarios + 437 people), about 1,100 normalised images.
- **Comparison with the estimate.** The "1–3 minutes" estimate of the risks section holds.

## Verification (task 7.1, 2026-10-04/05)

**Gates.**
- **Build:** a Release build with no warnings; `dotnet format --verify-no-changes` is clean once the
  private fields use the `_` prefix; no OpenAPI contract drift.
- **Frontend:** `typecheck`, ESLint and Prettier on `e2e/` pass, after reformatting 4 renamed specs.
- **Backend tests:** 2,544 of 2,544 pass. Line coverage is 96.57 % overall, and every file of the
  change is above 80 %:
  - `SyntheticPeople` 99 %, `SyntheticImages` 97 %, `RegistrySeeder` 97.5 %, `OrderSeeder` 96 %,
    `FullOrderPlan` 97 %, the painter 100 %;
  - `NotificationSeeder` stays at 76 %; this change only edits one of its comments.
- **E2E:** Playwright ran against an isolated compose stack seeded with `Scenarios`, with the web on
  18080 and Mailpit on 18025. 289 passed, 14 skipped by design, 1 failed. The failure was
  `notifications.spec.ts`, whose `docker compose run … send-notifications` targets the default compose
  project. Rerun with `COMPOSE_PROJECT_NAME`, `COMPOSE_FILE` and `COMPOSE_ENV_FILES` pointing at the
  isolated stack, it passed (276 tests run with its dependencies).
- **Full seed in Docker:** an isolated project created 449 arquebusiers, 1,086 photos, 34 orders,
  763 entries and 28 loans in 1 min 28 s, with no warning.
- **Security grep:**
  - no "Sintétic" in seeders or E2E, apart from one historical comment in `SyntheticComparsas`;
  - no San Vicente comparsa name in the seed;
  - no DNI literal in seeders;
  - nothing under `seed-assets/` tracked, and no API key in the repository.

**Reviews.**
- Groups 3–6 were reviewed by `csharp-reviewer`, `security-reviewer`, `silent-failure-hunter`,
  `database-reviewer` and `typescript-reviewer`.
- There were no CRITICAL findings. Every HIGH and the relevant MEDIUM findings were fixed:
  - the dataset setting may no longer fall back silently;
  - faces outside the band or of another gender are counted, or fail;
  - the license mix kept for young adults;
  - the typeface is loaded from `SKData`;
  - the staging guards use the full ids as a superset;
  - a duplicate inside a plan fails;
  - skip reasons and summaries are logged;
  - full-dataset loans and missing editions fail;
  - offered models are read from the seeded ids;
  - the script swaps folders safely, caps downloads and keys its cache by prompt.
- `pr-test-analyzer` found gaps, now covered in `FullDatasetSeedTests`:
  - Full after Scenarios gives identical data;
  - the staging guard accepts its own full data and refuses a real row;
  - faces follow gender and band;
  - a stored license photo matches its own holder;
  - numeric ID ranges.

  The registry tests now seed without a dataset setting, which covers "scenarios by default".

**Follow-ups.**
- E2E coverage of the seeded photos and start-page figures on the full dataset (the CI E2E job seeds
  Scenarios).
- `sharp` stays a transitive dependency of the frontend (`@vite-pwa/assets-generator`).

## Maintainer review (tasks 2.6 and 7.2, 2026-10-05)

- **Images.** The maintainer approved the 19 emblems and a sample of the faces.
- **Application.** The maintainer approved a local stack reset and seeded with `Full` (screenshots
  reviewed, not committed):
  - the arquebusiers list at scale;
  - comparsas with emblems in both themes;
  - an arquebusier with a face and specimen license photos;
  - the orders dashboard with every status;
  - the start page.
- **Follow-up.** Minor style changes and fixes from a manual pass will come separately.

## Migration Plan

- **Local:**
  1. `docker compose down -v`.
  2. `docker compose --profile seed build api-seed`.
  3. `docker compose run --rm api-seed`.
- **Staging** (when it exists): reset, then seed with `Full`.
- **Rollback:** revert the change and reseed. No migration or production data is involved.

## Open Questions

- None.
