# Tasks

## 1. Research

- [x] 1.1 Check what designs D3, D5 and D6 rely on, with Context7 and `gh search code` for
  real-world use:
  - Bogus 35.6.x:
    - a local `Randomizer(seed)` and weighted picks;
    - the `es` locale's surname data;
    - its license (MIT) from the package and repository.
  - SkiaSharp 4.153.x:
    - `SKTypeface.FromStream` from an embedded resource;
    - `SKFont` plus `DrawText` with alignment;
    - rotated, semi-transparent text;
    - gradients;
    - PNG encoding on Linux with `NativeAssets.Linux.NoDependencies` and no system fonts.
  - sharp (the version in `frontend/node_modules`): `resize`, mozjpeg, palette PNG, raw pixel
    access for the flood fill.
  - MSBuild: copying a folder outside the project to the output (`None`/`Content` with `LinkBase`)
    and its flow to a referencing test project.

  Record versions and any workaround in design.md. Verify: design.md is updated, with no new open
  question.
- [x] 1.2 Check that DNI numbers 99,000,000–99,999,999 and NIE numbers Z9,000,000–Z9,999,999 are not
  issued (design D4), from official or well-sourced references, and record the sources in design.md.
  If either range is issued, stop and revise the spec and design with the maintainer before group 2.
  Verify: design.md states the result and its sources.

## 2. Generated images

- [x] 2.1 Turn `scripts/generate-seed-faces.mjs` into `scripts/generate-seed-images.mjs` (design D5):
  - `--kind faces|logos`;
  - raw outputs cached in `seed-assets/raw/` (git-ignored), with the 400 existing faces moved there;
  - the optimise step with sharp;
  - `--dry-run`;
  - the API key read from `RUNPOD_API_KEY` only and never logged.

  Verify: `node --check` passes, and a dry run lists 400 faces and 19 logo prompts.
- [x] 2.2 Optimise the 400 faces into `backend/synthetic-data/faces/` with their manifest. Verify:
  every file is a 600 × 800 JPEG, the folder is about 11 MB, and a sample checked by eye has no
  visible artefacts.
- [x] 2.3 Generate the 19 emblems (about USD 0.10) and optimise them into
  `backend/synthetic-data/logos/` with their manifest. Cruzados' emblem is dark; Abencerrajes has
  none. Verify: each PNG has a transparent background and a long side of 512, and Cruzados'
  luminance is below the dark-tile threshold.
- [x] 2.4 Write `backend/synthetic-data/NOTICE.md` (what, how, model license, MIT for the images, not
  personal data). Mark the folder as binary-safe in `.gitattributes` if needed. Verify: `git
  check-attr` shows the images as binary, and the notice matches design D5.
- [x] 2.5 Review the images in parallel with `security-reviewer` (no text, no real likeness, no
  metadata: EXIF stripped by sharp) and `typescript-reviewer` (the script). Fix CRITICAL/HIGH
  findings.
- [x] 2.6 The maintainer reviews the 19 emblems and a sample of faces by eye. Regenerate any emblem
  with text or a likeness to a real one with another seed. Verify: the maintainer's approval is noted
  in design.md.

## 3. Shared seeding infrastructure

- [x] 3.1 Write tests for the dataset setting (design D1):
  - `Scenarios` by default;
  - `Full` accepted, case-insensitively;
  - an unknown value makes `SeedCommand` exit 1 without running any seeder, logging the refusal.

  Then add `SeedSettings` (`Seed:Dataset`) and the validation. Verify: `SeedCommandTests` pass.
- [x] 3.2 Write unit tests for `SyntheticNationalIds` (design D4):
  - DNI and NIE builders with BR-01 check letters, validated by `NationalId.Parse`;
  - scenario numbers and population draws stay inside the 99M and Z9 ranges;
  - no repeats over 10,000 draws.

  Then implement it. Verify: the tests pass.
