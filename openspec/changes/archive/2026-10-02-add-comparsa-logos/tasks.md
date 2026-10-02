# Tasks

## 1. Research

- [x] 1.1 Check with Context7 (and `gh` for real-world usage, if installed) the APIs used in design D1, D6 and D8:
  - EF Core 10: an optional owned type mapped to nullable columns on the owner's table, with a check constraint over the group, and a filtered unique index;
  - SkiaSharp 4: PNG encoding of an `Rgba8888`/`Premul` bitmap keeps alpha, and `SKCodec.Create` returns null for SVG content;
  - canvas `toBlob('image/png')` and `createImageBitmap` keep alpha through `drawImage` and rotations in Chromium, Firefox and WebKit;
  - ASP.NET Core rate limiting: a per-user fixed-window policy, configured like the existing ones.

  Record versions and any workaround in design.md. Verify: design.md updated, with no open question left.

## 2. Shared upload handling and synthetic PNGs

- [x] 2.1 Write unit tests for `SharedKernel/Http/ImageUploads`: non-multipart body gives `required`, missing `file` part gives `required`, `413` and an oversized part give `tooLarge`, a malformed body gives `required`, every `ImageRejection` maps to its reason. Then move the reading and mapping out of `PhotoEndpoints` (design D2) and make the registry call it. Verify: the new tests and every existing photo endpoint test pass unchanged.
- [x] 2.2 Write unit tests for `SharedKernel/Images/SyntheticPng`: RGB and RGBA output decode with SkiaSharp to the expected size, and a transparent pixel stays transparent. Then move the PNG writer out of the registry's `SyntheticPhotos`, add RGBA, and make the registry use it (design D9). Verify: the tests and the registry seeder tests pass.
- [x] 2.3 Add the `ImageUploads` rate-limit policy (design D6): per user, a fixed window, 20 per minute by default, read from `RateLimits:ImageUploads:PermitLimit`. Test hosts default it to 1000, like `PersonalDataWrites`. Add `RATE_LIMIT_IMAGE_UPLOADS_PER_MINUTE` to `compose.yaml` and `.env.example`, and document it in `docs/development.md`. The policy is only observable through an endpoint that uses it, so its throttling test (the third upload with a limit of 2 answers `429`, and another user is unaffected) lands with the logo endpoint in 4.2. Verify: the solution builds and the existing rate-limit tests pass.
- [x] 2.4 Review group 2 in parallel with `csharp-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 3. Catalogue: data model and logo rules

- [x] 3.1 Write unit tests for `LogoStorage.Rules` through `SkiaImageNormalizer`:
  - 255 px is rejected and 256 px accepted on the long side;
  - a ratio of 3.0 is accepted and 3.01 rejected;
  - 3000 × 1500 becomes 1024 × 512, and 300 × 300 is not scaled up;
  - the output is PNG, a transparent pixel stays transparent, and no metadata chunk remains;
  - SVG content is `unsupportedFormat`.

  Then add `LogoStorage` (design D3). Verify: the tests pass.
- [x] 3.2 Add the `ComparsaLogo` owned type on `Comparsa` and the `AddComparsaLogos` migration: nullable columns, the all-or-none check `ck_comparsas_logo_complete`, the key prefix check, positive dimensions and size, and the filtered unique index on `logo_object_key` (design D1). Add database tests that the constraints reject a partial logo and a key outside `catalog/logos/`. Verify: the database tests and `ModelDriftTests` pass, and the migration applies on a clean database.
- [x] 3.3 Review group 3 in parallel with `csharp-reviewer` and `database-reviewer`. Fix CRITICAL/HIGH findings.

## 4. Catalogue: logo service, endpoints, deletion and sweep

- [x] 4.1 Write integration tests (PostgreSQL and MinIO) for `ComparsaLogoAdministration`:
  - upload: the logo is stored, and name, side and active state are unchanged;
  - replace: the previous object is erased;
  - remove: the logo is cleared and the object erased;
  - an unknown id answers `404` and nothing is stored;
  - a comparsa deleted during the upload answers `404` and the new object is erased;
  - a failure before the commit erases the new object;
  - a name edit racing a logo upload keeps both changes;
  - two racing replacements leave one logo and no orphan once the erasures run.

  Then implement the service and `LogoObjects` (design D4). Verify: the tests pass.
- [x] 4.2 Write endpoint tests for `PUT|GET|DELETE /api/comparsas/{id}/logo`:
  - Admin: `200` with `ComparsaLogoResponse`, then `204`;
  - FiringChief: `403` on PUT and DELETE, including their own comparsa;
  - FiringChief GET: `200` `image/png` with `no-store` for their comparsa, and `404` for another;
  - signed out: `401`;
  - no logo: `404 logos.notFound`;
  - an unknown id answers `404` before the body is read;
  - each `file` reason (required, tooLarge, unsupportedFormat, tooSmall, aspectRatio);
  - an upload without the anti-forgery header answers `400 antiforgery.invalid`;
  - a busy normaliser answers `503 catalog.busy`;
  - with `RateLimits:ImageUploads:PermitLimit` at 2, an Admin's third upload within a minute answers `429`, and another Admin is unaffected;
  - the storage is down: `503 storage.unavailable` on PUT and GET, `204` on DELETE, and a comparsa edit still works;
  - `ComparsaResponse.logo` appears in the list and the detail;
  - the scope guard test lists the new route.

  Then add the endpoints, the problem codes and the response field (design D6). Verify: the tests pass.
- [x] 4.3 Write tests for the audit entries:
  - `ComparsaLogoUploaded` with `replaced` false, then true;
  - `ComparsaLogoRemoved`;
  - a rejected upload records nothing;
  - an entry never holds a key, a size or dimensions.

  Then record the entries (design D7). Verify: the tests pass.
- [x] 4.4 Write tests for comparsa deletion with a logo:
  - an unused comparsa is deleted, its logo object is erased, and `ComparsaDeleted` has `hadLogo: true`;
  - a referenced comparsa answers `409` and keeps its logo;
  - when the erasure fails, the deletion still succeeds and the object is left for the sweep.

  Then extend `ComparsaAdministration.DeleteAsync` (design D5). Verify: the tests pass.
- [x] 4.5 Write tests for `CatalogLogoOwner`: referenced keys are reported and unreferenced ones are not. Add a sweep test against PostgreSQL and MinIO: an orphan logo older than 1 h is erased, and a referenced one is kept. Then register the owner (design D5). Verify: the tests pass.
- [x] 4.6 Write integration tests for `ICatalogDirectory.ReadComparsaLogoAsync`:
  - it returns the PNG bytes and the dimensions;
  - it returns null without a logo or for an unknown comparsa;
  - it throws `StorageUnavailableException` when the storage is down.

  Then implement it (design D6). Verify: the tests pass.
- [x] 4.7 Regenerate `contracts/openapi.json` and the orval client. Check that the upload generates a `FormData` body, as for photos. Document the `catalog/logos/` prefix in `backend/src/Modules/README.md`. Verify: the contract check in CI passes locally and `npm run typecheck` passes.
- [x] 4.8 Review group 4 in parallel with `csharp-reviewer`, `security-reviewer`, `database-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 5. Catalogue: synthetic logos

