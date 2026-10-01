# Tasks

## 1. Research and dependencies

- [x] 1.1 Check with Context7 (and gh for real-world usage) the APIs used in design D1, D3, D5 and D9:
  - AWSSDK.S3 v4: `AmazonS3Config` with `ServiceURL`, `ForcePathStyle`, `AuthenticationRegion`, and `RequestChecksumCalculation`/`ResponseChecksumValidation` set to `WHEN_REQUIRED` against MinIO; `HeadBucket`/`PutBucket`; `ListObjectsV2` paging; the exception types for 404, auth and network errors;
  - SkiaSharp 3.x: `SKCodec.Info` before decoding, `EncodedOrigin` transforms, high-quality resampling, JPEG encoding without metadata, and `NativeAssets.Linux.NoDependencies` on .NET 10 chiseled images;
  - minimal APIs: `IFormFile` binding with `.DisableAntiforgery()`, `RequestSizeLimit` and `FormOptions.MultipartBodyLengthLimit` per endpoint, and the status code for an oversized body;
  - react-image-crop: the version for React 19, keyboard support, `ariaLabels`, `centerCrop`/`makeAspectCrop`;
  - orval 8: `multipart/form-data` request bodies with the fetch client and a custom mutator.

  Record versions and any workaround in design.md. Verify: design.md updated, with no open question left.
- [x] 1.2 Add `AWSSDK.S3`, `SkiaSharp` and `SkiaSharp.NativeAssets.Linux.NoDependencies` (runtime) and `Testcontainers.Minio` (tests) to `Directory.Packages.props` and the projects that use them. Add `react-image-crop` to the frontend. Add the four rows to `docs/third-party-licenses.md`. Verify: `dotnet build`, `npm ci` and `docker compose build api web` succeed, and the dependency review job raises nothing.

## 2. Platform: object storage, cleanup and readiness

- [x] 2.1 Write tests for `StorageOptions` validation: a missing service URL, bucket or keys, a non-absolute URL, and secrets absent from the error log. Then add `SharedKernel/Storage` (`IObjectStorage`, `StoredObject`, `StoredObjectInfo`, `StorageUnavailableException`) and the options with `ValidateOnStart` (design D1). Verify: the tests pass.
- [x] 2.2 Add `MinioFixture` (Testcontainers, the same Chainguard MinIO image as compose) to the test assembly and wire `ApiFactory` with storage settings. Write integration tests for `S3ObjectStorage`:
  - put, get and delete round trip, with the content type kept;
  - a missing key gives null, and a second delete does not throw;
  - listing pages through more than 1,000 keys under a prefix;
  - an unreachable endpoint gives `StorageUnavailableException` without credentials in the message;
  - anonymous HTTP GET on an object is refused.

  Then implement it (design D1). Verify: the tests pass.
- [x] 2.3 Write tests for the storage bootstrap: `migrate` creates a missing bucket, reruns leave it unchanged, an existing bucket with a key that cannot create buckets passes, and the 30 s retry while the storage starts. Then implement `IStorageBootstrapper` in `MigrateCommand`. Verify: the tests pass.
- [x] 2.4 Write tests for readiness: healthy with the database and storage up, `503` naming `storage` without host or bucket names when the storage is down, and liveness unaffected. Then add `StorageHealthCheck`. Verify: the tests pass, including the existing health tests.
- [x] 2.5 Write unit tests for `StoredObjectSweeper` with a fake storage, a fake `IStoredObjectOwner` and `FakeTimeProvider`:
  - it deletes unreferenced objects older than 1 hour only;
  - it keeps referenced and recent objects;
  - it pages at 500 keys;
  - an owner exception deletes nothing and logs a Warning;
  - it logs counts only.

  Then add the `IStoredObjectOwner` contract and the sweeper (design D2). Verify: the tests pass.
