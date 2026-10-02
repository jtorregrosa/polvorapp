# Proposal

## Why

Comparsas are recognised by their emblem far more than by their name. On festival day, FiringChiefs
and the Federation identify lists, flasks and badges by it, but PolvorApp only knows the name and
the side (#4). The catalogue change left the logo out because it needed the private storage and the
image pipeline that #6 built (`add-federation-catalog` proposal, maintainer decision). That pipeline
now exists. The logo also has to be in place before the documents that will print it: the
distribution lists (#13) and other PDFs.

Capability (from `docs/mvp.md`): **`federation-catalog`**, change #6b of the sequence. It completes
the catalogue part of **UC-24** with the comparsa `logo` from `docs/data-model.md`. It keeps
**BR-12** (scope) for reads and **SEC-05** (audit) for writes. It applies **ADR-0005** (private
S3-compatible storage, server re-encoding) and keeps **ADR-0012/ADR-0013** (real logos are
third-party brand assets and are never committed). It follows ADR-0001, ADR-0002, ADR-0006,
ADR-0007, ADR-0009 and ADR-0011.

## What Changes

- **One optional logo per comparsa** (`logo` in `docs/data-model.md`). An Admin uploads, replaces
  and removes it from the comparsa detail page. FiringChiefs read it but cannot change it (`403`).
  A comparsa without a logo shows a neutral placeholder.
- **Server-side processing** (ADR-0005). These rules are blocking (`400`):
  - only JPEG, PNG or WebP images of at most 10 MB are accepted;
  - the image must be at least 256 px on its long side, and its long side at most 3 times its
    short side.

  The server decodes the image, re-encodes it as PNG with its **transparency kept**, strips every
  metadata, and scales it down to at most 1024 px. SVG is not accepted.
- **Reading follows the comparsa scope** (BR-12). The API streams the logo after the same check as
  the comparsa: a logo outside the user's scope answers `404`. The browser never talks to the
  storage.
- **Where it shows**:
  - the comparsas list, next to each name;
  - the comparsa detail page header;
  - the **sidebar** for a FiringChief: under the PolvorApp mark, a card for each of their active
    comparsas, with the logo and the name, sorted by name, linking to the comparsa (maintainer
    decision). Admins see no card;
  - other modules can read a comparsa's logo through the catalogue contract, so later PDFs can
    print it.

  The logo stays visible in both themes, including dark logos with transparency in the dark theme.
- **Cleanup**: replacing or removing a logo and deleting a comparsa erase the stored file once the
  change is committed. The platform's periodic sweep (#6) also covers logos. Deactivating a
  comparsa keeps its logo.
- **Audit** (SEC-05): every upload, replacement and removal is recorded, without image data or
  storage keys.
- **UI**: the `PhotoUpload` composite learns to keep transparency: it decodes, crops and uploads a
  PNG when the screen asks for it. Free crop, and the whole image is selected by default. Every
  text is in es-ES, ca-ES-valencia and en.
- **Synthetic seed** (SEC-11): generated abstract emblems (flat geometric shapes with a transparent
  background, no text) for most seeded comparsas. One stays without a logo.

## Non-goals

- Any PDF that prints the logo: badges (#16) show the Federation's coat of arms, not the comparsa
  logo, and distribution lists come in #13. This change only makes the logo available to them.
- SVG or vector logos. SVG can carry scripts, and the image pipeline only handles raster images. A
  PNG of at least 256 px is enough for screens and for print at the sizes planned.
- FiringChiefs uploading their own comparsa's logo. The data model makes it an Admin task.
- The Federation's own logo or coat of arms, which is supplied at deployment time (ADR-0012,
  ADR-0013).
- Logo history, several logos per comparsa, or per-edition logos.
- Thumbnails or image variants: one stored PNG per logo, scaled by the browser.
- A comparsa switcher in the sidebar. The cards are links, not a context selector.

## Capabilities

### New Capabilities
<!-- None: the logo belongs to the existing federation-catalog capability (docs/mvp.md #6b). -->

### Modified Capabilities
- `federation-catalog`: new requirements "Comparsa logos", "Logo validation and processing",
  "Logo access (BR-12)" and "Logo display". Modified requirements: "Deleting comparsas and weapon
  models" (the logo is erased), "Catalogue changes are audited" (logo writes) and "Synthetic
  catalogue data" (synthetic logos).
- `platform`: "Application shell" (the sidebar shows a FiringChief's comparsas with their logos).
- `design-system`: "Photo upload with cropping" (it can keep transparency and upload a PNG, and a
  free crop starts with the whole image).

## Impact

- **Backend**:
  - The `FederationCatalog` module gets logo columns on `catalog.comparsas` and a migration, a
    `ComparsaLogoAdministration` service, logo endpoints and a stored-file owner for the
    `catalog/logos/` prefix. Comparsa deletion and the seeder are extended.
  - `FederationCatalog.Contracts`: `ICatalogDirectory` can read a comparsa's logo.
  - `SharedKernel/Images`: the small PNG writer the registry seed uses moves here, with
    transparency, so both seeders use it. Images are re-encoded with the existing
    `IImageNormalizer` (PNG output is already supported). No new dependency.
- **API**:
  - `PUT|GET|DELETE /api/comparsas/{id}/logo`. Uploads are `multipart/form-data`.
  - `ComparsaResponse` gains `logo` (version, width, height, upload time, or null).
  - `contracts/openapi.json` and the orval client are regenerated.
- **Frontend**:
  - `PhotoUpload` and `photo-image.ts` get a PNG output mode;
  - a new `ComparsaLogo` composite shows a logo or its placeholder on a fixed light tile;
  - `AppLayout` gets a sidebar slot, which `AppShell` fills for FiringChiefs;
  - the comparsas list and detail pages change;
  - the `catalog` and `ui` namespaces change in the three locales.
- **Docs**:
  - `docs/data-model.md`: the logo rules;
  - `docs/design/README.md` and `docs/design/patterns.md`: `ComparsaLogo` and the sidebar cards;
  - `backend/src/Modules/README.md`: the catalogue's storage prefix;
  - `docs/mvp.md`: the status of this change.
- **ADRs**: none new. The change applies ADR-0005 the same way #6 did.
