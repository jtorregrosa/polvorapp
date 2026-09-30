# 0005. Photos in S3-compatible private object storage

- Status: Accepted
- Date: 2026-09-30

## Context

Each arquebusier has an ID photo (printed on the badge) and license front/back photos. These are
sensitive (SEC-02) and must be printable (NFR-15). Volume is small (a few GB at most).

## Decision

- Store files in **S3-compatible object storage**: MinIO in local/dev containers, any
  S3-compatible provider (EU region) in production. Private bucket, encrypted at rest.
- Files are never public: the API streams them or issues **short-lived pre-signed URLs** after
  an authorisation check.
- On upload the server **re-encodes** images (JPEG/WebP), **strips EXIF** (SEC-12), enforces
  max size and, for the ID photo, a fixed aspect ratio and minimum resolution. Cropping happens
  in the browser before upload.
- Image processing with a permissively licensed library (e.g. SkiaSharp, MIT).
- Previous license photos are deleted on renewal; all photos are deleted when an arquebusier is deleted (BR-14).

## Consequences

- Database stays small; backups split into DB dump + bucket sync.
- Provider-agnostic thanks to the S3 API.

## Alternatives considered

- **Files in the database** — simpler backups but bloated DB and slower.
- **Local filesystem volume** — ties the app to one host; harder to back up and scale.