- [x] 2.6 Update `compose.yaml`: storage settings in the `api-environment` anchor, `api-migrate`/`api-seed` depending on `storage`, and the storage comment. Add `STORAGE_BUCKET` to `.env.example`. Document the storage settings, the MinIO console and "root credentials locally only" in `docs/development.md`. Document the key-prefix and after-commit-delete convention in `backend/src/Modules/README.md`. Verify: `docker compose up --build` reaches healthy from a clean volume, and the bucket exists.
- [x] 2.7 Review group 2 in parallel with `csharp-reviewer`, `security-reviewer` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 3. Platform: image normalisation

- [x] 3.1 Write unit tests for `SkiaImageNormalizer` with images generated in memory (no files from `docs/sources/`):
  - JPEG with EXIF GPS and camera tags gives an output without EXIF, XMP, IPTC or ICC markers;
  - each of the 8 EXIF orientations gives an upright output;
  - PNG and WebP are re-encoded as JPEG;
  - a PDF header, random bytes and an empty stream give `UnsupportedFormat`;
  - a header-only 41 MP PNG gives `TooLarge` without decoding;
  - over `MaxInputBytes` gives `TooLarge`.

  Verify: the tests fail before the implementation.
- [x] 3.2 Write unit tests for the rules:
  - fixed 3:4 within 1 % is centre-cropped to exact 3:4;
  - 1000 × 1000 gives `AspectRatio`;
  - 300 × 400 gives `TooSmall`;
  - 3000 × 4000 gives 1200 × 1600;
  - a license with a long side of 799 gives `TooSmall`;
  - a 2.1:1 license gives `AspectRatio`;
  - a 4000 px license gives a long side of 2000;
  - images are never scaled up.

  Then implement `SharedKernel/Images` (`IImageNormalizer`, `ImageRules`, results) and `Platform/Images/SkiaImageNormalizer` with the concurrency limiter (design D3). Verify: the tests of 3.1 and 3.2 pass.
- [x] 3.3 Review group 3 in parallel with `csharp-reviewer` and `security-reviewer` (decompression bombs, metadata, memory). Fix CRITICAL/HIGH findings.

## 4. Registry: photo data and API

- [x] 4.1 Add `ArquebusierPhotoKind` (`ID`, `LICENSE_FRONT`, `LICENSE_BACK`) to `ArquebusierRegistry.Contracts`, the `ArquebusierPhoto` entity and mapping, and the `AddArquebusierPhotos` migration (design D4). Write database tests:
  - one photo per kind per arquebusier;
  - a unique object key;
  - kind, size and dimension checks;
  - a cascade on arquebusier deletion.

  Verify: the tests, `CodedEnumsTests` and `ModelDriftTests` pass.
- [x] 4.2 Write integration tests for uploads (spec "Arquebusier photos" and "Photo validation and processing"):
  - a FiringChief uploads an ID photo in scope;
  - an Admin replaces a license photo, and the previous object is erased;
  - out of scope gives `404`, with no object stored and no CPU work (the normaliser is not called);
  - a license photo without a license gives `409 photos.noLicense`;
  - a missing file, a disguised PDF, 11 MB and the wrong shape each give `400` with the `file` reason;
  - the arquebusier's version is unchanged after an upload;
  - an upload without `X-XSRF-TOKEN` gives `400 antiforgery.invalid`;
  - the rate limit applies.

  Then implement `ArquebusierPhotoAdministration.UploadAsync` and the `PUT` endpoint (design D5, D6). Verify: the tests pass.
- [x] 4.3 Write integration tests for reading and removing:
  - `GET` returns `image/jpeg` with `no-store`, `nosniff` and a fixed inline file name;
  - an unknown kind slug gives `404`;
  - no photo gives `404 photos.notFound`;
  - a missing object is logged at Error and gives `404`;
  - `DELETE` removes the row and the object, and out of scope gives `404`;
  - signed out gives `401`.

  Then implement `GET`/`DELETE`, `photos` in the detail response and `hasIdPhoto` in list rows. Verify: the tests pass, and the scope guard test covers the new routes.
