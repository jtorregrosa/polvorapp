# Proposal

## Why

The registry (#5) holds every arquebusier's identity, license and course, but not the photos the
Federation asks for: the ID photo, which is printed on the arquebusier badge (UC-30, NFR-15), and the
front and back of the license, which prove that the license is real. Today these photos travel by
Google Forms and messaging apps and end up in shared folders. That is hard to keep private (SEC-02),
the files keep EXIF metadata such as GPS positions (SEC-12), and nobody deletes them when a license
is renewed or a person leaves the Federation (SEC-08, BR-14). PolvorApp is meant to be the
authoritative source for photos as well (`docs/vision.md`), and the badges (#16) need the ID photos.

Capability (from `docs/mvp.md`): **`arquebusier-registry`**, change #6 of the sequence. It completes
**UC-01** (the ID photo at registration) and **UC-02** (the license front and back photos). It
extends **BR-14** (deletion erases photos) and implements **ADR-0005** (private S3-compatible
storage, server re-encoding, EXIF stripping, browser cropping), **SEC-02**, **SEC-08**, **SEC-12**,
**NFR-01** (photos taken with a phone) and **NFR-15** (3:4 ID photo, at least 600 × 800 px). It
follows ADR-0001, ADR-0002, ADR-0006, ADR-0007, ADR-0009 and ADR-0011.

## What Changes

- **Three photos per arquebusier** (UC-01, UC-02): `idPhoto`, and the license `frontPhoto` and
  `backPhoto`. All three are **optional** (maintainer decision): the data model's "mandatory ID
  photo" becomes "expected": existing arquebusiers, the spreadsheet import (#8) and failed uploads
  leave arquebusiers without one. The list and the detail page show which arquebusiers have no ID
  photo. The badge change (#16) decides how to handle a missing photo.
- **Who**: the users who can edit an arquebusier upload, replace and remove their photos:
  FiringChiefs in their own comparsas, Admins in every comparsa (BR-12). Out-of-scope requests
  answer `404`.
- **License photos need a license**: they can be added only while the arquebusier has a license
  (pending or issued). Removing the license deletes its photos. Changing the license dates or type
  (a renewal) keeps the photos until new ones are uploaded (maintainer decision); each upload
  replaces and deletes the previous photo (ADR-0005, SEC-08).
- **Server-side processing** (ADR-0005, SEC-12, NFR-15). These rules are blocking (`400`):
  - only JPEG, PNG or WebP images of at most 10 MB are accepted;
  - the image is decoded, turned upright according to its EXIF orientation, re-encoded as JPEG and
    stripped of every metadata;
  - the ID photo must be 3:4 portrait and at least 600 × 800 px;
  - license photos must have a reasonable size and shape.

  Oversized images are scaled down.
- **Private storage** (ADR-0005, SEC-02): photos live in a private S3-compatible bucket (MinIO
  locally) under random keys that do not identify the person. The browser never talks to the
  storage: the API streams a photo only after the same scope check as the arquebusier, and the
  response is never cached.
- **Cleanup** (BR-14, SEC-08): replacing or removing a photo, removing a license and deleting an
  arquebusier delete the stored files once the change is committed. A periodic sweep deletes any
  stored file that no record references, so a failed deletion is retried within hours.
- **Audit** (SEC-05): every upload, replacement and removal is recorded with the photo kind, without
  image data, keys or personal values.
- **UI**: a new `PhotoUpload` composite. It picks a file or takes a picture with the phone camera,
  crops in the browser (fixed 3:4 for the ID photo, free for the license), rotates and previews,
  is fully keyboard operable, and uploads the cropped JPEG. The register page offers the ID photo.
  The detail page shows and manages the three photos. The list marks arquebusiers without an ID
  photo. Every text is in es-ES, ca-ES-valencia and en.
- **Platform**: an object storage service for every module, configured from the environment and
  validated at startup. The `migrate` command creates the bucket when it is missing, and readiness
  checks that the bucket is reachable.
- **Synthetic seed** (SEC-11): generated placeholder images (flat shapes, no faces and no real
  documents) for some seeded arquebusiers. Others are left without photos.

## Non-goals

- The arquebusier badge PDF (UC-30): that is `add-badges` (#16). This change only stores ID photos
  that are good enough to print.
- Comparsa logos: that is `add-comparsa-logos` (#6b), which reuses the storage and image services
  added here.
- Compliance warnings about missing photos or licenses without photos: that is
  `add-compliance-insights` (#7), if the Federation wants them. Here, a missing ID photo is only an
  indicator.
- Importing photos in bulk, for example from the Federation's external app or a ZIP file. The
  spreadsheet import (#8) brings no photos.
- Face detection, automatic background removal or any automated identification. Photos are never
  used for that (`docs/compliance.md`).
- Thumbnails in the arquebusiers list, photo history and old photo versions.
- Pre-signed URLs and direct browser uploads to the storage (ADR-0005 allows them; see design).
- Offline photo capture (UC-21 comes after the MVP).

## Capabilities

### New Capabilities
<!-- None: photos belong to the existing arquebusier-registry capability (docs/mvp.md #6). -->

### Modified Capabilities
- `arquebusier-registry`: new requirements for arquebusier photos, photo validation and processing,
  private photo access, photo screens and stored-file cleanup. Modified requirements: "Arquebusier
  visibility (BR-12)" (rows show whether there is an ID photo), "Current license (UC-02, BR-03)"
  (removing a license deletes its photos, and a renewal keeps them until they are replaced),
  "Deleting an arquebusier (UC-05, BR-14)" (photos are erased), "Registry changes are audited"
  (photo writes) and "Synthetic registry data" (synthetic photos).
- `platform`: new requirements "Private object storage" and "Stored file cleanup" (the periodic
  orphan sweep, shared with #6b); "Health endpoints" now also checks the storage for readiness.
- `design-system`: new requirement for the photo upload composite (pick or capture, crop, rotate,
  keyboard operable).

## Impact

- **Backend**:
  - `SharedKernel` gets two abstractions, `Storage/IObjectStorage` and `Images/IImageNormalizer`,
    and a contract that lets a module declare which stored files it references.
  - `PolvorApp.Api/Platform` implements them with **AWSSDK.S3** (Apache-2.0) and **SkiaSharp**
    (MIT, named in ADR-0005, with the Linux native assets that need no system dependencies). It
    also adds storage options, the readiness check, bucket creation in `migrate` and the orphan
    sweep.
  - The `ArquebusierRegistry` module gets a `registry.arquebusier_photos` table and migration,
    photo services and endpoints. Its license edit, deletion and seeder are extended.
- **API**:
  - `PUT|GET|DELETE /api/arquebusiers/{id}/photos/{kind}`, where `kind` is `id`, `license-front`
    or `license-back`. Uploads are `multipart/form-data`.
  - The detail response gains `photos`, and list rows gain `hasIdPhoto`.
  - `contracts/openapi.json` and the orval client are regenerated.
- **Frontend**: the `PhotoUpload` composite and its story, built on **react-image-crop** (ISC,
  keyboard accessible, no dependencies). The registry pages and the `registry` and `ui` i18n
  namespaces change in the three locales.
- **Infra**: in `compose.yaml`, the API, migrate and seed containers get the storage settings and
  wait for the storage. `.env.example` gets the storage variables. CI runs MinIO through
  Testcontainers for the backend tests and through compose for E2E.
- **Docs**:
  - `docs/data-model.md` and `docs/glossary.md`: the ID photo becomes optional, and the photo rules
    are added;
  - `docs/compliance.md`: how SEC-02, SEC-08 and SEC-12 are met, and the deletion delay;
  - `docs/third-party-licenses.md`: the new dependencies;
  - `docs/development.md`: storage settings and the MinIO console;
  - `docs/design/README.md`: the `PhotoUpload` composite;
  - `backend/src/Modules/README.md`: the object storage convention;
  - `docs/mvp.md`: the status of this change.
- **ADRs**: none new. The change applies ADR-0005 and chooses the streaming option it allows.