- [x] 3.3 Write unit tests for `SyntheticPeople` (design D3):
  - the same seed and dataset give the same people;
  - the gender matches the first name;
  - two surnames, except the single-surname NIE cases, and no "Sintétic";
  - ages 16–75 with the band weights;
  - minors hold no issued license, and an issued license starts at or after 18;
  - emails pass `InputFields.IsPlainEmail`, are lower-case and unique;
  - phones match the registry phone rule;
  - the license, status and course mix;
  - weapons match the side;
  - the photo mix;
  - the FiringChiefs, one per added comparsa;
  - the maintainer's surnames never appear.

  Then add `Bogus` (Directory.Packages.props) and implement the generator with the curated lists.
  Verify: the tests pass, with ≥ 80 % coverage of `SharedKernel/Seeding`.
- [x] 3.4 Copy `backend/synthetic-data/**` to the API output as `SyntheticData/` (design D5). Write
  tests for `SyntheticImages`:
  - manifests load from the output;
  - a file name with `..`, a separator or a rooted path is rejected;
  - an unknown gender or band names the manifest;
  - a missing folder fails naming it;
  - faces are matched by gender and band, `UNSPECIFIED` takes any gender, and there is no early
    reuse;
  - logos are matched by comparsa.

  Then implement it. Verify: the tests pass, and the published API folder holds the images.
- [x] 3.5 Move Geist Regular, Geist Bold and their OFL text from `PolvorApp.Exports/Fonts` to
  `PolvorApp.SharedKernel/Fonts` behind `EmbeddedFonts`, and register QuestPDF's fonts from there
  (design D6). Verify: the export and badge golden tests pass unchanged, and the licence file is
  still copied to the build output.
- [x] 3.6 Write tests for `ISpecimenCardPainter` / `SkiaSpecimenCardPainter` (design D6):
  - front and back are 1000 × 630 PNGs that pass `PhotoStorage` license rules through the normaliser;
  - the strings drawn, recorded through a test seam, include the national ID, the name, the dates,
    the type and "MUESTRA – SIN VALIDEZ";
  - no system font is needed.

  Then implement it in `PolvorApp.Api/Platform/Images` and register it. Verify: the tests pass.
- [x] 3.7 Review group 3 in parallel:
  - `csharp-reviewer`;
  - `security-reviewer`: manifest path handling, no real data;
  - `silent-failure-hunter`: a missing image folder, a missing font.

  Fix CRITICAL/HIGH findings.

## 4. Identity, catalogue and registry seeders

- [x] 4.1 Update `IdentitySeederTests`, then the identity seeder (design D2, D8):
  - the new display names, with the same ids, emails and states;
  - with `Full`, one active FiringChief per added comparsa;
  - the staging guard covers the Full ids.

  Verify: the identity seeder and hardening tests pass.
- [x] 4.2 Update `CatalogSeederTests`, then the catalogue seeder (spec "Synthetic catalogue data"):
  - the four scenario comparsas with their new names;
  - comparsas 5–20 and their assignments with `Full`;
  - generated emblems from `SyntheticImages` instead of `SyntheticLogos`, Abencerrajes without one
    and Cruzados dark;
  - the staging guard covers the Full ids.

  Delete `SyntheticLogos`. Verify: the catalogue tests pass, including new full-dataset tests.
- [x] 4.3 Update `RegistrySeederTests` for the re-skinned scenarios (design D2):
  - the D2 names and DNI/NIE;
  - every assertion on counts, warnings and photos kept;
  - ID photos are faces of the right gender;
  - license photos come from the painter at 1000 × 630.

  Then update the seeder and delete `SyntheticPhotos`. Verify: the registry seeder tests pass with
  the `Scenarios` dataset.
- [x] 4.4 Write the full-dataset registry checks (spec "Full population", "Seeded national ID ranges",
  "Faces match the person"):
  - 15–40 arquebusiers per added comparsa;
  - unique, in-range IDs;
  - no minor with an issued license;
  - weapons matching their side;
  - the photo mix;
  - faces matching, without early reuse;
  - determinism across two empty databases;
  - the staging guard accepting its own rows.

  Then implement the population, owned weapons and photos with the progress log. Verify: the tests
  pass and the run time is recorded in design.md.