- [x] 4.4 Write race and outage tests:
  - a license removal against a license photo upload never leaves a license photo without a license;
  - a deletion against an upload leaves no reference, and the orphan is swept;
  - two first uploads of the same kind give one success and `409 photos.modified`;
  - a failed commit erases the new object or leaves it to the sweep;
  - a storage outage gives `503 storage.unavailable` on photo routes, while `PUT /arquebusiers/{id}`, transfer and delete still succeed.

  Then fix what fails. Verify: the tests pass.
- [x] 4.5 Write tests for the changed flows (design D7):
  - removing the license deletes both license photo rows in the same transaction and erases the objects after the commit;
  - changing the type or dates keeps the photos;
  - deleting an arquebusier with three photos erases them, and the photo routes give `404`.

  Then extend `ArquebusierAdministration.UpdateAsync` and `DeleteAsync`, and implement the registry `IStoredObjectOwner` for `registry/photos/`. Verify: the tests pass.
- [x] 4.6 Write audit tests (spec "Registry changes are audited", design D8):
  - an upload records `ArquebusierPhotoUploaded {kind, replaced}`, and a removal records `ArquebusierPhotoRemoved {kind}`;
  - a license removal records `removedPhotos`;
  - a deletion records `photoCount`;
  - rejected uploads record nothing;
  - no entry contains image bytes, object keys or client file names;
  - logs carry no client file name.

  Then implement them. Verify: the tests and `AuditTrailRulesTests` pass.
- [x] 4.7 Regenerate `contracts/openapi.json` (multipart body, image response, the new problem codes) and run the contract check. Verify: the CI contract check passes locally.
- [x] 4.8 Update the domain docs:
  - in `docs/data-model.md`, the ID photo becomes optional and expected (maintainer decision), with the photo rules (formats, sizes, 3:4, EXIF stripping, replacement, removal with the license, renewal keeps photos until replaced);
  - the glossary rows for `idPhoto`, `frontPhoto`, `backPhoto` and `ArquebusierPhotoKind`;
  - in `docs/compliance.md`, how SEC-02, SEC-08 and SEC-12 are met, the worst-case erasure delay (about 2 h), encryption at rest as a go-live check, and the accepted retention of old license photos after a renewal without new photos.

  Verify: the docs match the specs.
- [x] 4.9 Review group 4 in parallel with `csharp-reviewer`, `security-reviewer`, `database-reviewer`, `silent-failure-hunter` and `doc-updater` (no doc contradicts ADR-0005 or the delta specs). Fix CRITICAL/HIGH findings.

## 5. Synthetic seed

- [x] 5.1 Write seeder tests:
  - about half the arquebusiers get an ID photo, and three licensed ones get both license photos;
  - one fixed arquebusier has all three photos and one has none;
  - a rerun creates nothing new and re-puts a missing object;
  - the generated images pass the normaliser and contain no text or metadata.

  Then extend `RegistrySeeder` with generated flat-shape images (design D10). Verify: the tests pass, and `docker compose run --rm api-seed` twice succeeds.
- [x] 5.2 Review group 5 with `csharp-reviewer` (synthetic data only, SEC-11). Fix CRITICAL/HIGH findings.

## 6. Frontend: `PhotoUpload` composite

- [x] 6.1 Regenerate the orval client and check the multipart mutation. Write `apiFetch` tests: a `FormData` body keeps the anti-forgery header and has no explicit `Content-Type`. Then adjust the mutator if needed. Verify: `npm run typecheck` and the tests pass.
- [x] 6.2 Write `PhotoUpload` tests (Vitest, Testing Library, axe):
  - empty and with-photo states, with the alt text;
  - choosing a non-image gives `unsupportedFormat`;
  - an image that is too small gives `tooSmall`, with no `onUpload` call;
  - the crop dialog opens with a centred 3:4 selection and is keyboard operable (move and resize with arrows, rotate buttons, Escape returns focus);
  - "Use photo" calls `onUpload` with a JPEG blob of the expected dimensions;
  - pending and success are announced in a live region, and an error shows the translated reason;
  - `img` `onError` shows `loadFailed`;
  - remove goes through `ConfirmDialog`, and cancel keeps the photo.

  Then implement the composite (design D9). Verify: the tests pass, and lint passes the primitive boundary.
