# Design

## Context

See `proposal.md` for the motivation, and the delta specs (`federation-catalog`, `platform`,
`design-system`) for the behaviour. The current state that shapes this design:

- **The #6 pipeline is generic and ready.**
  - `IObjectStorage` (put, get, delete, list) and `S3ObjectStorage`, with a private bucket created by
    `migrate`.
  - `IImageNormalizer` and `SkiaImageNormalizer`. They already support
    `ImageOutputFormat.Png`, which clears the canvas to transparent instead of white, and the
    `MaxSideRatio` and `MinLongSide` rules.
  - `IStoredObjectOwner` and the hourly `StoredObjectSweeper`. Owners are skipped when their
    prefixes overlap, and keys must follow `<module>/<collection>/<name>.<ext>`.
  - A platform semaphore allows one normalisation at a time. `ImageProcessingBusyException` is
    raised after a 30 s wait.
- **Registry photo code to reuse.**
  - `PhotoEndpoints` reads a `multipart/form-data` body in memory and maps form failures and
    `ImageRejection` values to the `file` field reasons. It calls `.DisableAntiforgery()`, because
    the platform middleware already checks `X-XSRF-TOKEN`.
  - `PhotoObjects` erases keys after the commit, with a 10 s budget, and leaves failures to the
    sweep.
  - `SyntheticPhotos` writes plain PNGs with no image library, as RGB only.