- [x] 4.5 Review group 4 in parallel:
  - `csharp-reviewer`;
  - `database-reviewer`: unique indexes and check constraints against the generated values;
  - `security-reviewer`;
  - `silent-failure-hunter`: photo storage failures.

  Fix CRITICAL/HIGH findings.

## 5. Orders and other seeders

- [x] 5.1 Replace the "sintético" texts in the orders, distribution and editions seeders (design D2,
  D8): the history copy, the external owner, the guides, the day locations and the milestone titles.
  Update `OrderSeederTests` (the history DNI 99000091 and the external owner 99000092, still not
  registered), `DistributionSeederTests` and `EditionSeederTests`. Verify: those tests and
  `NotificationSeederTests` pass.
- [x] 5.2 Write the full-dataset order tests (spec "Full dataset orders", "Consistent entries",
  "First-year arquebusiers"):
  - a past `VALIDATED` order per added comparsa;
  - current statuses 4/5/2/3, two not prepared, with return reasons;
  - every generated entry and loan accepted by the API's entry and loan validation;
  - reserves carry nothing;
  - rentals offered and on the right side;
  - owners use their weapon;
  - first-year arquebusiers flagged;
  - the scenario orders unchanged;
  - the staging guard covers the Full ids.

  Then implement the orders (design D7). Verify: the tests pass.
- [x] 5.3 Review group 5 in parallel with `csharp-reviewer`, `database-reviewer` (bulk inserts, ids)
  and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 6. Tooling, E2E and docs

- [x] 6.1 Set `Seed__Dataset: ${SEED_DATASET:-Full}` on `api-seed` in `compose.yaml`, document
  `SEED_DATASET` in `.env.example`, and make CI seed with `SEED_DATASET=Scenarios`. Remove
  `seed-assets/` from `.gitignore` except `seed-assets/raw/`. Verify: `docker compose run --rm
  api-seed` seeds the full dataset locally, and the CI job's seed step uses Scenarios.
- [x] 6.2 Rename the seeded names in the E2E specs from the D2 tables:
  - the comparsas, users and arquebusiers;
  - the history-copy DNI line;
  - the badge PDF slug `hospitalarios`.

  Ids and flows stay unchanged. Verify: `rg -n "Sintétic" frontend/e2e` finds nothing, and the full
  Playwright suite passes against a stack seeded with Scenarios.
- [x] 6.3 Update the docs:
  - `docs/development.md`: the seed section with the datasets, the D2 tables, the population, the
    orders, the images and their script, resetting the database, and the Full-then-Scenarios
    refusal in staging;
  - `docs/compliance.md`: SEC-11 wording on invented names, generated faces and emblems, and
    specimen documents;
  - `docs/third-party-licenses.md`: Bogus (MIT, runtime), and the generated images under "Data
    files";
  - the `FileSlug` doc comment example.

  Verify: the docs match the specs, and nothing from `docs/sources/` appears.
- [x] 6.4 Review group 6 in parallel with `typescript-reviewer` (E2E) and `security-reviewer`
  (compose, CI, scripts). Fix CRITICAL/HIGH findings.

## 7. Verification

- [x] 7.1 Run `verification-loop`:
  - build, types and lint;
  - backend and frontend tests, with coverage of at least 80 % on `SharedKernel/Seeding`, the
    painter and the changed seeders;
  - a security grep: no "Sintétic" left in seeders or E2E, no real comparsa name of San Vicente
    in the seed, no DNI outside the 99M/Z9 ranges in seeders, no file under `seed-assets/raw/`
    tracked;
  - a diff review.

  Run `e2e-runner` and `pr-test-analyzer` on the change. Verify: the PASS report, with the findings
  and follow-ups recorded in design.md.
- [x] 7.2 Manual check on a local stack seeded with `Full`:
  - the arquebusiers list at scale (paging, filters);
  - the comparsas with their emblems in both themes;
  - an arquebusier's detail with a face and specimen license photos;
  - the orders dashboard with every status;
  - a returned order;
  - a badge sheet of a whole comparsa;
  - the start page warnings.

  Verify: screenshots are reviewed with the maintainer and are not committed.
