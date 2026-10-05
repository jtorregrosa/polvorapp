# Proposal

## Why

The synthetic seed was built to cover cases, not to look real:
- four "Comparsa Sintética …" comparsas;
- 14 "Arcabucero Sintético …" arquebusiers;
- flat-shape photos and logos;
- license cards made of stripes;
- four orders.

That is enough for tests, but not for demos, for the maintainer's own manual checks or for judging
the screens at real scale (~20 comparsas, ~800 arquebusiers, `docs/vision.md`). Lists of 14 rows
and four orders hide layout, paging, dashboard and badge-sheet problems. "Sintético" names make
every screen read as a mock-up.

Now that the MVP sequence is complete (#16 `add-badges`), the seed should look like the festival it
serves. Everything in it stays invented or generated: no real person, comparsa, emblem or document
(SEC-11, NFR-13, NFR-14).

Capability: none new. It changes the seed requirements of `platform`, `federation-catalog`,
`arquebusier-registry` and `comparsa-orders`. It is a change outside the `docs/mvp.md` sequence
(maintainer decision 2026-10-04). It relies on:
- **SEC-11 / NFR-13 / NFR-14**: no real personal data in the repository or in non-production
  environments; the repository is public.
- **ADR-0013**: no logo or brand asset of another organisation is committed. The logos here are
  generated, not copied.
- **ADR-0005** and **NFR-15**: photos go through the same normaliser as uploads, and ID photos are
  3:4 and at least 600 × 800 px.
- **BR-01** (valid DNI/NIE), **BR-04** (compliance checks are warnings), **BR-05** (reserves carry
  nothing), **BR-07** (only offered models are rented), **BR-09** (loans) and **BR-12**
  (FiringChief scope).
- **ADR-0010**: every new dependency and data file is registered with its license.

## What Changes

- **Invented but plausible comparsas.** Twenty comparsas get festival-style names, none of them a
  comparsa of San Vicente del Raspeig. The four scenario comparsas keep their fixed ids and roles:
  - Cruzados (Christian, two FiringChiefs, dark logo);
  - Abencerrajes (Moorish, no logo);
  - Hospitalarios (Christian);
  - Zegríes (Moorish, inactive).
- **Two datasets**, chosen with a seed setting:
  - *scenarios* (default; backend tests and CI E2E): today's cases, the same in shape and count,
    with realistic names and images;
  - *full* (local environment and staging): the scenarios plus 16 more comparsas, each with:
    - one FiringChief;
    - 15–40 arquebusiers with believable, internally consistent data (about 480 in all);
    - a past-edition order and, for most, a current-edition order.
- **Believable people.** First names and two surnames come from curated Alicante frequency lists
  that mix Spanish and Valencian forms; Bogus is the deterministic generator. The data is
  consistent:
  - the gender matches the name;
  - emails are derived from the name, on `polvorapp.example`;
  - phones are Spanish mobiles;
  - ages are consistent with the license and course dates;
  - weapons match the comparsa's side.

  Every value stays deterministic from `SyntheticData.RandomSeed`.
- **DNI/NIE** have valid check letters (BR-01) and come from ranges with no evidence of
  use (Spain has no reserved fictional range) instead of today's 1–99, which are reserved for
  historical and royal holders:
  `99xxxxxx` DNIs and `Z9xxxxxx` NIEs. They never collide with the ranges the tests use for their
  own rows.
- **Users** keep their emails, which are the sign-in fixtures, but get realistic names.
- **Generated images, committed:**
  - **ID photos:** 400 faces of people who do not exist, generated with Z-Image Turbo (Tongyi-MAI,
    Apache-2.0) and optimised to 600 × 800 JPEG (about 11 MB). They are matched to each
    arquebusier's gender and age.
  - **Comparsa logos:** 20 heraldic emblems without text, also generated, with motifs of the
    comparsa's side and a transparent background.
  - **Script:** both sets come from `scripts/generate-seed-images.mjs` and live in
    `backend/synthetic-data/` with a manifest and a notice.
  - **Removed:** the flat-shape placeholders.
- **License photos drawn as specimens.** Both sides follow the layout of the Spanish arms license.
  They are filled from the arquebusier's own data and carry a visible "MUESTRA – SIN VALIDEZ"
  watermark. They have no coat of arms, flag emblem or EU stars.
- **Orders for the full dataset**, consistent with each arquebusier:
  - **Past edition:** a `VALIDATED` order per added comparsa.
  - **Current edition:** a mix of `DRAFT`, `SUBMITTED`, `RETURNED` and `VALIDATED`, with two
    comparsas not prepared.
  - **Entries:** reserves carry nothing; owners use their weapon; others rent an offered model or
    borrow a team-mate's weapon. Nothing breaks a blocking rule.
  - **Scenario orders:** unchanged.
- **Other seeded texts** that say "sintético" (the orders' history copy and external owner, the
  distribution places, the milestone titles, the ownership guides) become ordinary invented texts.
- **BREAKING (local data only):** seeded rows are never refreshed, so an existing local database
  keeps the old values. Reset it (`docker compose down -v`) and seed again.

## Non-goals

- Distribution slots or proxies, or notification data, for the added comparsas.
- Any real comparsa name, emblem, person or document, and any image downloaded from a third party.
- Changing what the application does with seeded data: no screen, endpoint, migration or
  translation changes.
- Importing real comparsa data; production data is still entered by Admins or imported
  (`add-registry-import`).
- Making the scenarios dataset bigger or changing the cases it covers.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `platform`: requirement "Guarded synthetic seed". It adds the scenarios and full datasets.
- `federation-catalog`: requirement "Synthetic catalogue data". It brings plausible invented
  names, generated emblem logos instead of flat shapes, and the extra comparsas and FiringChiefs of
  the full dataset.
- `arquebusier-registry`: requirement "Synthetic registry data". It brings realistic, consistent
  people, seeded DNI/NIE ranges, generated faces, drawn specimen license photos and the full
  population.
- `comparsa-orders`: requirement "Synthetic order data". It adds the orders of the full dataset and
  invented texts.

## Impact

- **Backend:**
  - `SharedKernel/Seeding`: the dataset setting, the people generator (Bogus plus curated name
    lists), the seeded DNI/NIE ranges and the reader of the committed images.
  - `IdentityAccess`: realistic names for the seeded users, and one FiringChief per added comparsa.
  - `FederationCatalog`: the invented names, the extra comparsas and assignments, and the generated
    emblems.
  - `ArquebusierRegistry`: the re-skinned scenarios, the population, faces and drawn license cards.
  - `ComparsaOrders`: the full-dataset orders and invented texts. `Distribution` and
    `FestivalEditions`: invented texts only.
  - `PolvorApp.Api/Platform/Images`: a license-card painter (SkiaSharp is already a dependency of
    the host). The Geist font files move to `SharedKernel`, so Exports and the painter share one
    copy.
  - `backend/synthetic-data/`: about 12 MB of generated images, copied to the build output.
  - New dependency: `Bogus` (MIT).
- **Tooling:**
  - `scripts/generate-seed-images.mjs` replaces `generate-seed-faces.mjs`. It has faces and logos
    modes, an optimise step and needs `RUNPOD_API_KEY`.
  - `compose.yaml` passes the dataset, and `.env.example` documents `SEED_DATASET`.
  - CI seeds the scenarios dataset.
- **Tests:**
  - The backend seeder tests and the E2E specs move from "Sintético" names to the new scenario
    names and DNIs.
  - New tests for the full dataset, the image reader and the license painter.
- **Docs:**
  - `docs/development.md` (the seed section);
  - `docs/compliance.md` (SEC-11 wording);
  - `docs/third-party-licenses.md` (Bogus, and the generated images as data files);
  - `.env.example`.
- **Security and GDPR.** No real person or organisation is represented:
  - Names are frequency-list combinations. A match with a real person is coincidental and never
    linked to real identity data.
  - The DNI/NIE ranges show no evidence of use and emails use a reserved domain.
  - The faces belong to nobody and the emblems are invented.
  - The license images are watermarked specimens.
  - Seeding still writes no audit entries.