- **Catalogue module (#4).**
  - `Comparsa` has `Name`, `Side`, `Active` and `CreatedAt`, and no version: "the last save wins"
    (#4 design D9).
  - Changes lock the row first (`LockComparsaForChangeAsync`, and `LockComparsaForDeleteAsync` for
    deletion). They audit with `IAuditTrail.Record` before `SaveChangesAsync`, and map outcomes to
    `CatalogProblems` codes, which the UI translates as `catalog:errors.<code>`.
  - Reads go through `IComparsaScope.Filter`. Writes are in an `adminOnly` group.
  - Catalogue endpoints have no rate limit today.
  - `ICatalogDirectory` is the read contract for other modules, and applies no scope.
- **Frontend.**
  - `PhotoUpload` takes `PhotoRules`: `aspect`, `maxSideRatio`, the minimum and maximum sizes. It
    always exports JPEG, and `photo-image.ts` also keeps its working copy (decode and rotate) as
    JPEG, which drops transparency. A free crop already starts at 100 %.
  - `RecordHeader` has a `media` slot, which the arquebusier ID photo uses.
  - `AppLayout` renders the mark link in `SidebarHeader`. Feature code may use only
    `components/app/` (ADR-0009, enforced by lint), so features cannot reach `useSidebar` to close
    the mobile drawer.
  - `AppShell` already imports from `features/identity-access`.

## Goals / Non-Goals

**Goals:**
- Reuse the #6 storage, normaliser, sweep and upload handling without changing their behaviour for
  photos. Shared upload code moves to the SharedKernel instead of being copied.
- Keep the logo visible on any background: light tile in both themes and on the night sidebar.
- Expose the logo to later document generators (#13, the exports in #12) through the catalogue
  contract, so they never touch catalogue tables or storage keys.

**Non-Goals:**
- No pre-signed URLs, no thumbnails and no image variants (as in #6).
- No caching of logo responses: the platform sends `no-store` on every API response. About 20
  small PNGs per page are cheap at this scale (NFR-05).
- No change to the photo rate limit. See "Follow-ups".

## Decisions

### D1. Data model: logo columns on `catalog.comparsas`

The migration `AddComparsaLogos` adds nullable columns:

| Column | Type | Notes |
|---|---|---|
| `logo_id` | uuid | UUIDv7; also the object name and the API `version` |
| `logo_object_key` | varchar(200) | unique filtered index `ix_comparsas_logo_object_key`; check `ck_comparsas_logo_key`: equals `'catalog/logos/' \|\| logo_id \|\| '.png'` (database review: ties the key to the id and to the prefix the sweep owns) |
| `logo_width`, `logo_height` | int | checked `> 0` |
| `logo_size_bytes` | int | checked `> 0` |
| `logo_uploaded_at` | timestamptz | |

A check constraint `ck_comparsas_logo_complete` makes the six columns either all null or all set,
and `ck_comparsas_logo_size` keeps the dimensions and the size positive.
In the model they map to an owned `ComparsaLogo` value object (`Comparsa.Logo`, nullable) with
init-only properties.

*Why columns, not a table as for photos*: photos got their own table so that a photo write never
changes the arquebusier's `xmin` version (#6 D4). Comparsas have no version, and EF updates only
the modified columns. So a name edit and a logo upload on the same row never overwrite each
other, and the row lock serialises them. One logo per comparsa is a 1:0..1 relation, so a table
would add a join to every list for nothing.

`ModelDriftTests` already covers the catalogue context.

### D2. Shared upload handling (`SharedKernel/Http/ImageUploads`)

The multipart reading in `PhotoEndpoints` moves to a shared helper, with no change in behaviour:
- `ImageUploads.ReadFileAsync(request, maxBytes, logger, ct)` returns `(IFormFile?, reason?)`. It
  reads the form in memory, and `413`, `InvalidDataException` and `BadHttpRequestException` become
  `tooLarge` or `required`;
- `ImageUploads.Reason(ImageRejection)` maps a rejection to its field reason;
- `ImageUploads.MaxRequestBytes(maxBytes)` adds the multipart framing (64 KB).

The registry's photo endpoints call it, and their existing tests prove the move changed nothing.
The logo endpoint uses the same helper.

*Alternative considered*: copying the 60 lines into the catalogue. Rejected: the two copies would
drift, as the group 4 review notes of #6 show (the `413` handling was fixed late).

### D3. Logo rules and storage (`LogoStorage`)

```csharp
Prefix = "catalog/logos/"; ContentType = "image/png"; MaxUploadBytes = 10 MB;
Rules = new ImageRules {
    MaxInputBytes = MaxUploadBytes, MaxInputPixels = 40_000_000,
    MinLongSide = 256, MaxSideRatio = 3, MaxWidth = 1024, MaxHeight = 1024,
    Output = ImageOutputFormat.Png };
```

- **256 px minimum long side**: enough for a 64 px tile at 4× density, and for about 22 mm at
  300 dpi on a printed list. Logos taken from comparsa websites are usually at least this size.
- **A factor of 3 between the sides**: allows wide or tall emblems, and rejects banners that
  would be unreadable in a square tile.
- **1024 px maximum**: a 1024 × 1024 RGBA PNG is at most a few hundred KB for flat logos. It is
  never scaled up.
- **The normaliser is unchanged.** Orientation, metadata stripping, the pixel cap before decoding
  and the concurrency slot all apply as for photos (#6 D3).

### D4. Upload, replace and remove flow (`ComparsaLogoAdministration`)

`UploadAsync(id, stream)`:
1. **Existence check** without a lock (`ComparsaExists`). An unknown id answers `404` before the
   body is read: no CPU work, nothing stored. Only Admins reach this endpoint, so there is no
   scope to check.
2. **Normalise** (D3). A rejection answers `400`, with no storage write and no audit entry. A busy
   normaliser answers `503 catalog.busy`.
3. **Put** `catalog/logos/{newId}.png`. A storage outage answers `503 storage.unavailable`.
4. **Transaction**:
   1. `LockComparsaForChangeAsync(id)` (existing). If the comparsa was deleted meanwhile, answer
      `404`, and the new key is erased.
   2. Remember the previous key. Set the new `ComparsaLogo`.
   3. Audit (D7). Commit.
5. **After the commit**: erase the previous key with `LogoObjects.DeleteAsync`. It is best-effort,
   uses the 10 s budget and leaves failures to the sweep. If step 4 fails before the commit, erase
   the new key at once. If it fails during the commit, whose outcome is unknown, log it and leave
   the new key to the sweep. This is the same rule as #6 D6, so there is never a reference
   without an image.

`RemoveAsync(id)`: lock, `404 logos.notFound` when there is no logo, clear it, audit, commit, then
erase the key. During a storage outage the removal still answers `204`, and the sweep erases the
image later (#6 group 7 note).

`OpenAsync(id)`: a scoped query (`IComparsaScope.Filter`) for the logo reference, then
`IObjectStorage.GetAsync`. A missing object re-reads the reference once (a replacement may be
racing). It is then logged at Error with ids only and answered `404 logos.notFound`.

Two concurrent replacements serialise on the row lock. The last one wins, and each erases the key
it replaced, so no image is left behind.

`LogoObjects` is the catalogue's copy of `PhotoObjects`, under 40 lines. It stays in the module:
the log message names the module's collection, and a shared version would need a "what kind of
file" parameter for one log line. If a third module needs it, it moves to the SharedKernel.

### D5. Comparsa deletion and the sweep

- **`ComparsaAdministration.DeleteAsync`**: under the existing `FOR UPDATE` lock, read
  `Logo?.ObjectKey`. Add `hadLogo` to the `ComparsaDeleted` audit data. After the commit, erase
  the key with `LogoObjects`. The logo is a column of the deleted row, so it never counts as a
  usage (`ICatalogUsage`) and never blocks a deletion.
- **`CatalogLogoOwner : IStoredObjectOwner`**: prefix `catalog/logos/`. One query
  `WHERE logo_object_key = ANY(@keys)` per page. It is registered in `FederationCatalogModule`.
  The sweeper's rules are unchanged: 1 h grace, never delete on doubt, and refuse a mass
  deletion.

### D6. API endpoints and contract

| Method | Path | Access | Notes |
|---|---|---|---|
| PUT | `/comparsas/{id}/logo` | Admin | `multipart/form-data` with one part, `file`. 200 `ComparsaLogoResponse {version, width, height, uploadedAt}`. Request limit 10 MB + 64 KB. `.DisableAntiforgery()` (platform check, as #6 D5). Rate limit `ImageUploads` |
| GET | `/comparsas/{id}/logo` | signed in, scoped | Streams `image/png` with `Content-Length`, `Content-Disposition: inline; filename="logo.png"` and `nosniff` (platform). 404 `comparsas.notFound` or `logos.notFound` |
| DELETE | `/comparsas/{id}/logo` | Admin | 204 |

- **`ComparsaResponse`** gains `Logo` (`ComparsaLogoResponse?`), used in the list and the detail.
  The UI builds the image URL `/api/comparsas/{id}/logo?v={logo.version}`, so `<img>` reloads
  when the logo changes.
- **Problem codes** (`CatalogProblems`, translated as `catalog:errors.*`):

  | Situation | Response | `code` |
  |---|---|---|
  | file missing, too large, wrong format, too small, too elongated | 400 | `validation`, `errors{file: required \| tooLarge \| unsupportedFormat \| tooSmall \| aspectRatio}` |
  | comparsa unknown, or out of scope on GET | 404 | `comparsas.notFound` (existing) |
  | no logo (GET, DELETE) | 404 | `logos.notFound` |
  | FiringChief on PUT or DELETE | 403 | (authorisation policy) |
  | storage unreachable | 503 | `storage.unavailable` |
  | normaliser busy | 503 | `catalog.busy` |

- **Rate limit `ImageUploads`** (new platform policy in `RateLimitPolicies`): a per-user fixed
  window, 20 per minute by default, `RateLimits:ImageUploads:PermitLimit`. The compose stack
  exposes it as `RATE_LIMIT_IMAGE_UPLOADS_PER_MINUTE`, so E2E can raise it. The catalogue had no
  limit because its writes were cheap, but a logo upload costs a decode.
- **`ICatalogDirectory.ReadComparsaLogoAsync(comparsaId, ct)`** returns
  `ComparsaLogoImage(ReadOnlyMemory<byte> Png, int Width, int Height)?`. It is null when the
  comparsa or its logo does not exist. It throws `StorageUnavailableException` during an outage,
  and the caller decides: a PDF can fall back to the placeholder. Like the rest of the directory,
  it applies no scope. It returns bytes rather than a stream, because document generators embed
  the whole image anyway and a logo is small. Its tests are integration tests against MinIO. No
  production caller exists until #12 or #13. The cost is one method, and the outcome in
  `docs/mvp.md` asks for it.

### D7. Audit entries

The entity type is `Comparsa`, the entity id and `ComparsaId` are the comparsa id, and the actor
is the Admin.

| Action | Data |
|---|---|
| `ComparsaLogoUploaded` | `{replaced: bool}` |
| `ComparsaLogoRemoved` | none |
| `ComparsaDeleted` | existing snapshot, plus `hadLogo` |

There is no image data, key, size or dimension, for the same reasons as #6 D8. Reading a logo is
not audited.

### D8. Frontend

**`PhotoUpload` and `photo-image.ts`: PNG mode.** `PhotoRules` gains `output?: 'jpeg' | 'png'`,
with `jpeg` as the default, so photos are unchanged.
- In `png` mode, `loadImage`, `rotateImage` and the crop export render with `image/png`, so the
  working copy keeps its alpha channel. The crop export is renamed from `cropToJpeg` to
  `cropImage(…, type)`. The existing fallback rule "never upload something else" stays.
- The preview and the crop area show a checkerboard behind transparent pixels. It is a CSS
  background built from tokens, so the user sees what is transparent.
- The file picker is unchanged (no `capture` attribute), so a phone user can also photograph a
  printed emblem.
- Stories: "transparent logo" (a generated synthetic PNG). The catalogue test registers it.

**`ComparsaLogo` composite** (`src/components/app/ComparsaLogo.tsx`).
- Props: `src: string | null`, `size: 'sm' | 'md' | 'lg'` (32, 40 and 64 px), and `alt` (default
  `""`, decorative, because the name is always next to it).
- It renders a square tile with the new tokens `--logo-tile` and `--logo-tile-foreground`. They
  are the same light neutral surface and a muted foreground in both themes, recorded in
  `docs/design/tokens.md`. The tile has a 1 px border and `rounded-md`. The image uses
  `object-contain` with inner padding, so the logo keeps its shape.
- With no `src`, or when `<img onError>` fires, it shows the placeholder: a lucide `Flag` icon in
  `--logo-tile-foreground`. There is no broken image.
- Stories: with logo, transparent dark logo in the dark theme, placeholder and load failure, with
  axe checks.

**`AppLayout` sidebar cards.**
- New prop `sidebarCards?: readonly SidebarCard[]` (`{ to, label, media: ReactNode }`).
- They are rendered in `SidebarHeader` under the mark, as a list of links inside
  `<nav aria-label={t('ui:nav.comparsas')}>`.
- Each card is a `Link` with the media and the name. The name wraps to at most two lines, then
  truncates with the full name in `title`. The card has a visible focus ring on the night
  surface and a minimum target of 44 px.
- A click closes the mobile drawer, as `NavigationMenu` does. The composite owns `useSidebar`, so
  feature code never imports `components/ui`.

**`AppShell`.**
- `useFiringChiefComparsaCards()` lives in `features/federation-catalog/components/`. For a
  FiringChief it runs `useListComparsas({})`: active only, scoped by the server, sorted by name.
  It returns the cards with `<ComparsaLogo size="md">`.
- For Admins, and while loading or on error, it returns an empty list: the sidebar stays as
  today. An error is not shown in the sidebar. The comparsas page shows it.

**Comparsa pages.**
- **List**: `ComparsaLogo size="sm"` before the name, in the name column and in `mobileRow`. No new
  column.
- **Detail**: `RecordHeader media={<ComparsaLogo size="lg" …/>}`.
- **Admins** also get a "Logo" `SectionCard` with `PhotoUpload`:
  - rules: `output: 'png'`, no `aspect`, `maxSideRatio: 3`, `minLongSide: 256`, maximum 1024;
  - `removal` goes through `ConfirmDialog`;
  - upload and removal use the orval mutations, then invalidate `getGetComparsaQueryKey(id)` and
    `getListComparsasQueryKey()`, which also refreshes the sidebar cards.
- FiringChiefs get no logo section.

**i18n** (es-ES, ca-ES-valencia, en):
- `catalog:comparsas.logo.*`: `section`, `help` (formats, minimum size, transparency kept), `alt`
  ("Logo of {{name}}", used inside the logo section where the name is not next to the image),
  `empty`, `uploaded`, `removed`, and `removeConfirm.{title,description,confirm}`;
- `catalog:errors.{logos.notFound, storage.unavailable, catalog.busy}` and
  `catalog:validation.file.*`;
- `ui:nav.comparsas` (the label of the sidebar cards' landmark);
- `ui:photoUpload.transparencyHint`, if the PNG mode needs a hint.

### D9. Synthetic seed

`CatalogSeeder` gives the seeded comparsas a logo, except one active comparsa.
- The images come from a PNG writer that moves from the registry's `SyntheticPhotos` to
  `SharedKernel/Images/SyntheticPng` and gains RGBA output. The registry keeps its shapes and
  calls the shared writer.
- The shapes are flat and abstract on a transparent background: a disc with a band, a diamond, a
  crescent, a chevron shield. They have no text or letters. One is near-black, to exercise the
  light tile in the dark theme.
- They go through the same `IImageNormalizer` with `LogoStorage.Rules`.
- The logo ids are fixed, so the keys are fixed and a rerun re-puts any missing object
  (idempotent). The `api-seed` service already has the storage settings since #6.
- E2E relies on one seeded comparsa with a logo and one without, both assigned to the seeded
  FiringChief "Jefe Sintético Uno".

### D10. Tests

- **Backend unit tests**:
  - the `LogoStorage.Rules` edges: 255 and 256 px long side, ratio 3.0 and 3.01, a 3000 × 1500
    image becomes 1024 × 512;
  - alpha survives in the output PNG;
  - an SVG header is rejected;
  - the shared `ImageUploads` mapping.
- **Integration tests** (Testcontainers PostgreSQL and MinIO):
  - Admin upload, replace and remove, with the old object erased;
  - FiringChief `403` on PUT and DELETE;
  - FiringChief GET of an out-of-scope comparsa answers `404`;
  - an unknown id answers `404` before the body is read;
  - the audit entries, and no entry on a rejection;
  - a deletion erases the logo, and a blocked deletion keeps it;
  - a storage outage on upload and read answers `503`, and on removal answers `204`;
  - a name edit racing a logo upload keeps both changes;
  - the sweep owner;
  - `ReadComparsaLogoAsync`;
  - the scope guard test's route list includes `/comparsas/{id}/logo`.
- **Frontend tests**: Vitest for `ComparsaLogo` (placeholder, load failure, axe), for `PhotoUpload`
  in PNG mode (with a mocked `photo-image`), for the sidebar cards (role, order, drawer closing,
  axe on the night surface) and for the comparsa pages with MSW multipart.
- **Playwright** (`e2e/comparsa-logos.spec.ts`):
  - an Admin uploads a generated transparent PNG, and the list and the header show it;
  - an Admin removes it after the confirmation;
  - the FiringChief sees the sidebar cards (one logo, one placeholder) and no logo actions;
  - the FiringChief gets `404` on another comparsa's logo URL;
  - axe runs on the sidebar in both themes, including at 360 px in the drawer.

### D11. Security

- **BR-12 and SEC-03**:
  - writes are Admin-only through the existing policy;
  - reads use the comparsa scope and answer `404` out of scope;
  - existence is checked before the body is read.
- **SEC-02 posture**: a logo is not personal data, but it shares the private bucket. Every read is
  authorised, no browser path leads to the storage, and responses are `no-store`.
- **Hostile files**: the defences are the same as #6 D12:
  - the format comes from the content;
  - the pixel cap applies before decoding;
  - the size cap applies in Kestrel and in the form reader;
  - one normalisation runs at a time;
  - the output is a freshly encoded PNG, so polyglots and SVG scripts cannot survive;
  - the response has a fixed `image/png` type and `nosniff`.

  The new `ImageUploads` limit caps upload abuse.
- **NFR-12**: logs carry comparsa and logo ids, outcomes and reasons. They never carry client file
  names or object contents.
- **Third-party brand assets** (ADR-0012, ADR-0013): real logos exist only in a deployment's
  bucket. The seed, the fixtures and the stories use generated shapes only.
- **CSP**: unchanged. `<img>` loads from `'self'`, and previews use `blob:`.

## Risks / Trade-offs

- [The working copy in PNG mode is larger than in JPEG mode, up to 2400 px RGBA] → Logos are small
  in practice. The 25 MB and 100 MP source caps in `photo-image.ts` still apply, and
  `releaseImage` frees the canvases.
- [A logo with a white background looks boxed on the light tile] → It is acceptable, and the help
  text recommends a transparent PNG. Background removal is out of scope.
- [About 20 uncached logo requests on the comparsas list] → Each logo is a few KB to a few hundred
  KB, at about 60 users. If it ever matters, the versioned URL allows `private, max-age`
  later, with a platform exception for that route.
- [The new `ImageUploads` limit applies to logos only, while photos keep `PersonalDataWrites`] →
  This is deliberate, to keep this change's scope. See "Follow-ups".
- [The sidebar grows for a FiringChief with several comparsas] → It is one or two in practice.
  The cards scroll with the sidebar content, and the names truncate after two lines.

## Migration Plan

1. Deploy: `migrate` adds the nullable logo columns and the constraints. The bucket already
   exists since #6. The readiness check is unchanged.
2. No data migration. Existing comparsas have no logo and show the placeholder. Admins upload the
   real logos in production.
3. Rollback: redeploy the previous image. It ignores the new columns. Stored logos stay in the
   bucket under `catalog/logos/`, which the previous sweeper does not own, so they are not
   erased. A later redeploy finds them referenced again.

## Research notes (task 1.1)

Checked with Context7 on 2026-10-02 (`gh` is not installed on the dev machine, as in #5 and #6):

- **EF Core 10.0.12**: optional complex types exist since EF 10 (`ComparsaLogo?`, all columns null
  when absent), but indexes on complex-type properties only arrive in EF 11. The logo is therefore
  an **owned type** (`OwnsOne`, table splitting on `catalog.comparsas`), which supports
  `HasIndex(l => l.ObjectKey).IsUnique().HasFilter(...)` and explicit column names. EF sets an
  optional owned dependent to null when one of its required properties has no value, so every logo
  property is required inside the owned type and the columns are nullable on the table. Replacing
  the logo assigns a new `ComparsaLogo` instance; EF rewrites the columns of the same row.
- **SkiaSharp 4.153.1**: the normaliser already encodes `Rgba8888`/`Premul` bitmaps to PNG after
  clearing to transparent (#6), so alpha survives. Skia has no SVG codec: `SKCodec.Create` returns
  null for SVG content, which is `UnsupportedFormat`. Task 3.1 pins both with tests.
- **Canvas**: `createImageBitmap` keeps alpha by default (`premultiplyAlpha: 'default'`), and
  `toBlob('image/png')` is lossless with alpha in every engine. Only the JPEG working copy dropped
  it. The Playwright run checks the three engines.
- **Rate limiting**: the new policy reuses `RateLimits.PerUser` with a fixed window, as
  `PersonalDataWrites` does. No new API is involved.

## Implementation notes from the group 2 reviews

- `ImageUploads` refuses a non-positive limit or one whose request size does not fit an `int`
  (the in-memory buffer threshold), instead of wrapping silently. Its doc says the endpoint must
  use `MaxRequestBytes` as its request size limit. A test reads a streamed (non-seekable) body above
  ASP.NET's 64 KB default threshold, which is the path where the threshold matters.
- `SyntheticPng` refuses empty sizes and overflows.

## Implementation notes from the group 4 reviews

- **Routes**: the group is `/comparsas/{id:guid}` and the routes are `/logo`. A group ending in
  `/logo` with an empty child pattern yields the template `…/logo/`, which the scope guard does not
  recognise.
- **Content type before authorisation**: `.Accepts("multipart/form-data")` makes routing answer
  `415` to another content type before authorisation runs. This reveals nothing, because the
  answer does not depend on the id. The comparsa scope guard sends a multipart image to routes that
  declare it, so it still proves the `403`.
- **Upload structure**:
  - `UploadAsync` delegates the transaction to `SaveAsync`, and erases images only after the
    transaction has ended;
  - the put is inside the guarded block, so a cancelled put erases the image at once;
  - a lock timeout or deadlock before the commit answers `503 catalog.busy` instead of `500`
    (`CatalogLocks.IsRetryable`), and so does a removal.
- **Shared reader**: `LogoReader` serves both the API read and `ICatalogDirectory`. It re-reads the
  reference once when the image is missing (a replacement race), and logs an Error only for a
  reference that still has no image. A failure while copying the image becomes
  `StorageUnavailableException`, as the contract promises.
- **Logs**: rejected uploads carry the acting user, and the "left for the sweep" warning carries the
  exception.
- **New tests**:
  - deactivation and reactivation keep the logo;
  - a replacement whose erasure fails is cleaned by the next sweep;
  - a storage outage changes nothing;
  - the sweep owner is case-sensitive;
  - a reference without an image reads as no logo, through the API and through the directory;
  - a comparsa row held past the lock timeout answers busy and keeps no image;
  - the GET response has `nosniff` and `no-store`.

## Implementation notes from the group 5 review

- Seeded logo ids use the unused prefix `0193a600-…`, because `0193a300-…` is taken by registry
  arquebusiers.
- Outside local environments, the staging guard also refuses a comparsa whose logo the seeder did
  not create, since it may be a real emblem uploaded by hand. A refused run writes nothing to the
  storage either.
- Locally, a seeded logo that an Admin replaced is kept, and one an Admin removed is added again.
- The rows are saved first and the logos after them, so a storage failure leaves rows without logos
  until a rerun completes them.
- `docker compose up --build` does not rebuild the `api-seed` image: use
  `docker compose run --rm --build api-seed` after backend changes.

## Implementation notes from the group 6 reviews

These notes supersede the parts of D8 they change.

- **Sidebar cards**:
  - they are rendered first inside the scrollable `SidebarContent`, not in the fixed header, so at
    320 px or 400 % zoom they scroll with the navigation instead of squeezing it out (1.4.10);
  - a long name wraps over as many lines as it needs (`wrap-break-word`), never clipped. There is
    no tooltip, which a keyboard or touch user cannot reach (1.4.12);
  - the open comparsa is marked like the current navigation item, with `aria-current="page"`,
    weight and the ember bar;
  - the drawer-closing click is shared with the navigation (`useCloseDrawer`).
- **`ComparsaLogo`**: when it has an `alt` and only the placeholder can be shown, the tile becomes
  `role="img"` with that name, so a standalone logo keeps its name.
- **`PhotoUpload` in PNG mode**:
  - the checkerboard is behind the current logo and the preview image, not behind the caption;
  - `photo-image.test.ts` checks the encodings with a fake canvas: JPEG with a white fill by
    default, PNG without a fill or quality, and a refusal when the browser falls back to another
    type.
- **Contrast pairs**: the card name on the hover surface (`sidebar-border`) and the focus ring on it
  are in `contrast.test.ts`.

## Implementation notes from the group 7 reviews

- **`PhotoUpload` `subject="logo"`**: the control's own texts speak of a logo with its rules (use,
  chosen image, preview, uploading, load failure, and the client-side errors "at least 256 px" and
  "at most 3 times"). The photo texts are unchanged.
- **Removal is announced**: there is a `removedText` prop, "Removed: <label>" by default, so the
  photos gain the announcement too.
- **Errors**: a `429` from the `ImageUploads` limit shows "too many uploads". A `validation` problem
  without a known file reason is generic, because the crop dialog has no fields.
- **Sidebar cards**: they refetch when the window regains focus. The shell stays mounted for the
  whole session, while an Admin may change logos, names or assignments.
- **Logo URL**: it is built from the generated client (`getGetComparsaLogoUrl`), with the version
  encoded.
- **E2E**: the suite's stored FiringChief is "Jefa Sintética Dos" (Norte only). The spec's "two
  cards, one logo and one placeholder" needs "Jefe Sintético Uno" (Norte with a logo, Sur without),
  so `auth.setup.ts` saves that session as well.

## Implementation notes from the verification (task 8.3)

- **E2E (`e2e-runner`)**:
  - the FiringChief tests run with `test.use({ storageState })` on the `page` fixture, so the CSP and
    page-error watcher covers them;
  - the cards are asserted by name, because `catalog.spec.ts` briefly assigns "Jefe Sintético Uno"
    to a comparsa of its own while other projects run;
  - the out-of-scope `404` has a positive control (the Admin reads Este's logo);
  - the dark-tile check compares with the `--logo-tile` token instead of a fixed colour;
  - on phones, choosing a card closes the drawer, the open comparsa's card is marked current, and an
    Admin sees no cards;
  - the spec runs in Chromium, at 360 px, and in Firefox and WebKit.
- **Coverage (`pr-test-analyzer`)**:
  - the FiringChief `403` test starts from an existing logo and proves it is untouched;
  - a logo can be uploaded for an inactive comparsa;
  - the logo rules accept an EXIF-rotated JPEG (stored upright) and WebP;
  - the UI refuses a 200 × 150 logo before uploading, in logo terms, and replaces an existing logo.
- **Not done (low risk)**: an upload racing a comparsa deletion end to end (both halves are tested),
  and a catalogue assertion per logo story (the story is in the catalogue and its axe check).

## Follow-ups

- **JPEG working copy (pre-existing, #6).** `loadImage` and `rotateImage` encode the working copy
  as JPEG without a white fill, so a transparent PNG chosen as a license photo gets a black
  background before the crop. Only `cropImage` fills white. Fill white in the JPEG branch of both
  in a small fix commit.
- **Image upload memory (security review, MEDIUM).** The whole multipart body is buffered before
  the single normalisation slot is taken, and decoding a 40 MP image peaks at about 400 MB. This
  is shared with the #6 photos, which FiringChiefs also upload. Fix it together:
  - add a global concurrency limit (2–3 uploads) next to the per-user windows;
  - read the exact length from seekable streams and avoid the extra copies;
  - set the container memory limit at deployment (ADR-0006).
- **Logo reads**: `GET /comparsas/{id}/logo` has no rate limit and is `no-store`. This is acceptable
  at about 60 users. If it ever matters, the versioned URL allows `private, max-age`, which needs a
  spec change.
- `RateLimits.Limit()` silently falls back to the default when a configured value is invalid (for
  example `"200 "` or `0`). This was pre-existing for every limit (silent-failure review, group 2).
  It fails closed, but an operator gets no hint. Validating the values at startup belongs in a small
  platform commit.
- Move the registry photo uploads to the `ImageUploads` limit. This was suggested in #6 group 2–3
  notes. It belongs in a small tooling or security commit after this change.
