# Design

## Context

See `proposal.md` for the motivation, and the delta specs (`arquebusier-registry`, `platform`,
`design-system`) for the behaviour. The current state that shapes this design:

- **No storage code yet.** `compose.yaml` already runs MinIO (Chainguard image, no shell, so no
  healthcheck), and `.env.example` has `MINIO_ROOT_USER` and `MINIO_ROOT_PASSWORD`. No .NET project
  references an S3 client or an image library. The API image is `aspnet:10.0-noble-chiseled-extra`:
  it has no shell and no system libraries beyond ICU.
- **Platform services follow one pattern.** Abstractions live in `PolvorApp.SharedKernel`, and
  implementations live in `PolvorApp.Api/Platform/*`: for example `IEmailSender` and
  `SmtpEmailSender`. Modules reference only the SharedKernel and other modules' `.Contracts`
  (architecture tests).
- **Registry module (#5).** Arquebusiers carry an `xmin` `Version`. Rows are locked with
  `SqlQuery<int>("SELECT 1 … FOR …")`, with the scope in the `WHERE` clause (`RegistryLocks`).
  Outcomes are mapped to problem `code`s in `RegistryProblems`. `IAuditTrail.Record` runs before
  `SaveChangesAsync`. Registry routes accept at most 64 KB bodies, writes use the
  `PersonalDataWrites` rate limit (60 per minute per user), and every registry transaction sets
  `lock_timeout = '5s'`. #5 design D10 left a seam for #6: delete the photos after the deletion
  commits, and catch failures with an orphan sweep.
- **Platform.**
  - Every API response gets `Cache-Control: no-store` and `Content-Security-Policy: default-src
    'none'`.
  - The UI policy allows `img-src 'self' data: blob:`, and `Permissions-Policy` already allows the
    camera for the app's own origin.
  - nginx already allows 10 MB bodies on `/api/`.
  - Anti-forgery is a platform middleware that checks `X-XSRF-TOKEN` on every unsafe `/api`
    request. It is not the ASP.NET `UseAntiforgery()` middleware.
- **Frontend.** The UI calls the API only through the orval-generated client, with the `apiFetch`
  mutator (cookies and anti-forgery header). `ArquebusierDetailPage` groups personal data, license,
  course and owned weapons in `FormSection` and `PageSection`. There is no photo composite yet, but
  ADR-0009 already lists `PhotoUpload` (crop) among the planned composites.

## Goals / Non-Goals

**Goals:**
- Build storage and image services that are generic: #6b (comparsa logos, PNG with transparency)
  and #16 (badges read ID photos) reuse them without changes to the platform.
- Never let a photo leak: no public bucket, no storage address or credentials in the browser, no
  personal data in object names, logs or audit entries, and no cached responses.
- Keep photo writes independent of arquebusier edits, so an upload never makes the edit form's
  version outdated.
- Make erasure reliable (BR-14, SEC-08), even when the storage is briefly down.

**Non-Goals:**
- No pre-signed URLs and no direct browser uploads to the storage (D5).
- No image variants or thumbnails: one normalised JPEG per photo.
- No virus scanning: decoding and re-encoding already destroys any payload hidden in the file (D3).

## Decisions

### D1. Platform storage service (`IObjectStorage`)

`PolvorApp.SharedKernel/Storage`:

```csharp
public interface IObjectStorage
{
    Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken ct);
    /// <returns>null when the object does not exist.</returns>
    Task<StoredObject?> GetAsync(string key, CancellationToken ct);   // StoredObject: Stream, ContentType, Length
    Task DeleteAsync(string key, CancellationToken ct);                // idempotent
    IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, CancellationToken ct); // Key, LastModified
}
public sealed class StorageUnavailableException : Exception;           // never wraps SDK text with keys/credentials
```

The implementation is `PolvorApp.Api/Platform/Storage/S3ObjectStorage`, built on **AWSSDK.S3**
(Apache-2.0). It speaks the generic S3 API, so MinIO and EU providers are interchangeable
(ADR-0005).
- **Configuration** (`StorageOptions`, `Storage__*`):
  - `ServiceUrl`;
  - `Bucket`;
  - `AccessKey` and `SecretKey`;
  - `Region`, default `us-east-1`, used for signing only;
  - `ForcePathStyle`, default `true`;
  - `ServerSideEncryption`, default `None`, or `Aes256` (SSE-S3) for providers that need it per request.

  The host validates them at startup with `ValidateOnStart`. A failure logs the setting names only,
  as the database and email options do.
- **SDK v4 note** (Context7, `aws-sdk-net`): `AmazonS3Config { ServiceURL, ForcePathStyle,
  AuthenticationRegion }`. Set `RequestChecksumCalculation` and `ResponseChecksumValidation` to
  `WHEN_REQUIRED`: the v4 default (`WHEN_SUPPORTED`) sends checksum headers that some
  S3-compatible providers reject. Check this against MinIO in the integration tests (task 1.1).
- **Errors**: network failures, 5xx answers and authentication failures become
  `StorageUnavailableException` without the SDK message. Registry endpoints map it to
  `503 storage.unavailable`. `NoSuchKey` becomes `null` on get, and nothing on delete.
- **Bucket bootstrap**: `migrate` runs a `StorageBootstrapper` after the database migrations.
  It calls `HeadBucket`, and creates the bucket only when that answers 404. It retries for up to
  30 s, because MinIO may still be starting. Production operators may pre-create the bucket and
  grant a least-privilege key (Get, Put, Delete and List on the bucket); `HeadBucket` then
  succeeds and nothing is created. The bucket is never given a public policy: it is private by
  default in S3 and in MinIO.
- **Readiness**: a `StorageHealthCheck` runs `HeadBucket` with the same 3 s timeout, tagged `ready`
  (platform spec, Health endpoints).
- **Key convention** (modules README): `<module>/<collection>/<uuid>.<ext>`, for example
  `registry/photos/0192….jpg`. Every module owns its prefix. The keys are random UUIDv7 values,
  never derived from the person (spec: Private photo access).

*Alternative considered*: the MinIO .NET SDK. It is equally licensed, but tied to one vendor's
idioms, and the AWS SDK is the de facto client for "any S3-compatible provider".

### D2. Orphan sweep (platform "Stored file cleanup")

Writes and deletions touch two systems, the database and the storage, which cannot share a
transaction. The order of operations makes every failure leave an **unreferenced object**, never
a dangling reference:
- **write**: put the new object, then commit the reference, then delete the replaced object;
- **delete**: commit the removal of the reference, then delete the object.

An unreferenced object is harmless except for retention, so a sweep removes it:

- SharedKernel contract: `IStoredObjectOwner { string Prefix; Task<IReadOnlySet<string>>
  FilterReferencedAsync(IReadOnlyCollection<string> keys, CancellationToken ct); }`. The registry
  implements it for `registry/photos/` with one `WHERE object_key = ANY(@keys)` query per page.
- Platform `StoredObjectSweeper` is a `BackgroundService`. Every hour (`Storage__SweepIntervalMinutes`),
  for each owner it pages `ListAsync(prefix)` 500 keys at a time and asks which keys are
  referenced. It deletes every unreferenced object whose `LastModified` is more than 1 hour old,
  the grace that protects an upload in flight. Any exception skips the rest of the run and logs at
  Warning, so it never deletes on doubt. It logs `{owner, scanned, deleted}` at Information. It
  uses `TimeProvider` and is unit-tested with a fake storage and owner.
- **Worst-case retention** of an erased photo is 1 h of grace plus one interval, about 2 h. That is
  well inside the 24 h the spec allows. The common case is an immediate delete right after the
  commit.

*Alternative considered*: an outbox table of keys to delete, written in the business transaction.
It is more precise, but it still needs a sweep for the "put succeeded, commit failed" case. The
sweep alone covers both, and at about 3,000 objects (NFR scale) listing is cheap.

### D3. Image normalisation (`IImageNormalizer`)

`PolvorApp.SharedKernel/Images`:

```csharp
public sealed record ImageRules(
    int MaxInputBytes, long MaxInputPixels,
    AspectRule Aspect,             // Fixed(3, 4, tolerance 0.01) | Range(min 0.5, max 2.0)
    int MinWidth, int MinHeight,   // ID: 600×800; license: long side ≥ 800 (MinLongSide)
    int MaxWidth, int MaxHeight,   // ID: 1200×1600; license: long side ≤ 2000
    OutputFormat Output);          // Jpeg(quality 85) — #6b adds Png
public abstract record ImageNormalization; // NormalizedImage(ReadOnlyMemory<byte> Content, int Width, int Height, string ContentType) | RejectedImage(ImageRejection)
public enum ImageRejection { TooLarge, UnsupportedFormat, TooSmall, AspectRatio }
```

The implementation is `PolvorApp.Api/Platform/Images/SkiaImageNormalizer`, built on **SkiaSharp**
(MIT, named in ADR-0005) with `SkiaSharp.NativeAssets.Linux.NoDependencies`. The chiseled image
has no fontconfig, and no text is drawn.
1. Check the length, then read the stream into memory up to `MaxInputBytes + 1`.
2. `SKCodec.Create`: when it returns null or reports a format other than JPEG, PNG or WebP, reject
   with `UnsupportedFormat`. The format comes from the content, never from the file name or the
   declared type.
3. Read `codec.Info` **before decoding**. When width × height exceeds `MaxInputPixels` (40 MP),
   reject with `TooLarge`. This guards against decompression bombs.
4. Decode, then apply `codec.EncodedOrigin`: rotations and flips for the 8 EXIF values (Context7,
   `SKEncodedOrigin`). Validate the dimensions and aspect **after** orientation.
5. For a fixed aspect: centre-crop to the exact ratio, which is at most 1 % off, then scale down
   to fit the maxima (`SKSamplingOptions` with a high-quality filter). Never scale up.
6. Encode a fresh `SKImage` as JPEG at quality 85, in the sRGB colour space. A re-encoded bitmap
   carries no EXIF, XMP, IPTC or ICC metadata. A test reads the output's markers to prove it.

Image work is CPU-bound and allocates memory: a 40 MP RGBA bitmap is about 160 MB. A platform
`SemaphoreSlim(1)` allows one normalisation at a time (see the group 2–3 review notes below), and the per-user rate limit applies first.
Waiting is bounded by the request timeout, and a cancelled request releases its slot.

*Alternatives considered*:
- ImageSharp: under the Six Labors Split License, permissive only under conditions. ADR-0010
  prefers plain permissive licenses.
- Magick.NET and NetVips: they need native libraries the chiseled image lacks.
- Trusting the browser crop and skipping server processing: this contradicts ADR-0005 and SEC-12,
  because the client can be bypassed.

### D4. Data model and migration

There is a new table in schema `registry`, with migration `AddArquebusierPhotos`:

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | UUIDv7; also the object name |
| `arquebusier_id` | uuid | FK → `arquebusiers(id)` `ON DELETE CASCADE` |
| `kind` | varchar(16) | `ID` \| `LICENSE_FRONT` \| `LICENSE_BACK` (check constraint from `EnumCodes`) |
| `object_key` | varchar(200) | unique `ix_arquebusier_photos_object_key` |
| `width`, `height` | int | checked `> 0` |
| `size_bytes` | int | checked `> 0` |
| `uploaded_at` | timestamptz | |

The unique index `ix_arquebusier_photos_arquebusier_id_kind` on `(arquebusier_id, kind)` allows
one photo per kind.

- **The enum is `ArquebusierPhotoKind`** in `ArquebusierRegistry.Contracts`, because #16 badges
  need `ID`. Its codes are `ID`, `LICENSE_FRONT` and `LICENSE_BACK`. The glossary maps them to
  `idPhoto`, `frontPhoto` and `backPhoto`. In URLs the kind is a slug (`id`, `license-front`,
  `license-back`), parsed by a fixed map, so no codes in upper case appear in paths.
- **A separate table, not columns on `arquebusiers`**: a photo write then never changes the
  arquebusier's `xmin`, so the edit form's version stays valid (spec). Deleting an arquebusier
  cascades to the photo rows. The objects are deleted after the commit (D7).
- **No license constraint in the database**: the rule "license photos need a license" spans two
  tables and is enforced under the row lock in D6. The license removal in D7 deletes the rows in
  the same transaction, and a database test asserts that no license photo exists without a
  license after the race tests.
- `ModelDriftTests` already covers the registry context.

### D5. API endpoints and access

The routes sit in their own group: the registry group's 64 KB limit must not apply to them.

| Method | Path | Access | Notes |
|---|---|---|---|
| PUT | `/arquebusiers/{id}/photos/{kind}` | signed in, scoped | `multipart/form-data` with one part, `file`. Answers 200 with `ArquebusierPhotoResponse {kind, version, width, height, uploadedAt}`, where `version` is the photo id. 10 MB request limit (`RequestSizeLimit`, with `MultipartBodyLengthLimit` to match). Rate limit `PersonalDataWrites` |
| GET | `/arquebusiers/{id}/photos/{kind}` | signed in, scoped | Streams `image/jpeg` with `Content-Length`. Headers: `Cache-Control: no-store` (platform) and `Content-Disposition: inline; filename="photo.jpg"`, a fixed name with no personal data. 404 `photos.notFound` |
| DELETE | `/arquebusiers/{id}/photos/{kind}` | signed in, scoped | 204. Rate limit `PersonalDataWrites` |

- **Response changes**:
  - the detail (`ArquebusierResponse`) gains `photos: { id, licenseFront, licenseBack }`. Each
    entry is `ArquebusierPhotoResponse` or null;
  - list rows gain `hasIdPhoto` (an `EXISTS` subquery; no extra request per row).
- **Anti-forgery**: minimal APIs mark `IFormFile` parameters as needing the ASP.NET anti-forgery
  middleware, and throw when it has not run. The platform validates `X-XSRF-TOKEN` in its own
  middleware for every unsafe `/api` request, so the upload endpoint calls `.DisableAntiforgery()`.
  A test proves that an upload without the header still gets `400 antiforgery.invalid`.
- **Problem codes** (`RegistryProblems`, translated in `registry:errors.*`):

  | Situation | Response | `code` |
  |---|---|---|
  | unknown `kind` slug | 404 | (route does not match) |
  | file missing, too large, wrong format, too small, wrong shape | 400 | `validation`, `errors{file: required \| tooLarge \| unsupportedFormat \| tooSmall \| aspectRatio}` |
  | arquebusier not found or out of scope | 404 | `arquebusiers.notFound` |
  | no photo of that kind (GET, DELETE) | 404 | `photos.notFound` |
  | license photo without a license | 409 | `photos.noLicense` |
  | concurrent first upload of the same kind lost the race | 409 | `photos.modified` |
  | storage unreachable | 503 | `storage.unavailable` |
  | lock timeout | 503 | `registry.busy` (existing) |

  A body over 10 MB is cut off by Kestrel before the endpoint runs. The endpoint answers 400
  `file: tooLarge` when the form reader throws `InvalidDataException` or `BadHttpRequestException`
  (413), so the UI handles a single code. The test pins the observed status.
- **Why streaming, not pre-signed URLs** (ADR-0005 allows both):
  - streaming re-runs the scope check on every view;
  - the storage never needs to be reachable from browsers, and nginx and the CSP stay unchanged;
  - no bearer URL can leak into logs, history or screenshots.

  The cost is that the API relays about 3 photos per detail view (a few hundred KB), which is
  trivial at this scale (NFR-05). The cookie session authenticates `<img src>` because the API is
  same-origin.

### D6. Upload flow and concurrency

`ArquebusierPhotoAdministration.UploadAsync(id, kind, stream)`:

1. **Scoped existence check** without a lock (`ArquebusierQueries.ExistsInScope`). If out of
   scope, answer `404` before reading the body or doing any CPU work, so out-of-scope ids cannot
   be probed through expensive work or stored objects.
2. **Normalise** (D3). If rejected, answer `400`. Nothing is stored or audited.
3. **Put** the object `registry/photos/{newId}.jpg`. If the storage is down, answer `503`.
4. **Transaction** (`lock_timeout 5s`):
   1. Lock the arquebusier with `SELECT 1 … FOR SHARE`, with the scope in the `WHERE` clause.
      `FOR SHARE` conflicts with the `UPDATE` of an edit (`FOR NO KEY UPDATE`) and with the
      `FOR UPDATE` of a transfer or deletion. So a license photo cannot slip in while the same
      license is being removed, and a photo never lands on an arquebusier that just left the
      scope. Row locks do not change `xmin`, so the edit form's version stays valid (#5 D10).
   2. For license kinds, re-read `license_type`. If it is null, answer `409 photos.noLicense`.
   3. Read the existing photo row of that kind (`FOR UPDATE`). Update it in place with the new
      id, key and dimensions, or insert one.
   4. Audit (D8) and commit.

   A `UniqueViolation` on `(arquebusier_id, kind)` happens only when two *first* uploads race.
   It becomes `409 photos.modified`, and the UI reloads. (Implementation: the existing row is
   rewritten in place; see the group 4 review notes.)
5. **After commit**: best-effort `DeleteAsync` of the replaced key. **On any failure in step 4**:
   best-effort `DeleteAsync` of the new key. In both cases the sweep (D2) is the backstop, and
   failures are logged with ids only.

`RemoveAsync` locks the same way, deletes the row, audits, commits, then deletes the object.
`GetAsync` runs the scoped query for the row, then `IObjectStorage.GetAsync`. A missing object is
an inconsistency: it is logged at Error with ids and answered `404 photos.notFound`.

### D7. Changes to existing registry flows

- **License removal** in `ArquebusierAdministration.UpdateAsync`: when the license goes from
  present to absent, the update deletes the `LICENSE_FRONT` and `LICENSE_BACK` rows in the same
  transaction. The `ArquebusierUpdated` audit data then gets `removedPhotos: ["LICENSE_FRONT",
  …]`. The keys are deleted after the commit. A type or date change keeps the photos (maintainer
  decision).
- **Deletion** (`DeleteAsync`): under the existing `FOR UPDATE` lock, read the photo keys and
  count them for the audit (`photoCount`). The rows cascade. After the commit, delete the objects
  best-effort.
- **Transfer**: nothing changes. The photo rows hang off the arquebusier, and the scope follows
  `comparsa_id`.
- **Storage outage**: arquebusier edits, transfers and deletions never call the storage before
  the commit, so they keep working (spec). Deletions leave objects for the sweep.

### D8. Audit entries

The entity type is `Arquebusier`, the entity id is the arquebusier id, and `ComparsaId` is the
current comparsa.

| Action | Data |
|---|---|
| `ArquebusierPhotoUploaded` | `{kind, replaced: bool}` |
| `ArquebusierPhotoRemoved` | `{kind}` |
| `ArquebusierUpdated` (license removed) | `{changedFields, removedPhotos}` |
| `ArquebusierDeleted` | `{ownedWeaponCount, photoCount}` |

There is no image data, object key, size or dimension. Keys are random, but they let someone with
bucket access link an entry to an image, and SEC-09 erasure must stay complete. Reading a photo is
not audited: it is a read, like the detail page.

### D9. Frontend

**The `PhotoUpload` composite** (`src/components/app/PhotoUpload.tsx`, built on
**react-image-crop**, ISC, about 5 KB gzip, no dependencies). Its README and Context7 docs say it
is "fully keyboard accessible", with fixed aspect support and an `ariaLabels` prop for
translations.
- **Props**:
  - `label`;
  - `photoUrl | null`, `photoAlt`;
  - `aspect?` (3/4), `minWidth`, `minHeight` or `minLongSide`, `maxWidth`, `maxHeight`;
  - `onUpload(blob): Promise<void>`;
  - `onRemove?(): Promise<void>`, which goes through `ConfirmDialog`;
  - `disabled`, `disabledHint`.
- **Picking**: a visually hidden `<input type="file" accept="image/*">` behind a `Button`, with
  **no `capture` attribute**. Phones then offer both the camera and the gallery (NFR-01). iOS
  hands HEIC photos over as JPEG.
- **Checks before upload**:
  - files over 25 MB are refused before decoding;
  - the file is decoded with `createImageBitmap` (with `imageOrientation: 'from-image'`). When
    that fails, the control shows `unsupportedFormat`;
  - an image below the minimum dimensions after orientation shows `tooSmall`.
- **Crop dialog** (the existing dialog primitive through the composite):
  - `ReactCrop` with `aspect`, `keepSelection` and `ruleOfThirds`. The initial selection uses
    `centerCrop` and `makeAspectCrop` at 90 %;
  - "Rotate left" and "Rotate right" buttons apply quarter turns;
  - a live preview, and "Use photo" or "Cancel".
- **Export**: our own canvas draw, not `cropToCanvas`, which multiplies by `devicePixelRatio`. It
  draws at the natural resolution, clamped to the maxima. For the ID photo it draws exactly 3:4,
  then `canvas.toBlob('image/jpeg', 0.9)`. Only the cropped blob is uploaded, so no original EXIF
  ever leaves the phone (defence in depth for SEC-12).
- **States**: empty (`EmptyState`-like placeholder), photo, uploading (`Button pending`, polite
  live region), and error (an `AlertBanner` inside the control with the translated reason).
  `<img onError>` swaps the image for a "Photo could not be loaded" message. Object URLs are
  revoked on unmount.
- **Storybook stories** cover empty, with photo, uploading, error, load failure and the crop dialog
  with a generated synthetic image. The stories carry axe checks, and the catalogue test registers
  the composite.

**Registry screens:**
- **Register page**: an optional "ID photo" field in *Personal data* (`PhotoUpload` in deferred
  mode, which keeps the cropped blob in form state). On submit: `POST /arquebusiers`, then, when a
  blob is present, `PUT …/photos/id`. If the upload fails, navigate to the detail page with a
  notice (`registry:photos.uploadFailedAfterRegister`, carrying the translated reason).
- **Detail page**:
  - in *Personal data*, the ID photo `PhotoUpload` with `aspect 3/4`, minimum 600 × 800 and
    maximum 1200 × 1600;
  - in *License*, two `PhotoUpload`s (front and back) with free aspect, a minimum long side of
    800 and a maximum long side of 2000. They are disabled with a hint when there is no stored
    license. A license that is only typed in but not yet saved also counts as no license.
  - **Image URL**: `/api/arquebusiers/{id}/photos/{slug}?v={photo.version}`. The query string
    changes when the photo changes, so `<img>` reloads. It carries no personal data. Uploads and
    removals invalidate the `arquebusiers/{id}` and `arquebusiers` queries.
  - **License removal warning**: saving the form when it removes a stored license that has photos
    opens a `ConfirmDialog` (`registry:license.removePhotosConfirm`). Cancelling keeps the form
    dirty.
  - **Renewal reminder**: after a save that changed the license type or dates while photos exist,
    an info `AlertBanner` says `registry:license.replacePhotosHint`.
- **List**: a "No ID photo" marker next to the name: a `StatusBadge`-like text with an icon, never
  colour alone. It is driven by `hasIdPhoto`. No thumbnails are shown.
- **Uploads** go through orval-generated mutations. The OpenAPI `multipart/form-data` body makes
  orval build a `FormData`, and the `apiFetch` mutator must leave `Content-Type` unset for
  `FormData`, so the browser adds the boundary (task 6.1 checks the generated code).

**i18n** (es-ES, ca-ES-valencia, en):
- `ui:photoUpload.*`: choose, take or choose photo, replace, remove, crop dialog title and help,
  rotate left and right, use photo, cancel, uploading, uploaded, `errors.{tooLarge,
  unsupportedFormat, tooSmall, aspectRatio, generic}`, `loadFailed`, `empty`, and the crop area
  ARIA label;
- `registry:photos.*`: section labels (`idPhoto`, `frontPhoto`, `backPhoto`), alt texts
  (`idPhotoAlt` with the name), `noIdPhoto`, `needsLicense`, `removeConfirm.{title,description}`,
  `uploadFailedAfterRegister`, `errors.{notFound,noLicense,modified}`;
- `registry:errors.storage.unavailable`, `registry:validation.file.*`;
- `registry:license.removePhotosConfirm.*` and `registry:license.replacePhotosHint`.

### D10. Synthetic seed

`RegistrySeeder` gives about half of the seeded arquebusiers an ID photo, and three of those with a
license both license photos.
- The images are generated at seed time as small hand-built PNGs (the module has no image
  library; implementation note): flat background colours from a fixed palette, a head-and-shoulders
  silhouette for ID photos, and rectangles and stripes for license cards. They have no text and no
  faces.
- They are run through the same `IImageNormalizer`, so they meet the real rules. Photo ids are
  fixed, so a rerun finds the rows and skips them.
- Because the object key derives from the fixed id, a rerun after a partial failure re-puts a
  missing object (put is idempotent). The seed requires the storage, and the `api-seed` service
  gets the storage settings.
- E2E tests rely on one seeded arquebusier with all three photos and one without any.

### D11. Infrastructure, CI and tests

- **compose.yaml**:
  - the `api-environment` anchor gets `Storage__ServiceUrl: http://storage:9000`,
    `Storage__Bucket: ${STORAGE_BUCKET}`, `Storage__AccessKey: ${MINIO_ROOT_USER}` and
    `Storage__SecretKey: ${MINIO_ROOT_PASSWORD}`. The root credentials are acceptable locally
    only, and `.env.example` and `docs/development.md` say production uses a scoped key;
  - `api-migrate` and `api-seed` depend on `storage: service_started`, and the 30 s bootstrap
    retry covers MinIO start-up;
  - the storage comment "nothing depends on it yet" is updated.
- **`.env.example`**: `STORAGE_BUCKET=polvorapp-photos`.
- **Backend tests**:
  - a `MinioFixture` (Testcontainers.Minio, MIT) on the same Chainguard image that compose uses,
    shared through the existing assembly fixture;
  - `ApiFactory` wires the storage options;
  - unit tests for `SkiaImageNormalizer` use small images generated in memory: EXIF with GPS and
    orientation, PNG, WebP, a PDF header, a 41 MP header-only PNG, and every aspect and size edge;
  - the sweeper is tested with fakes and `FakeTimeProvider`;
  - integration tests cover scope, rules, races (a license removal against a license photo upload,
    a deletion against an upload), the audit and storage outages (a stopped container, or a
    storage double that throws).
- **Frontend tests**: Vitest and Testing Library for `PhotoUpload`: validation, keyboard crop,
  rotation, error and load-failure states, and axe. The registry pages use MSW for multipart
  uploads.
- **Playwright** (`e2e/photos.spec.ts`): upload an ID photo through the crop dialog with a
  generated synthetic PNG, see it on the detail page and the list marker disappear, replace and
  remove a license photo, a FiringChief gets 404 on another comparsa's photo URL, and run axe on
  the dialog.
- **CI** already runs compose for E2E with the storage. The backend job's Testcontainers now also
  pulls the MinIO image.

### D12. Security and GDPR

- **SEC-02**: the bucket is private, there is no browser path to the storage, every read is
  authorised (D5), and responses are `no-store`. Encryption at rest is a provider or deployment
  setting (S3 default encryption, or MinIO with KMS). `docs/compliance.md` records this as a
  go-live check. Backups of the bucket follow ADR-0005 (DB dump plus bucket sync).
- **SEC-12**: server re-encoding always strips metadata (D3), and the browser export never sends
  the original file (D9).
- **SEC-08 and BR-14**: replaced, removed and deleted photos are erased after commit, with the
  sweep as backstop (D2). Old license photos go when they are replaced. A renewal without new
  photos keeps the old ones (maintainer decision). This is recorded in `docs/compliance.md` as an
  accepted retention: they belong to the current license record until replaced.
- **SEC-03 and BR-12**: every photo route is in the scope guard test's list (`/arquebusiers/{id}`
  prefix, already generic since #5), and the existence check runs before any work (D6).
- **SEC-05**: writes are audited without image data or keys (D8).
- **NFR-12**: logs carry arquebusier and photo ids, kinds, sizes and outcomes, never file names
  from the client (they may contain names), object contents or storage credentials. The
  multipart file name is ignored entirely.
- **Hostile files**:
  - formats are whitelisted by decoding, never by name or declared type;
  - the pixel cap is checked before decoding;
  - the size cap applies in Kestrel and in the form reader;
  - image work runs in a concurrency limiter and behind the per-user rate limit;
  - output is always freshly encoded JPEG, so polyglot or script-carrying files cannot survive;
  - responses send `nosniff` and a fixed `image/jpeg` type.
- **CSP**: unchanged. `<img>` from `'self'` and `blob:` previews are already allowed.

### Implementation notes from the group 2–3 reviews

- **Clocks**: the bucket bootstrap retries on the system clock, never on the host's
  `TimeProvider`: test hosts run on a `FakeTimeProvider`, and a retry delay on it never ends (this
  hung the suite under load). The sweeper keeps the host's clock, so its tests can drive it.
- **Timeouts**: AWSSDK.S3 4 surfaces a request timeout as `TimeoutException` (not
  `OperationCanceledException`); storage calls and the bootstrap map it to
  `StorageUnavailableException`, and the bootstrap retries it. Configuration and authorisation
  answers (`AccessDenied`, `InvalidAccessKeyId`, `SignatureDoesNotMatch`, `NoSuchBucket`) are
  logged at Error and are never retried. A missing bucket on read is an outage, not a missing
  object.
- **Sweeper safety** (beyond D2): a listing without `LastModified` reads as "just written"; a
  truncated listing without a continuation token stops the run; owners with a malformed or
  overlapping prefix are skipped; a run that would delete more than 100 objects and more than half
  of what it scanned is refused and logged at Error; a failed delete of one object no longer stops
  the others; any failure of a run is logged and never stops the host; `Storage__SweepEnabled`
  turns it off during restores. Test hosts disable it, because they share one bucket but not one
  database. Operations: one bucket per environment, versioning off (`docs/development.md`).
- **Image memory**: one image at a time, with the slot taken before the upload is buffered and a
  30 s wait (`ImageProcessingBusyException` → `503 registry.busy`); every rule is checked on the
  upright header dimensions before decoding; JPEGs much larger than the output are decoded at a
  smaller scale (the smallest eighth that still covers the output); bitmaps are marked immutable
  so images share their pixels; an upright image is not copied. Keys passed to the storage must
  follow the `<module>/<collection>/<name>.<ext>` convention.
- **Follow-up, not blocking**: a dedicated, lower rate limit for photo uploads (the general
  `PersonalDataWrites` 60 per minute applies today) and a container memory limit, decided with
  the deployment (ADR-0006).

### Implementation notes from the group 4 reviews

- **Scope before body**: the upload endpoint checks the scope before it reads the request body, so an
  out-of-scope or unknown id never makes the server receive the upload (D6 step 1). Only
  `multipart/form-data` is accepted (`415` otherwise, from the endpoint's `Accepts` metadata); a
  malformed body answers `400 file: required`, an oversized one `400 file: tooLarge`.
- **No photo on disk**: the form is read entirely in memory (`MemoryBufferThreshold` above the
  upload limit); ASP.NET would otherwise spill every part over 64 KB to a temporary file.
- **Replacement in place**: an upload of a kind that exists rewrites the locked row (new id, key and
  dimensions) instead of deleting and inserting it, so two concurrent replacements both succeed and
  the last one wins; `409 photos.modified` is left for two concurrent *first* uploads. The photo
  fields are init-only; the row lock reads explicit columns.
- **Cleanup on exceptions**: when the save throws before its commit (a cancelled request, a database
  error), the new image is erased at once; when it throws during the commit, whose outcome is
  unknown, the image is logged and left to the sweep (never a reference without an image). Erasures
  after a commit are bounded (10 s overall, stop at the first failure) and never fail the committed
  change.
- **Reads racing a replacement** re-read the reference once before logging a missing image as an
  error. Only the photo-kind index and the arquebusier foreign key are mapped to problems; any other
  constraint violation is a bug and surfaces as 500.
- **Logs**: rejected uploads (scope miss, image rule, busy, storage outage) are logged with ids, kind
  and reason, like other rejected registry writes. The platform anti-forgery check now refuses a
  request without the header before the validator could read a form body, and nginx accepts 11 MB
  (10 MB image plus multipart framing). The database checks that object keys are under
  `registry/photos/`, where the sweep looks.

### Implementation notes from the group 7 reviews and verification

- **Removal during a storage outage** answers `204`, not `503`: it commits the removal of the
  reference and leaves the image to the sweep (D2), so it does not need the storage. Uploads and
  reads answer `503` and change nothing.
- **Request limits**: the photo routes keep the registry's 64 KB body limit; only the upload has
  its own 10 MB + 64 KB limit.
- **E2E write throttle**: the suite registers and changes many arquebusiers as the one seeded Admin,
  so the compose stack exposes `RATE_LIMIT_PERSONAL_DATA_WRITES_PER_MINUTE` (default 60) and CI
  raises it for the E2E run, as for the sign-in limits.
- **Crop dialog at 320 px**: its single grid column cannot grow past the dialog and the action rows
  wrap, so every control stays reachable without horizontal scrolling (checked by Playwright).
- **Screens**: without a license only the explanation is shown (no disabled photo controls); each
  photo is a group named by its title; a controlled confirmation without a trigger returns focus to
  a named control (`returnFocus`); the register page announces a chosen, not yet saved ID photo.

- **Register page**: the photo control is disabled while the registration is sent (the chosen photo
  is the one uploaded), and if the answer carries no new arquebusier the list notice says the photo
  was not saved. After a confirmed license removal focus stays on Save during the request; a refused
  field then takes it with its message.
- **Test follow-ups** (from `pr-test-analyzer` and `e2e-runner`; the higher-risk gaps were closed with
  endpoint-level processing tests, a sweep against Postgres and MinIO, outage tests and the
  cross-engine Playwright checks of EXIF orientation, rotation, keyboard crop and 320 px):
  - Playwright for registering with an ID photo, for a seeded arquebusier's photos and for deleting
    an arquebusier with photos (covered by Vitest and API tests today);
  - unit tests of the real `photo-image.ts` in a browser-based runner (only Playwright exercises it);
  - sweeper boundaries at exactly 100 orphans and exactly half of a page;
  - the audit entry rolling back with a failed photo write, and positive log-shape assertions.

## Risks / Trade-offs

- [SkiaSharp native assets in the chiseled image] → Use `NativeAssets.Linux.NoDependencies`. A
  smoke test in the container E2E run proves that an upload works in the published image, and task
  1.1 verifies the package versions for .NET 10 with Context7.
- [AWS SDK v4 checksum defaults break some S3-compatible providers] → Use `WHEN_REQUIRED` for
  request and response checksums, with integration tests against MinIO. A provider-specific
  problem shows up at the go-live smoke test.
- [A FiringChief renews a license but never uploads the new photos] → The UI reminder after the
  save, and #7 may add a warning. Accepted (maintainer decision).
- [Memory spikes from large images] → The 10 MB and 40 MP caps checked before decoding, at most 2
  concurrent normalisations and the per-user rate limit. Client-side cropping means real uploads
  are usually under 1 MB.
- [The sweep deletes a referenced object because of a bug] → It deletes only after a successful
  reference query for that exact page, never on exceptions, and keeps the 1 h grace. A test seeds
  referenced and unreferenced objects and asserts that only the latter go. Backups are the last
  resort.
- [The API relays every image] → This is acceptable at about 3 photos per detail view and about
  60 users. Pre-signed URLs remain an option behind the same endpoint contract if it ever matters.
- [Optional ID photo] → Badges (#16) must handle a missing photo. The list marker makes the gap
  visible to FiringChiefs.

## Migration Plan

1. Deploy:
   - provision the bucket, or let `migrate` create it, and set the `Storage__*` secrets;
   - `migrate` adds `registry.arquebusier_photos` and ensures the bucket exists;
   - readiness turns unhealthy until the storage is reachable, which is intended.
2. No data migration: existing arquebusiers have no photos and show "No ID photo".
3. Rollback: redeploy the previous image. It ignores the new table and the bucket. Stored photos
   stay until the change is redeployed or the bucket and table are dropped by hand. The sweep
   does not run in the previous version, which is harmless.

## Research notes (task 1.1)

Checked with Context7 and the package registries on 2026-10-01 (`gh` is not installed on the dev
machine, so the real-world code search was skipped, as in #5):

- **Versions**: `AWSSDK.S3` 4.0.104, `SkiaSharp` and `SkiaSharp.NativeAssets.Linux.NoDependencies`
  4.153.1 (the 4.x line; "3.x" above is superseded), `Testcontainers.Minio` 4.15.0 (matches
  `Testcontainers.PostgreSql`), `react-image-crop` 11.1.2 (ISC, peer `react >= 16.13.1`).
- **AWSSDK.S3 v4**: `AmazonS3Config { ServiceURL, ForcePathStyle, AuthenticationRegion }`;
  `RequestChecksumCalculation` and `ResponseChecksumValidation` are `ClientConfig` properties and
  default to `WHEN_SUPPORTED`, so both are set to `WHEN_REQUIRED`. Listing pages with
  `ListObjectsV2Request.ContinuationToken`. A missing key or bucket surfaces as
  `AmazonS3Exception` with `StatusCode == 404`; network failures as `HttpRequestException` or
  `AmazonServiceException` wrappers, all mapped to `StorageUnavailableException` (D1).
- **SkiaSharp 4**: `SKCodec.Create`, `codec.Info`, `codec.EncodedFormat` and `codec.EncodedOrigin`
  are unchanged; `SKBitmap.Resize(SKImageInfo, SKSamplingOptions)` and
  `SKImage.Encode(SKEncodedImageFormat, int)` are present and not obsolete.
- **Minimal APIs**: endpoints that bind form data require the ASP.NET antiforgery middleware by
  default and fail before the handler runs. The platform validates `X-XSRF-TOKEN` in its own
  middleware (after authentication, before the body is read), so the upload endpoint uses
  `.DisableAntiforgery()` as planned in D5.
- **react-image-crop 11**: keyboard support on the selection (`role="group"`, `tabIndex=0`, arrows
  move it) and on the eight handles (`role="button"`, arrows resize); nudge 1 px, `Shift` 10 px,
  `Ctrl`/`Cmd` 100 px. All nine `ariaLabels` are replaceable, so they are translated. The
  stylesheet `react-image-crop/dist/ReactCrop.css` is imported by the composite; its colours are
  overridden with the design tokens through its CSS custom properties.
- **orval 8**: a `multipart/form-data` request body generates a `FormData` (`override.formData`
  defaults to generating it); the `apiFetch` mutator must not set `Content-Type` for it (task 6.1).