- [x] 5.1 Write seeder tests:
  - every seeded comparsa but one has a logo that passes `LogoStorage.Rules`;
  - one generated logo is near-black on transparency;
  - a second run changes nothing;
  - a rerun after a missing object re-puts it.

  Then extend `CatalogSeeder` with the flat abstract shapes (design D9). Verify: the tests pass, and `docker compose run --rm api-seed` twice on a clean stack succeeds.
- [x] 5.2 Review group 5 with `csharp-reviewer`. Fix CRITICAL/HIGH findings.

## 6. Frontend: composites

- [x] 6.1 Write Vitest tests for `photo-image.ts` and `PhotoUpload` in PNG mode, with a mocked `photo-image` in jsdom:
  - `output: 'png'` hands a `image/png` blob to `onUpload`;
  - the default still hands a JPEG;
  - a free crop starts at the whole image, and confirming at once hands the whole image;
  - the transparency checkerboard is present in PNG mode only.

  Then add the `output` rule and rename `cropToJpeg` to `cropImage` (design D8). Add the "transparent logo" story. Verify: the tests, the existing `PhotoUpload` tests and the Storybook axe checks pass.
- [x] 6.2 Add the `--logo-tile` and `--logo-tile-foreground` tokens for both themes, and record them in `docs/design/tokens.md`. Write tests for `ComparsaLogo`:
  - the image has an empty `alt` by default;
  - without `src`, the placeholder shows;
  - `onError` swaps to the placeholder;
  - each size renders;
  - axe passes in both themes.

  Then implement it and its stories (with logo, dark transparent logo in the dark theme, placeholder, load failure), and register it in the catalogue test. Verify: the tests, `npm run lint` (no arbitrary values) and the Storybook axe checks pass.