- [x] 6.3 Add the `PhotoUpload` stories (empty, photo, uploading, error, load failure, crop dialog; with a synthetic generated image) with axe checks, and register the composite in the catalogue test. Add the `ui:photoUpload.*` keys in es-ES, ca-ES-valencia and en. Add the composite to `docs/design/README.md`. Verify: `npm run build-storybook`, the catalogue test and `npm run check-i18n` pass.
- [x] 6.4 Review group 6 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 7. Frontend: registry photo screens

- [x] 7.1 Write tests for the detail page (MSW):
  - the ID photo shows in *Personal data*, with the versioned URL;
  - license photos show in *License*, and are disabled with the `needsLicense` hint when no license is stored;
  - an upload and a removal refresh the detail;
  - `409 photos.noLicense`, `503 storage.unavailable` and `400 file` reasons show translated messages;
  - saving a form that removes a license with photos asks first, and cancelling keeps the form;
  - after changing the license dates with photos, the replace reminder appears.

  Then implement them. Verify: the tests pass.
- [x] 7.2 Write tests for the register page:
  - the optional ID photo is uploaded after the create;
  - when the upload fails after registering, the detail page opens with `uploadFailedAfterRegister` and the reason;
  - no photo means no upload request.

  Then implement them. Verify: the tests pass.
- [x] 7.3 Write list tests: rows without an ID photo show "No ID photo" with icon and text, rows with one show nothing, and no image requests are made. Then implement them. Verify: the tests pass.
- [x] 7.4 Add the `registry:photos.*`, `registry:license.removePhotosConfirm.*`, `registry:license.replacePhotosHint`, `registry:errors.*` and `registry:validation.file.*` keys in es-ES, ca-ES-valencia and en (design D9). Verify: `npm run check-i18n` and the translation completeness test pass.
- [x] 7.5 Review group 7 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 8. Verification

- [x] 8.1 Write the Playwright suite `e2e/photos.spec.ts` against the seeded compose environment:
  - an Admin uploads an ID photo through the crop dialog with a generated synthetic PNG and sees it on the detail page;
  - the "No ID photo" marker disappears from the list;
  - a license photo is replaced and then removed after confirming;
  - a FiringChief requesting another comparsa's photo URL gets 404;
  - axe finds no violations on the crop dialog;
  - the upload also works in the published API image (SkiaSharp native assets).

  Verify: the suite passes in Chromium, Firefox and WebKit.
- [x] 8.2 Run `verification-loop`:
  - backend and frontend build, format, lint, typecheck, `check-i18n`;
  - all tests with coverage of at least 80 % for the new platform services and registry photo code;
  - the contract check;
  - a security grep (no secrets, no real images, no personal data in logs and fixtures);
  - a diff review.

  Run `e2e-runner` and `pr-test-analyzer` over the change. Verify: a PASS report, and findings fixed or recorded as follow-ups in design.md.
- [ ] 8.3 Manual release checks, recorded in the PR:
  - take an ID photo and a license photo with the camera on a real Android phone and on an iPhone (HEIC);
  - crop with a screen reader (NVDA or VoiceOver);
  - check that the stored objects in MinIO have no EXIF (`exiftool`).

  Verify: the results noted in the PR description.

  Deferred by the maintainer (2026-10-01) to the exhaustive regression once the basic features are
  done; tracked in `docs/mvp.md`. The automated stand-ins: Playwright checks EXIF stripping,
  orientation, rotation and keyboard cropping in Chromium, Firefox and WebKit.