- [x] 6.3 Write tests for the `AppLayout` sidebar cards:
  - the cards render under the mark in the given order, inside a named `nav`;
  - each card links to its route;
  - a card click closes the mobile drawer;
  - a long name truncates after two lines with the full name in `title`;
  - no cards render when the list is empty;
  - axe passes on the night surface in both themes.

  Then add `sidebarCards` (design D8) and a story. Verify: the tests and the existing `AppLayout` tests pass.
- [x] 6.4 Document `ComparsaLogo`, the PNG mode of `PhotoUpload` and the sidebar cards in `docs/design/README.md` and `docs/design/patterns.md`. Verify: the docs name the props and the stories.
- [x] 6.5 Review group 6 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 7. Frontend: screens and translations

- [x] 7.1 Write tests for `useFiringChiefComparsaCards` and `AppShell` with MSW:
  - a FiringChief with two active comparsas (one with a logo) gets two cards sorted by name;
  - an inactive comparsa gets no card;
  - an Admin gets no cards;
  - a failed list request leaves the sidebar with the mark and the navigation only.

  Then implement them (design D8). Verify: the tests pass.
- [x] 7.2 Write tests for the comparsas list: the logo before each name on desktop and mobile rows, the placeholder without a logo, and the logo URL carries `?v=<version>`. Then implement it. Verify: the tests pass.
- [x] 7.3 Write tests for the comparsa detail page with MSW multipart:
  - the record header shows the logo or the placeholder;
  - an Admin sees the logo section and uploads a PNG, then the header and the list query refresh;
  - the removal is confirmed, and cancelling keeps the logo;
  - a `400 file: tooSmall` and a `503` show their translated reasons;
  - a FiringChief sees no logo section.

  Then implement them (design D8). Verify: the tests pass.
- [x] 7.4 Add the `catalog` and `ui` keys of design D8 to es-ES, ca-ES-valencia and en. Verify: the translation completeness test passes, and the screens show no raw keys in the three languages.
- [x] 7.5 Review group 7 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 8. End-to-end and documentation

- [x] 8.1 Write `e2e/comparsa-logos.spec.ts` (design D10):
  - an Admin uploads a generated transparent PNG, and the list and the header show it;
  - an Admin removes it after confirming;
  - the seeded FiringChief sees two sidebar cards (a logo and the placeholder) and no logo actions;
  - the FiringChief gets `404` on another comparsa's logo URL;
  - axe runs on the sidebar in both themes, and in the 360 px drawer.

  Raise `RATE_LIMIT_IMAGE_UPLOADS_PER_MINUTE` for the CI E2E run if needed. Verify: the spec passes in the compose stack on Chromium, Firefox and WebKit.
- [x] 8.2 Update `docs/data-model.md` (logo rules: formats, 256 px, ratio 3, PNG with transparency, 1024 px, Admin-only), `docs/glossary.md` (comparsa logo → `logo`) and `docs/mvp.md` (status of #6b). Verify: the documents match the specs, and no real logo or comparsa name appears.
- [x] 8.3 Run `verification-loop`: build, types, lint, backend and frontend tests with coverage of at least 80 % on the new catalogue code, a security grep (no keys or file names in logs, no committed brand assets) and a diff review. Run `e2e-runner` and `pr-test-analyzer` on the change. Verify: the PASS report, with the findings and follow-ups recorded in design.md.
